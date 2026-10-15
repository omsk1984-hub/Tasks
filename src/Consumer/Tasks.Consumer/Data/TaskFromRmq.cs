namespace Tasks.Consumer.Data;

/// <summary>
/// Проекция события <c>task.completed</c>, полученного из RabbitMQ.
/// Отдельная таблица <c>tasks_from_rmq</c> хранит уже обработанные consumer-ом
/// сообщения. <see cref="TaskId"/> — естественный ключ идемпотентности:
/// повторная доставка одного события не создаёт новую строку.
/// </summary>
public sealed class TaskFromRmq
{
    /// <summary>Id задачи из события (= AMQP MessageId). Первичный ключ.</summary>
    public Guid TaskId { get; set; }

    /// <summary>Заголовок задачи на момент завершения.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Приоритет (Low/Medium/High) — хранится строкой.</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>Время завершения задачи (из события).</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>Время, когда consumer записал строку (аудит/диагностика).</summary>
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>Routing key сообщения (для диагностики).</summary>
    public string? RoutingKey { get; set; }

    /// <summary>Оригинальное тело сообщения (jsonb) — для разбора инцидентов.</summary>
    public string? RawPayload { get; set; }
}
