using System.Collections.Concurrent;

namespace Tasks.Api.Messaging;

/// <summary>
/// In-memory реализация <see cref="ITaskEventPublisher"/> для тестов и отладки:
/// не ходит в брокер, а просто складывает опубликованные события в список.
///
/// Регистрируется, когда в конфигурации включён флаг <c>RabbitMq:UseRecordingPublisher=true</c>
/// (например, в окружениях без доступного RabbitMQ).
/// </summary>
public sealed class RecordingTaskEventPublisher : ITaskEventPublisher, IBrokerHealthProbe
{
    private readonly ConcurrentQueue<TaskCompletedEvent> _events = new();
    private readonly ILogger<RecordingTaskEventPublisher> _logger;

    public RecordingTaskEventPublisher(ILogger<RecordingTaskEventPublisher> logger)
    {
        _logger = logger;
    }

    /// <summary>Все опубликованные события (для проверок в тестах).</summary>
    public IReadOnlyCollection<TaskCompletedEvent> Events => _events.ToArray();

    public Task PublishTaskCompletedAsync(TaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(@event);
        _logger.LogInformation(
            "[RecordingPublisher] Событие task.completed записано в память: TaskId={TaskId}, Title={Title}, Priority={Priority}.",
            @event.TaskId, @event.Title, @event.Priority);
        return Task.CompletedTask;
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
