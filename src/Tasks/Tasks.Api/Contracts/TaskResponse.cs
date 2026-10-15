using Tasks.Api.Application;
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
    public static TaskResponse From(TaskDto dto) => new(
        dto.Id,
        dto.Title,
        dto.IsCompleted,
        dto.CreatedAt,
        dto.CompletedAt,
        dto.Priority,
        dto.RowVersion);
}
