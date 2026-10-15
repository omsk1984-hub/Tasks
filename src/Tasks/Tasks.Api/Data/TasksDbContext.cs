using Microsoft.EntityFrameworkCore;
using Tasks.Api.Domain;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core контекст для задач. Маппинг сущностей вынесен в отдельные
/// конфигурации (<see cref="Configurations.TaskItemConfiguration"/>) и подключается
/// автоматически через ApplyConfigurationsFromAssembly.
/// </summary>
public class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksDbContext).Assembly);
    }
}
