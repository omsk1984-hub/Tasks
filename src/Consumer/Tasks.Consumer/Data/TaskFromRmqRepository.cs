using Microsoft.EntityFrameworkCore;
using Tasks.Consumer.Messaging;

namespace Tasks.Consumer.Data;

/// <summary>
/// Запись обработанных событий задач в <c>tasks_from_rmq</c>.
/// Идемпотентность обеспечивается атомарным
/// <c>INSERT ... ON CONFLICT (task_id) DO NOTHING</c>: при повторной доставке
/// того же события строка не создаётся повторно, а метод возвращает <c>false</c>.
/// </summary>
public sealed class TaskFromRmqRepository(ConsumerDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Пытается вставить событие. Возвращает <c>true</c>, если строка добавлена,
    /// и <c>false</c>, если событие уже было обработано ранее (дубликат).
    /// </summary>
    public async Task<bool> TryInsertAsync(
        TaskCompletedEvent @event,
        string? routingKey,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        var entity = new TaskFromRmq
        {
            TaskId = @event.TaskId,
            Title = @event.Title,
            // Priority в БД хранится строкой (varchar(16)): enum -> имя ("Low"/"Medium"/"High").
            Priority = @event.Priority.ToString(),
            CompletedAt = @event.CompletedAt,
            ProcessedAt = clock.GetUtcNow(),
            RoutingKey = routingKey,
            RawPayload = rawPayload
        };

        // ON CONFLICT DO NOTHING — атомарно отсекает повторную вставку одного и того же
        // task_id, возвращая 0 затронутых строк для дубликата.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO tasks_from_rmq
                 (task_id, title, priority, completed_at, processed_at, routing_key, raw_payload)
             VALUES
                 ({entity.TaskId}, {entity.Title}, {entity.Priority}, {entity.CompletedAt},
                  {entity.ProcessedAt}, {entity.RoutingKey}, {entity.RawPayload}::jsonb)
             ON CONFLICT (task_id) DO NOTHING
             """,
            cancellationToken);

        return affected > 0;
    }
}
