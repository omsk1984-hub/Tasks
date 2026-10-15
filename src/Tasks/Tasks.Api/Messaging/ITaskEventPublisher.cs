namespace Tasks.Api.Messaging;

/// <summary>
/// Публикация integration-событий задач в брокер.
/// Абстракция нужна, чтобы Application не зависел от RabbitMQ, а тесты могли
/// подменить реализацию.
/// </summary>
public interface ITaskEventPublisher
{
    /// <summary>
    /// Публикует сообщение о завершении задачи. Реализация НЕ должна выбрасывать
    /// исключение при недоступности брокера (fail silently + лог) и не должна
    /// блокировать вызывающий поток.
    /// </summary>
    Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default);
}
