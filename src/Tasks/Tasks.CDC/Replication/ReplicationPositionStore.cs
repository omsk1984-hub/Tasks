using Npgsql;
using NpgsqlTypes;

namespace Tasks.CDC.Replication;

/// <summary>
/// Хранилище позиции репликации и дедупликации обработанных WAL-изменений.
///
/// Для дедупликации и устойчивости создаём таблицу cdc_processed с уникальным
/// ключом (lsn, outbox_id): повторная обработка одного и того же изменения
/// (например, при реконнекте/переигрывании WAL) не приводит к повторной публикации.
///
/// Таблица ведётся в той же БД, что и outbox. Методы идемпотентны.
/// </summary>
public sealed class ReplicationPositionStore(NpgsqlDataSource dataSource)
{
    /// <summary>Создаёт служебную таблицу дедупликации, если её нет.</summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS cdc_processed (
                lsn            pg_lsn NOT NULL,
                outbox_id      uuid NOT NULL,
                processed_at   timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT pk_cdc_processed PRIMARY KEY (lsn, outbox_id)
            );
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Пытается «зарезервировать» обработку изменения. Возвращает true, если пара
    /// (lsn, outboxId) ещё не обрабатывалась и запись вставлена; false — дубликат.
    /// ON CONFLICT DO NOTHING делает операцию атомарной при нескольких воркерах.
    /// </summary>
    public async Task<bool> TryMarkProcessedAsync(
        ulong lsn, Guid outboxId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO cdc_processed (lsn, outbox_id)
            VALUES (@lsn, @outbox_id)
            ON CONFLICT (lsn, outbox_id) DO NOTHING;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        // Явно указываем NpgsqlDbType.PgLsn: Npgsql не выводит тип из NpgsqlLogSequenceNumber
        // без подсказки (иначе InvalidCastException при записи параметра).
        cmd.Parameters.AddWithValue("lsn", NpgsqlDbType.PgLsn, new NpgsqlLogSequenceNumber(lsn));
        cmd.Parameters.AddWithValue("outbox_id", outboxId);
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }
}
