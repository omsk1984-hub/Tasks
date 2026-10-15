using Tasks.Api.Domain;

namespace Tasks.Api.Application;

/// <summary>
/// DTO уровня Application: представление задачи для use-case.
/// Presentation маппит его в HTTP-контракт (TaskResponse).
/// </summary>
public sealed record TaskDto(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Priority Priority,
    uint RowVersion);
