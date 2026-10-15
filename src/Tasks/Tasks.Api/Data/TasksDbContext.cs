using Microsoft.EntityFrameworkCore;
using Tasks.Api.Data.Outbox;
using Tasks.Api.Domain;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core контекст для задач. Маппинг сущностей вынесен в отдельные
/// конфигурации (<see cref="Configurations.TaskItemConfiguration"/>,
/// <see cref="Configurations.OutboxMessageConfiguration"/>) и подключается
/// автоматически через ApplyConfigurationsFromAssembly.
/// </summary>
public class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>Таблица outbox (transactional outbox): читается CDC-воркером.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksDbContext).Assembly);
    }
}
