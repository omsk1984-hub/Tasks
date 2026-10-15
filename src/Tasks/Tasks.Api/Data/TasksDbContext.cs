using Microsoft.EntityFrameworkCore;
using Tasks.Api.Domain;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core контекст для задач.
/// </summary>
public class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();

        task.ToTable("tasks");
        task.HasKey(t => t.Id);

        task.Property(t => t.Id)
            .HasColumnName("id")
            // Id генерирует приложение (не БД), поэтому ValueGeneratedNever.
            .ValueGeneratedNever();

        task.Property(t => t.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(200);

        task.Property(t => t.IsCompleted)
            .HasColumnName("is_completed")
            .HasDefaultValue(false);

        task.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        task.Property(t => t.CompletedAt)
            .HasColumnName("completed_at");

        // Храним enum как строку (Low/Medium/High) — читаемее в БД и устойчивее к смене порядка.
        task.Property(t => t.Priority)
            .HasColumnName("priority")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        // Конкурентность через системную колонку PostgreSQL xmin.
        // Это человеческий способ без явных pessimistic locks: UPDATE ... WHERE xmin = @original.
        // HACK: EF не генерирует обычные миграции для xmin-колонки — она системная,
        // поэтому в миграции для неё ничего не создаётся, а сравнение идёт по WHERE xmin = ...
        task.Property(t => t.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        task.HasIndex(t => t.CreatedAt).HasDatabaseName("ix_tasks_created_at");
    }
}
