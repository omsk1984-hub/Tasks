namespace Tasks.Consumer.Messaging;

/// <summary>
/// Контракт сообщения, публикуемого API при завершении задачи.
/// JSON-формат (camelCase, enum строкой):
/// { "taskId": "...", "title": "...", "completedAt": "...", "priority": "High" }
/// Тип намеренно продублирован в consumer-проекте, чтобы не тянуть ссылку на API.
/// </summary>
public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    string Priority);
