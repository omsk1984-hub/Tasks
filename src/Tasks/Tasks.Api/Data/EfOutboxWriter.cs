using System.Text.Json;
using Tasks.Api.Application.Abstractions;
using Tasks.Api.Data.Outbox;
using Tasks.Core.Messaging;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core реализация <see cref="IOutboxWriter"/>: сериализует integration-событие
/// в JSON (единый формат с потребителем — <see cref="JsonSerializationDefaults"/>)
/// и добавляет строку в таблицу outbox_messages. Сохранение выполняет UnitOfWork
/// в той же транзакции, что и изменение агрегата.
/// </summary>
public sealed class EfOutboxWriter(TasksDbContext db) : IOutboxWriter
{
    // Те же настройки, что у продюсера/потребителя: camelCase + enum строкой.
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializationDefaults.Options;

    /// <summary>Тип события = routing key в RabbitMQ.</summary>
    public const string TaskCompletedEventType = "task.completed";

    public void Add(TaskCompletedEvent @event)
    {
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        // Id outbox = TaskId: идемпотентность события (PK по task_id).
        var message = OutboxMessage.Create(
            @event.TaskId,
            TaskCompletedEventType,
            payload,
            @event.CompletedAt);

        db.OutboxMessages.Add(message);
    }
}
