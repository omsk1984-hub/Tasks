using Tasks.Api.Domain.Events;
using Tasks.Api.Domain.Exceptions;

namespace Tasks.Api.Domain;

/// <summary>
/// Сущность задачи. Богатая модель: состояние меняется только через методы,
/// что гарантирует инварианты:
///  - <see cref="CompletedAt"/> заполнен тогда и только тогда, когда <see cref="IsCompleted"/> = true;
///  - заголовок всегда валиден (представлен <see cref="TaskTitle"/>).
/// </summary>
public class TaskItem
{
    private readonly List<IDomainEvent> _domainEvents = [];

    // Приватный конструктор без параметров — только для материализации EF Core.
    private TaskItem()
    {
        Title = null!; // заполняется EF через backing field либо фабрикой
    }

    private TaskItem(Guid id, TaskTitle title, Priority priority, DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Priority = priority;
        CreatedAt = createdAt;
        IsCompleted = false;
        CompletedAt = null;
    }

    /// <summary>Идентификатор задачи. Генерируется приложением при создании.</summary>
    public Guid Id { get; private set; }

    /// <summary>Заголовок задачи (Value Object). Маппится на колонку title.</summary>
    public TaskTitle Title { get; private set; }

    /// <summary>Признак завершения. По умолчанию false.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Когда задача создана (заполняется один раз при создании).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Когда задача завершена. Заполняется только в момент завершения, иначе null.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Приоритет. По умолчанию Medium.</summary>
    public Priority Priority { get; private set; }

    /// <summary>
    /// Токен конкурентности. В PostgreSQL маппится на системную колонку xmin
    /// (см. TaskItemConfiguration). Позволяет вернуть 409 при гонке на complete.
    /// </summary>
    public uint RowVersion { get; private set; }

    /// <summary>Накопленные доменные события (до сброса внешним слоем).</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // --- Фабрика ---

    /// <summary>
    /// Создаёт новую задачу. Заголовок уже валидирован как <see cref="TaskTitle"/>.
    /// CompletedAt по умолчанию null.
    /// </summary>
    public static TaskItem Create(TaskTitle title, Priority priority, DateTimeOffset now, Guid? id = null)
        => new(id ?? Guid.NewGuid(), title, priority, now);

    // --- Поведение ---

    /// <summary>
    /// Завершает задачу: проставляет IsCompleted=true и CompletedAt=now, поднимает
    /// доменное событие <see cref="TaskCompletedDomainEvent"/>.
    /// Бросает <see cref="DomainException"/>, если задача уже завершена.
    /// </summary>
    public void Complete(DateTimeOffset now)
    {
        if (IsCompleted)
        {
            throw DomainException.TaskAlreadyCompleted();
        }

        IsCompleted = true;
        CompletedAt = now;

        _domainEvents.Add(new TaskCompletedDomainEvent(Id, Title.Value, now, Priority));
    }

    /// <summary>Сбрасывает накопленные доменные события после обработки.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
