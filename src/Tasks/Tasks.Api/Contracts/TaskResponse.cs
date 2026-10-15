using Tasks.Api.Domain;

namespace Tasks.Api.Contracts;

/// <summary>
/// Представление задачи в ответах API.
/// </summary>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Priority Priority,
    uint RowVersion)
{
    public static TaskResponse From(TaskItem item) => new(
        item.Id,
        item.Title,
        item.IsCompleted,
        item.CreatedAt,
        item.CompletedAt,
        item.Priority,
        item.RowVersion);
}
