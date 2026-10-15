namespace Tasks.Consumer.Messaging;

/// <summary>
/// Настройки подключения к RabbitMQ и топологии обменника/очереди.
/// Секция конфигурации: "RabbitMq".
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string User { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Обменник из ТЗ: task.events (topic, durable).</summary>
    public string Exchange { get; set; } = "task.events";

    /// <summary>Routing key из ТЗ: task.completed.</summary>
    public string RoutingKey { get; set; } = "task.completed";

    /// <summary>Очередь consumer-а (durable).</summary>
    public string Queue { get; set; } = "tasks.task.completed";

    /// <summary>Ограничение числа неподтверждённых сообщений на канале (back-pressure).</summary>
    public int PrefetchCount { get; set; } = 10;
}
