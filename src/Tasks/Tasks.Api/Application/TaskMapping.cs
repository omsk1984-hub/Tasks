using Tasks.Api.Domain;

namespace Tasks.Api.Application;

/// <summary>Маппинг доменной сущности в Application-DTO.</summary>
internal static class TaskMapping
{
    public static TaskDto ToDto(this TaskItem item) => new(
        item.Id,
        item.Title.Value,
        item.IsCompleted,
        item.CreatedAt,
        item.CompletedAt,
        item.Priority,
        item.RowVersion);
}
