namespace Tasks.CDC.Messaging;

/// <summary>
/// Настройки публикации в RabbitMQ. Секция конфигурации: <c>RabbitMq</c>.
/// Те же значения, что использовал API до перехода на outbox.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5673;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Обменник из ТЗ (topic, durable).</summary>
    public string Exchange { get; set; } = "task.events";

    /// <summary>Таймаут ожидания in-flight публикаций при graceful shutdown (секунды).</summary>
    public int ShutdownDrainTimeoutSeconds { get; set; } = 4;
}
