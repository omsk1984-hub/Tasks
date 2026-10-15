namespace Tasks.Api.Domain.Events;

/// <summary>
/// Маркер доменного события. Доменные события описывают факты, значимые для
/// бизнеса, и отделены от integration-сообщений, публикуемых в брокер.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Момент возникновения события (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
