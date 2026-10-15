namespace Tasks.Api.Domain;

/// <summary>
/// Сущность задачи (примерно из ТЗ).
/// </summary>
public class TaskItem
{
    /// <summary>Идентификатор задачи. Генерируется приложением при создании.</summary>
    public Guid Id { get; set; }

    /// <summary>Заголовок. Не null, не пустой, максимум 200 символов (валидируется в API).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Признак завершения. По умолчанию false.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Когда задача создана (заполняется один раз при создании).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Когда задача завершена. Заполняется ТОЛЬКО в момент завершения, иначе null.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Приоритет. По умолчанию Medium.</summary>
    public Priority Priority { get; set; }

    /// <summary>
    /// Токен конкурентности. В PostgreSQL маппится на системную колонку <c>xmin</c>
    /// (см. TasksDbContext.OnModelCreating). Позволяет вернуть 409 при гонке на complete.
    /// </summary>
    public uint RowVersion { get; set; }
}
