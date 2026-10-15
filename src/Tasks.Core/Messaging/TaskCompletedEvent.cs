using Tasks.Core;

namespace Tasks.Core.Messaging;

/// <summary>
/// Общий контракт сообщения, публикуемого API при завершении задачи.
/// JSON-формат (camelCase, enum строкой):
/// { "taskId": "...", "title": "...", "completedAt": "...", "priority": "High" }.
///
/// Тип вынесен в Tasks.Core, чтобы продюсер (Tasks.Api) и потребитель (Tasks.Consumer)
/// использовали единую модель и формат сериализации (см. <see cref="JsonSerializationDefaults"/>).
/// </summary>
public sealed record TaskCompletedEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    Priority Priority);
