using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tasks.Consumer.Data.Configurations;

/// <summary>
/// Маппинг сущности <see cref="TaskFromRmq"/> на таблицу tasks_from_rmq.
/// Вынесен из DbContext по принципу единственной ответственности.
/// </summary>
public sealed class TaskFromRmqConfiguration : IEntityTypeConfiguration<TaskFromRmq>
{
    public void Configure(EntityTypeBuilder<TaskFromRmq> task)
    {
        task.ToTable("tasks_from_rmq");

        // task_id — естественный ключ идемпотентности (PK).
        task.HasKey(t => t.TaskId);
        task.Property(t => t.TaskId)
            .HasColumnName("task_id")
            .ValueGeneratedNever();

        task.Property(t => t.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(200);

        task.Property(t => t.Priority)
            .HasColumnName("priority")
            .IsRequired()
            .HasMaxLength(16);

        task.Property(t => t.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired();

        task.Property(t => t.ProcessedAt)
            .HasColumnName("processed_at")
            .IsRequired();

        task.Property(t => t.RoutingKey)
            .HasColumnName("routing_key")
            .HasMaxLength(128);

        task.Property(t => t.RawPayload)
            .HasColumnName("raw_payload")
            .HasColumnType("jsonb");

        // Индекс по processed_at — для диагностики/аудита порядка обработки.
        task.HasIndex(t => t.ProcessedAt).HasDatabaseName("ix_tasks_from_rmq_processed_at");
    }
}
