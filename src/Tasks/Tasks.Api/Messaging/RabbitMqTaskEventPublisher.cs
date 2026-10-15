using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tasks.Api.Messaging;

/// <summary>
/// Публикация событий задач в RabbitMQ (только publish, consumer не нужен).
///
/// Особенности реализации по требованиям ТЗ:
///  - публикация не блокирует основной поток: запрос complete не ждёт publisher confirms
///    (publisher confirms отключены, а если брокер недоступен — исключение не всплывает);
///  - fail silently + лог: если RabbitMQ недоступен, задача всё равно завершена в БД,
///    а в лог пишется ошибка публикации;
///  - graceful shutdown: при остановке приложения канал/соединение закрываются корректно,
///    а уже начатые публикации успевают завершиться (drain с таймаутом).
/// </summary>
public sealed class RabbitMqTaskEventPublisher : ITaskEventPublisher, IBrokerHealthProbe, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTaskEventPublisher> _logger;

    // Ленивая инициализация соединения/канала с защитой от гонок.
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    // Учёт "в полёте" публикаций: нужен, чтобы при shutdown дождаться их завершения.
    // Заменяет счётчик на Interlocked, чтобы не тащить лишние зависимости.
    private int _inFlight;
    private volatile bool _shuttingDown;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RabbitMqTaskEventPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTaskEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_shuttingDown)
        {
            _logger.LogWarning(
                "Публикация события task.completed для задачи {TaskId} пропущена: приложение останавливается.",
                @event.TaskId);
            return;
        }

        Interlocked.Increment(ref _inFlight);
        try
        {
            var channel = await GetOrCreateChannelAsync(cancellationToken);

            var body = JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);

            var props = new BasicProperties
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = @event.TaskId.ToString(),
                Type = "task.completed",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            // Без ожидания publisher confirms: fire-and-forget по разрешению ТЗ.
            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: _options.RoutingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Опубликовано событие task.completed для задачи {TaskId} (exchange={Exchange}, routingKey={RoutingKey}).",
                @event.TaskId, _options.Exchange, _options.RoutingKey);
        }
        catch (Exception ex)
        {
            // Fail silently: задача уже завершена в БД, просто логируем.
            _logger.LogError(ex,
                "Не удалось опубликовать событие task.completed для задачи {TaskId}. " +
                "Задача завершена, сообщение потеряно (RabbitMQ недоступен).",
                @event.TaskId);
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0 && _shuttingDown)
            {
                _drained.TrySetResult();
            }
        }
    }

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

    /// <summary>
    /// Возвращает активное соединение для health-check'а (AddRabbitMQ ожидает IConnection).
    /// Гарантирует инициализацию соединения и канала.
    /// </summary>
    public async Task<RabbitMQ.Client.IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
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

            // Чистим возможно мёртвые ресурсы перед реконнектом.
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

            // Идемпотентная декларация обменника (topic, durable) — чтобы сообщения не терялись
            // при рестарте брокера. Очереди/биндинги consumer-сторона заводит сама.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Установлено подключение к RabbitMQ {Host}:{Port}, обменник '{Exchange}' объявлен.",
                _options.Host, _options.Port, _options.Exchange);

            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Корректное закрытие при остановке приложения:
    /// 1) перестаём принимать новые публикации;
    /// 2) ждём завершения уже начатых (drain с таймаутом);
    /// 3) закрываем канал и соединение.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _shuttingDown = true;

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.ShutdownDrainTimeoutSeconds));

        if (Volatile.Read(ref _inFlight) > 0)
        {
            _logger.LogInformation(
                "Ожидание завершения {Count} начатых публикаций в RabbitMQ (таймаут {Timeout} c)...",
                Volatile.Read(ref _inFlight), timeout.TotalSeconds);

            var completed = await Task.WhenAny(_drained.Task, Task.Delay(timeout));
            if (completed != _drained.Task)
            {
                _logger.LogWarning(
                    "Таймаут ожидания публикаций истёк, осталось незавершённых: {Count}.",
                    Volatile.Read(ref _inFlight));
            }
        }

        await DisposeChannelUnsafeAsync();
        _initLock.Dispose();

        _logger.LogInformation("RabbitMQ: канал и соединение закрыты.");
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
                _logger.LogDebug(ex, "Ошибка при закрытии канала RabbitMQ (игнорируется).");
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
                _logger.LogDebug(ex, "Ошибка при закрытии соединения RabbitMQ (игнорируется).");
            }
            finally
            {
                _connection.Dispose();
                _connection = null;
            }
        }
    }
}
