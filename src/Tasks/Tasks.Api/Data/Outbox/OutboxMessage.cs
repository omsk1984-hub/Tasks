namespace Tasks.Api.Data.Outbox;

/// <summary>
/// Transactional outbox: строка события, записанная в той же транзакции, что и
/// изменение агрегата. Читается CDC-воркером (Npgsql logical replication) и
/// публикуется в RabbitMQ. Строки хранятся как журнал исходящих событий
/// (для реализации/аудита), автоматического удаления нет.
/// </summary>
public sealed class OutboxMessage
{
    // Приватный конструктор без параметров — только для материализации EF Core.
    private OutboxMessage()
    {
        EventType = null!;
        Payload = null!;
    }

    private OutboxMessage(Guid id, string eventType, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Идентификатор сообщения (= TaskId для идемпотентности события).</summary>
    public Guid Id { get; private set; }

    /// <summary>Тип события (= routing key): например, <c>task.completed</c>.</summary>
    public string EventType { get; private set; }

    /// <summary>Готовый JSON контракта (camelCase, enum строкой).</summary>
    public string Payload { get; private set; }

    /// <summary>Доменное время события (CompletedAt).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Время записи в outbox (аудит).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Создаёт сообщение outbox. Ident Id передаётся вызывающим слоем
    /// (для task.completed совпадает с TaskId).
    /// </summary>
    public static OutboxMessage Create(Guid id, string eventType, string payload, DateTimeOffset occurredAt)
        => new(id, eventType, payload, occurredAt);
}
