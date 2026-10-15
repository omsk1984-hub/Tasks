using Microsoft.EntityFrameworkCore;
using Tasks.Api.Data;

namespace Tasks.Api.Extensions;

/// <summary>
/// Прикладные extension-методы для <see cref="WebApplication"/>.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Применяет миграции при старте, если включён флаг <c>Db:MigrateOnStartup</c>.
    /// В проде миграции обычно выносят в отдельный шаг deploy (см. ТЗ);
    /// поэтому поведение управляется конфигурацией, а не зашито жёстко.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        var migrateOnStartup = app.Configuration.GetValue("Db:MigrateOnStartup", defaultValue: true);
        if (!migrateOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await db.Database.MigrateAsync();
    }
}
