using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Tasks.CDC.Replication;

namespace Tasks.CDC.Messaging;

/// <summary>
/// Публикация декодированных outbox-событий в RabbitMQ.
///
/// Публикует уже готовый JSON из outbox «как есть» (формат контракта совпадает с
/// тем, что ожидает Tasks.Consumer), в обменник task.events с routing key из
/// event_type (= task.completed), persistent, MessageId = TaskId.
///
/// В отличие от прежней прямой публикации из API, здесь подтверждение LSN
/// продвигается только после успешного publish — поэтому публикация синхронная
/// (ожидаем завершения BasicPublishAsync), а при недоступности брокера выбрасываем
/// исключение, чтобы воркер не подтвердил LSN и повторил позже.
/// </summary>
public sealed class RabbitMqPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>Публикует событие. Бросает исключение при неудаче (для повтора из CDC).</summary>
    public async Task PublishAsync(DecodedOutboxEvent message, CancellationToken cancellationToken)
    {
        var channel = await GetOrCreateChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(message.RawPayload);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString(),
            Type = message.EventType,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: message.EventType,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Опубликовано событие {EventType} (outbox id={Id}) в {Exchange}/{RoutingKey}.",
            message.EventType, message.Id, _options.Exchange, message.EventType);
    }

    /// <summary>Проверка доступности брокера для health-check'а.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetOrCreateChannelAsync(cancellationToken);
            return _channel is { IsOpen: true };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Активное соединение для health-check-а (AddRabbitMQ ожидает IConnection).</summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        await GetOrCreateChannelAsync(cancellationToken);
        return _connection!;
    }

    private async Task<IChannel> GetOrCreateChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            await DisposeChannelUnsafeAsync();

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.User,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Идемпотентная декларация обменника (topic, durable): сообщения не теряются
            // при рестарте брокера. Очереди/биндинги объявляет consumer.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Установлено подключение к RabbitMQ {Host}:{Port}, обменник '{Exchange}' объявлен.",
                _options.Host, _options.Port, _options.Exchange);

            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            await DisposeChannelUnsafeAsync();
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
        }
    }

    private async Task DisposeChannelUnsafeAsync()
    {
        if (_channel is not null)
        {
            try
            {
                if (_channel.IsOpen)
                {
                    await _channel.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ошибка при закрытии канала RabbitMQ (игнорируется).");
            }
            finally
            {
                _channel.Dispose();
                _channel = null;
            }
        }

        if (_connection is not null)
        {
            try
            {
                if (_connection.IsOpen)
                {
                    await _connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ошибка при закрытии соединения RabbitMQ (игнорируется).");
            }
            finally
            {
                _connection.Dispose();
                _connection = null;
            }
        }
    }
}
