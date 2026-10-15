namespace Tasks.Api.Messaging;

/// <summary>
/// Проверка доступности брокера для health-check'ов (ISP: отделён от публикации,
/// чтобы потребители health-check не зависели от ITaskEventPublisher).
/// </summary>
public interface IBrokerHealthProbe
{
    /// <summary>Возвращает true, если брокер доступен.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
