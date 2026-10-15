namespace Tasks.Api.Domain.Exceptions;

/// <summary>
/// Нарушение бизнес-инварианта домена (например, невалидный заголовок или
/// попытка завершить уже завершённую задачу).
///
/// Несёт машинно-читаемый <see cref="Code"/>, чтобы слой Application/Presentation
/// мог отобразить его в HTTP-ответ без анализа текста сообщения.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Машинно-читаемый код нарушения (например, "task_already_completed").</summary>
    public string Code { get; }

    // --- Фабрики типовых нарушений ---

    public static DomainException InvalidTitle(string message) => new("invalid_title", message);

    public static DomainException TaskAlreadyCompleted() =>
        new("task_already_completed", "Задача уже завершена.");
}
