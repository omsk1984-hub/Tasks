using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tasks.CDC;

/// <summary>
/// Периодически прогоняет зарегистрированные health-checks (RabbitMQ) и пишет
/// результат в лог. Переносит readiness-проверку брокера из API в CDC-воркер:
/// API больше не публикует в RabbitMQ напрямую, поэтому его доступность важна
/// именно здесь (CDC публикует события).
///
/// Дополнительно можно подключить экспорт метрик/HTTP-эндпоинт при развёртывании
/// в оркестраторе; для воркера достаточно периодического отчёта в лог.
/// </summary>
public sealed class HealthReporterService(
    HealthCheckService healthCheckService,
    ILogger<HealthReporterService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = await healthCheckService.CheckHealthAsync(stoppingToken);
                if (report.Status == HealthStatus.Healthy)
                {
                    logger.LogInformation("Readiness: {Status} (RabbitMQ доступен).", report.Status);
                }
                else
                {
                    logger.LogWarning(
                        "Readiness: {Status}. Неисправности: {Failures}.",
                        report.Status,
                        string.Join("; ", report.Entries
                            .Where(e => e.Value.Status != HealthStatus.Healthy)
                            .Select(e => $"{e.Key}={e.Value.Status}: {e.Value.Description}")));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка выполнения health-check.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
