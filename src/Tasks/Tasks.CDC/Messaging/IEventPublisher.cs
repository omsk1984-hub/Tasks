using Tasks.CDC.Replication;

namespace Tasks.CDC.Messaging;

/// <summary>
/// Абстракция публикации декодированного outbox-события в брокер.
/// Выделена, чтобы CdcWorker не зависел от RabbitMQ напрямую (DIP) и был тестируем.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Публикует событие. Бросает исключение при неудаче — воркер не подтверждает
    /// LSN и повторит публикацию позже (at-least-once).
    /// </summary>
    Task PublishAsync(DecodedOutboxEvent message, CancellationToken cancellationToken);
}
