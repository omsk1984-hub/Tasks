namespace Tasks.Api.Messaging;

/// <summary>
/// Настройки подключения к RabbitMQ и топологии обменника.
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

    /// <summary>Обменник из ТЗ: task.events.</summary>
    public string Exchange { get; set; } = "task.events";

    /// <summary>Routing key из ТЗ: task.completed.</summary>
    public string RoutingKey { get; set; } = "task.completed";

    /// <summary>Таймаут ожидания завершения in-flight публикаций при shutdown (сек).</summary>
    public int ShutdownDrainTimeoutSeconds { get; set; } = 4;
}
