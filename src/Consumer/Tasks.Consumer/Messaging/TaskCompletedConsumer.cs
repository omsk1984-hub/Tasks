using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tasks.Consumer.Data;

namespace Tasks.Consumer.Messaging;

/// <summary>
/// Подписчик на события <c>task.completed</c> из RabbitMQ.
///
/// Идемпотентность: сообщение подтверждается (ack) только после успешной записи
/// в <c>tasks_from_rmq</c>, а сама запись защищена ON CONFLICT (task_id) DO NOTHING —
/// повторная доставка не создаёт дубликаты.
///
/// Очередь объявляет сам consumer (durable) и привязывает её к обменнику
/// <c>task.events</c> по ключу <c>task.completed</c>, т.к. publisher создаёт только обменник.
/// </summary>
public sealed class TaskCompletedConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<TaskCompletedConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Consumer запущен. Exchange={Exchange}, Queue={Queue}, RoutingKey={RoutingKey}.",
            _options.Exchange, _options.Queue, _options.RoutingKey);

        try
        {
            await StartConsumingAsync(stoppingToken);

            // Держим сервис активным до сигнала остановки хоста.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Нормальная остановка.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Consumer остановлен из-за ошибки.");
            throw;
        }
        finally
        {
            await DisposeChannelAsync();
        }

        logger.LogInformation("Consumer остановлен.");
    }

    private async Task StartConsumingAsync(CancellationToken cancellationToken)
    {
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

        // Идемпотентная декларация топологии: обменник (создаётся и publisher-ом),
        // очередь и привязка — обязанность consumer-стороны.
        await _channel.ExchangeDeclareAsync(
            exchange: _options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: _options.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue: _options.Queue,
            exchange: _options.Exchange,
            routingKey: _options.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        // Back-pressure: не более N неподтверждённых сообщений одновременно.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: (ushort)Math.Max(1, _options.PrefetchCount),
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: _options.Queue,
            autoAck: false, // подтверждаем вручную после успешной записи в БД
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Подписка оформлена: очередь '{Queue}' слушает '{Exchange}' по ключу '{RoutingKey}'.",
            _options.Queue, _options.Exchange, _options.RoutingKey);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel;
        if (channel is null)
        {
            return;
        }

        var body = ea.Body.ToArray();
        var rawPayload = Encoding.UTF8.GetString(body);

        try
        {
            var @event = JsonSerializer.Deserialize<TaskCompletedEvent>(body, JsonOptions)
                ?? throw new InvalidOperationException("Пустое тело сообщения task.completed.");

            // Новый scope на каждое сообщение: DbContext/репозиторий — scoped.
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<TaskFromRmqRepository>();

            var inserted = await repository.TryInsertAsync(
                @event, ea.RoutingKey, rawPayload, ea.CancellationToken);

            if (inserted)
            {
                logger.LogInformation(
                    "Событие task.completed для задачи {TaskId} записано в tasks_from_rmq.", @event.TaskId);
            }
            else
            {
                logger.LogInformation(
                    "Событие task.completed для задачи {TaskId} уже обработано (дубликат, пропущен).", @event.TaskId);
            }

            // Ack только после успешной записи (или осознанного пропуска дубликата).
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // nack без requeue: не зацикливаем «ядовитое» сообщение бесконечно.
            logger.LogError(ex,
                "Ошибка обработки сообщения task.completed (deliveryTag={DeliveryTag}). Сообщение не будет переобработано.",
                ea.DeliveryTag);

            try
            {
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception nackEx)
            {
                logger.LogError(nackEx, "Не удалось отправить nack для deliveryTag={DeliveryTag}.", ea.DeliveryTag);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Останавливаю consumer...");
        await base.StopAsync(cancellationToken);
        await DisposeChannelAsync();
    }

    private async Task DisposeChannelAsync()
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
