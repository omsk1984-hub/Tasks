using Microsoft.Extensions.Options;
using Npgsql.Replication.PgOutput.Messages;
using Tasks.CDC.Messaging;
using Tasks.CDC.Replication;

namespace Tasks.CDC;

/// <summary>
/// Основной воркер CDC: читает поток логической репликации outbox_messages,
/// декодирует INSERT, публикует событие в RabbitMQ и только затем подтверждает LSN.
///
/// Гарантия at-least-once: при ошибке публикации LSN НЕ подтверждается — после
/// реконнекта изменение будет прочитано повторно. Дедупликация (lsn, outbox_id)
/// в <see cref="ReplicationPositionStore"/> защищает от повторной публикации уже
/// обработанных изменений при переигрывании WAL.
/// </summary>
public sealed class CdcWorker(
    LogicalReplicationReader reader,
    PublicationInitializer publicationInitializer,
    OutboxInsertDecoder decoder,
    ReplicationPositionStore positionStore,
    IEventPublisher publisher,
    IOptions<ReplicationOptions> options,
    ILogger<CdcWorker> logger) : BackgroundService
{
    private readonly ReplicationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CDC-воркер запущен. Публикация={Publication}, слот={Slot}, таблица={Table}.",
            _options.PublicationName, _options.SlotName, _options.OutboxTable);

        await positionStore.EnsureSchemaAsync(stoppingToken);
        await publicationInitializer.EnsurePublicationAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Нормальная остановка.
                break;
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));
                logger.LogError(ex,
                    "Поток репликации прерван, повтор через {Delay} с.", delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.LogInformation("CDC-воркер остановлен.");
    }

    /// <summary>Одна сессия чтения потока: старт → чтение → обработка до обрыва/отмены.</summary>
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var session = reader;
        await reader.StartAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await reader.ReadAsync(cancellationToken);
            if (message is null)
            {
                logger.LogWarning("Поток репликации завершился (null). Переподключаюсь.");
                return;
            }

            // Begin/Commit/Relation обрабатывает сам Npgsql; нас интересует Insert.
            if (message is InsertMessage insert)
            {
                await HandleInsertAsync(insert, cancellationToken);
            }
        }
    }

    private async Task HandleInsertAsync(InsertMessage insert, CancellationToken cancellationToken)
    {
        var decoded = await decoder.TryDecodeAsync(insert, cancellationToken);
        if (decoded is null)
        {
            return;
        }

        // NpgsqlLogSequenceNumber -> ulong (для хранения в cdc_processed.lsn).
        var lsn = (ulong)insert.WalStart;

        // Дедупликация: пропускаем уже обработанные (lsn, id).
        if (!await positionStore.TryMarkProcessedAsync(lsn, decoded.Id, cancellationToken))
        {
            logger.LogDebug(
                "Изменение (lsn={Lsn}, id={Id}) уже обработано — пропускаю.", lsn, decoded.Id);
            return;
        }

        // Публикуем ДО подтверждения LSN: при сбое publish LSN не продвинется,
        // и изменение будет прочитано заново.
        await publisher.PublishAsync(decoded, cancellationToken);

        // Подтверждаем позицию только после успешной публикации.
        await reader.ConfirmLsnAsync(lsn, cancellationToken);
    }
}
