namespace Tasks.Api.Application;

/// <summary>
/// Категория ошибки use-case. Presentation маппит её в HTTP-код, не полагаясь
/// на текст сообщения (SRP: HTTP-детали не протекают в ядро).
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict
}

/// <summary>Описание ошибки use-case: машинный код + человекочитаемое сообщение.</summary>
public sealed record Error(ErrorType Type, string Code, string Message)
{
    public static Error Validation(string message) => new(ErrorType.Validation, "validation_error", message);

    public static Error NotFound(string message) => new(ErrorType.NotFound, "not_found", message);

    public static Error Conflict(string message) => new(ErrorType.Conflict, "conflict", message);
}

/// <summary>
/// Результат выполнения use-case без полезной нагрузки.
/// Заменяет протекание HttpResults в бизнес-логику.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);
}

/// <summary>Результат use-case со значением типа <typeparamref name="T"/>.</summary>
public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, Error? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);

    public static new Result<T> Failure(Error error) => new(false, default, error);
}
