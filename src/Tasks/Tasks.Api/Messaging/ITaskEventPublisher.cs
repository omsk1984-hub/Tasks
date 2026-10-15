namespace Tasks.Api.Messaging;

/// <summary>
/// Публикация доменных событий задач в брокер.
/// Абстракция нужна, чтобы интеграционный тест мог подменить реализацию.
/// </summary>
public interface ITaskEventPublisher
{
    /// <summary>
    /// Публикует событие завершения задачи. Реализация НЕ должна выбрасывать исключение
    /// при недоступности брокера (fail silently + лог) и не должна блокировать вызывающий поток.
    /// </summary>
    Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Проверка доступности брокера (для health-check).</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
