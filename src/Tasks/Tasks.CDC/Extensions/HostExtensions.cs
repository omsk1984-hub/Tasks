using Tasks.CDC.Replication;

namespace Tasks.CDC.Extensions;

/// <summary>
/// Прикладные extension-методы хоста CDC-воркера.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// Идемпотентно создаёт публикацию для outbox-таблицы при старте.
    /// Вызывается до запуска <see cref="CdcWorker"/>, чтобы публикация точно была
    /// готова к моменту старта потока репликации.
    /// </summary>
    public static async Task EnsureReplicationSetupAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<PublicationInitializer>();
        var store = scope.ServiceProvider.GetRequiredService<ReplicationPositionStore>();

        await store.EnsureSchemaAsync(CancellationToken.None);
        await initializer.EnsurePublicationAsync(CancellationToken.None);
    }
}
