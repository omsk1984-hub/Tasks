using Tasks.Api.Domain;

namespace Tasks.Api.Messaging;

/// <summary>
/// Сообщение, публикуемое в RabbitMQ при завершении задачи.
/// JSON-формат (camelCase):
/// { "taskId": "...", "title": "...", "completedAt": "...", "priority": "High" }
/// </summary>
public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    Priority Priority);
