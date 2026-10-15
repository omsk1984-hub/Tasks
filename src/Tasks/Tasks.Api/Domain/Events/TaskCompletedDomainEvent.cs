namespace Tasks.Api.Domain.Events;

/// <summary>
/// Доменное событие: задача завершена. Поднимается сущностью <see cref="TaskItem"/>
/// в момент вызова <c>Complete</c>. Внешний слой маппит его в integration-сообщение
/// брокера (TaskCompletedEvent), сохраняя разделение домена и инфраструктуры.
/// </summary>
public sealed record TaskCompletedDomainEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset CompletedAt,
    Priority Priority) : IDomainEvent
{
    public DateTimeOffset OccurredAt => CompletedAt;
}
