using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tasks.Api.Data.Outbox;

namespace Tasks.Api.Data.Configurations;

/// <summary>
/// Маппинг сущности <see cref="OutboxMessage"/> на таблицу outbox_messages.
/// Таблица — журнал исходящих событий (transactional outbox), читаемый CDC-воркером
/// через logical replication (pgoutput). Строки хранятся для реализации/аудита.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> outbox)
    {
        outbox.ToTable("outbox_messages");
        outbox.HasKey(m => m.Id);

        outbox.Property(m => m.Id)
            .HasColumnName("id")
            // Id задаёт приложение (= TaskId), не БД.
            .ValueGeneratedNever();

        outbox.Property(m => m.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();

        // payload — готовый JSON контракта; храним как jsonb (аудит/диагностика).
        outbox.Property(m => m.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        outbox.Property(m => m.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        outbox.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // Индекс по created_at — для запросов аудита/ретенции.
        outbox.HasIndex(m => m.CreatedAt).HasDatabaseName("ix_outbox_messages_created_at");
    }
}
