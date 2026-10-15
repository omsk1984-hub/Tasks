using Microsoft.Extensions.Options;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;
using NpgsqlTypes;

namespace Tasks.CDC.Replication;

/// <summary>
/// Читатель логической репликации PostgreSQL поверх высокоуровневого API Npgsql.
///
/// Npgsql сам разбирает протокол pgoutput и отдаёт типизированные сообщения
/// (<see cref="PgOutputReplicationMessage"/>): Begin/Relation/Insert/Commit и т.п.
///
/// Подтверждение позиции: <see cref="ConfirmLsn"/> выставляет LastFlushedLsn и
/// отправляет StandbyStatusUpdate — вызывается воркером ТОЛЬКО после успешной
/// публикации события в RabbitMQ (гарантия at-least-once).
/// </summary>
public sealed class LogicalReplicationReader(
    IOptions<ReplicationOptions> options,
    ILogger<LogicalReplicationReader> logger) : IAsyncDisposable
{
    private readonly ReplicationOptions _options = options.Value;

    private LogicalReplicationConnection? _connection;
    private PgOutputReplicationSlot? _slot;

    /// <summary>Поток сообщений pgoutput (устанавливается в <see cref="StartAsync"/>).</summary>
    private IAsyncEnumerator<PgOutputReplicationMessage>? _enumerator;

    /// <summary>Последний полученный из потока LSN (WalStart текущего сообщения).</summary>
    public ulong LastReceivedLsn { get; private set; }

    /// <summary>Последний подтверждённый (опубликованный) LSN.</summary>
    public ulong LastConfirmedLsn { get; private set; }

    /// <summary>
    /// Открывает соединение, создаёт/переиспользует слот pgoutput и запускает
    /// поток репликации по указанной публикации.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // LogicalReplicationConnection сам открывает соединение в режиме репликации;
        // отдельно выставлять ReplicationMode в строке подключения не нужно.
        _connection = new LogicalReplicationConnection(_options.ConnectionString);
        await _connection.Open(cancellationToken);

        // Слот создаётся идемпотентно (Npgsql сам проверяет существование? нет —
        // создаём только если его нет, иначе переиспользуем существующий).
        var slotExists = await SlotExistsAsync(_options.SlotName, cancellationToken);
        if (!slotExists)
        {
            _slot = await _connection.CreatePgOutputReplicationSlot(
                _options.SlotName,
                temporarySlot: false,
                slotSnapshotInitMode: null,
                twoPhase: false,
                cancellationToken: cancellationToken);
            logger.LogInformation("Создан логический слот pgoutput '{Slot}'.", _options.SlotName);
        }
        else
        {
            _slot = new PgOutputReplicationSlot(_options.SlotName);
            logger.LogInformation("Логический слот '{Slot}' уже существует, переиспользую.", _options.SlotName);
        }

        // Читаем изменения только из публикации на outbox_messages.
        var pgOptions = new PgOutputReplicationOptions(
            _options.PublicationName,
            PgOutputProtocolVersion.V1);

        var enumerable = _connection.StartReplication(
            _slot,
            pgOptions,
            cancellationToken);

        _enumerator = enumerable.GetAsyncEnumerator(cancellationToken);

        logger.LogInformation(
            "Поток репликации запущен: slot={Slot}, publication={Publication}.",
            _options.SlotName, _options.PublicationName);
    }

    /// <summary>
    /// Переходит к следующему сообщению потока. Возвращает null, если поток завершился.
    /// Обновляет <see cref="LastReceivedLsn"/> по WalStart сообщения.
    /// </summary>
    public async Task<PgOutputReplicationMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_enumerator is null)
        {
            throw new InvalidOperationException("Репликация не запущена. Вызовите StartAsync.");
        }

        if (!await _enumerator.MoveNextAsync())
        {
            return null;
        }

        var message = _enumerator.Current;
        LastReceivedLsn = (ulong)message.WalStart;

        return message;
    }

    /// <summary>
    /// Подтверждает позицию репликации: выставляет LastFlushedLsn/LastAppliedLsn и
    /// отправляет StandbyStatusUpdate. Вызывается ТОЛЬКО после успешной публикации
    /// в Rabbit — до этого сервер не продвинет confirmed_flush_lsn слота.
    /// </summary>
    public async Task ConfirmLsnAsync(ulong lsn, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        var lsnNumber = new NpgsqlLogSequenceNumber(lsn);
        _connection.SetReplicationStatus(lsnNumber);
        _connection.LastAppliedLsn = lsnNumber;
        _connection.LastFlushedLsn = lsnNumber;

        await _connection.SendStatusUpdate(cancellationToken);
        LastConfirmedLsn = lsn;
    }

    /// <summary>Проверка существования слота (через отдельное нерепликационное соединение).</summary>
    private async Task<bool> SlotExistsAsync(string slot, CancellationToken cancellationToken)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT 1 FROM pg_replication_slots WHERE slot_name = @slot;", conn);
        cmd.Parameters.AddWithValue("slot", NpgsqlDbType.Text, slot);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_enumerator is not null)
        {
            try
            {
                await _enumerator.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ошибка при закрытии потока репликации (игнорируется).");
            }
            finally
            {
                _enumerator = null;
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ошибка при закрытии соединения репликации (игнорируется).");
            }
            finally
            {
                _connection = null;
            }
        }
    }
}
