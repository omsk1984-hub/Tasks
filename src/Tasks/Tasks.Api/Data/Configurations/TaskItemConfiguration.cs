using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tasks.Api.Domain;

namespace Tasks.Api.Data.Configurations;

/// <summary>
/// Маппинг сущности <see cref="TaskItem"/> на таблицу tasks.
/// Вынесен из DbContext по принципу единственной ответственности:
/// контекст отвечает за доступ к данным, конфигурация — за схему.
/// </summary>
public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> task)
    {
        task.ToTable("tasks");
        task.HasKey(t => t.Id);

        task.Property(t => t.Id)
            .HasColumnName("id")
            // Id генерирует приложение (не БД), поэтому ValueGeneratedNever.
            .ValueGeneratedNever();

        // Value Object TaskTitle маппится на строковую колонку title через backing-поле.
        // В БД хранится нормализованное значение (TaskTitle.Value).
        task.Property(t => t.Title)
            .HasColumnName("title")
            .HasConversion(new ValueConverter<TaskTitle, string>(
                title => title.Value,
                value => TaskTitle.FromPersisted(value)))
            .IsRequired()
            .HasMaxLength(TaskTitle.MaxLength);

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
        // UPDATE ... WHERE xmin = @original — без явных pessimistic locks.
        // HACK: EF не генерирует обычные миграции для xmin-колонки (она системная),
        // поэтому в миграции ничего не создаётся, а сравнение идёт по WHERE xmin = ...
        task.Property(t => t.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        task.HasIndex(t => t.CreatedAt).HasDatabaseName("ix_tasks_created_at");

        // Доменные события — не часть схемы БД.
        task.Ignore(t => t.DomainEvents);
    }
}
