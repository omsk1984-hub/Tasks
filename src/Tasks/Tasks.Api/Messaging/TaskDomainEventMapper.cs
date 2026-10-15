using Tasks.Api.Domain.Events;

namespace Tasks.Api.Messaging;

/// <summary>
/// Маппер доменных событий в integration-сообщения брокера.
/// Сохраняет разделение DDD-домена и инфраструктурного формата обмена.
/// </summary>
public static class TaskDomainEventMapper
{
    public static TaskCompletedEvent ToIntegrationEvent(this TaskCompletedDomainEvent domainEvent) => new(
        domainEvent.TaskId,
        domainEvent.Title,
        domainEvent.CompletedAt,
        domainEvent.Priority);
}
