namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Абстракция конфликта конкурентности: Application ловит это исключение, не зная
/// о EF Core (DIP). Инфраструктура транслирует сюда DbUpdateConcurrencyException.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
