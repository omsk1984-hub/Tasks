namespace Tasks.CDC.Replication;

/// <summary>
/// Настройки CDC-читателя (logical replication, pgoutput).
/// Секция конфигурации: <c>Cdc</c>.
/// </summary>
public sealed class ReplicationOptions
{
    public const string SectionName = "Cdc";

    /// <summary>Имя логического репликационного слота.</summary>
    public string SlotName { get; set; } = "tasks_outbox_slot";

    /// <summary>Имя публикации (на таблицу outbox_messages).</summary>
    public string PublicationName { get; set; } = "tasks_outbox_pub";

    /// <summary>Таблица outbox, изменения которой нас интересуют.</summary>
    public string OutboxTable { get; set; } = "outbox_messages";

    /// <summary>Пауза перед переподключением к слоту при обрыве (секунды).</summary>
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>Строка подключения к PostgreSQL (берётся из ConnectionStrings:Postgres).</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
