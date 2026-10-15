using Microsoft.EntityFrameworkCore;
using Tasks.Consumer.Data;

namespace Tasks.Consumer;

/// <summary>
/// Extension-методы хоста consumer-а.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// Применяет миграции при старте, если включён флаг <c>Db:MigrateOnStartup</c>.
    /// Consumer — владелец таблицы tasks_from_rmq, поэтому именно он создаёт схему.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        var migrateOnStartup = host.Services
            .GetRequiredService<IConfiguration>()
            .GetValue("Db:MigrateOnStartup", defaultValue: true);

        if (!migrateOnStartup)
        {
            return;
        }

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsumerDbContext>();
        await db.Database.MigrateAsync();
    }
}
