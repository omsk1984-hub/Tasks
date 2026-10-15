using Microsoft.EntityFrameworkCore;

namespace Tasks.Consumer.Data;

/// <summary>
/// EF Core контекст consumer-а. Маппинг сущностей вынесен в отдельные конфигурации
/// (<see cref="Configurations.TaskFromRmqConfiguration"/>) и подключается автоматически
/// через ApplyConfigurationsFromAssembly. Использует ту же БД, что и Tasks.Api.
/// </summary>
public sealed class ConsumerDbContext(DbContextOptions<ConsumerDbContext> options) : DbContext(options)
{
    public DbSet<TaskFromRmq> TasksFromRmq => Set<TaskFromRmq>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsumerDbContext).Assembly);
    }
}
