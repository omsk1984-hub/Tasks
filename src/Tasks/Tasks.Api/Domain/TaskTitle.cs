using Tasks.Api.Domain.Exceptions;

namespace Tasks.Api.Domain;

/// <summary>
/// Value Object заголовка задачи. Инкапсулирует правила валидности:
///  - обязателен, не пустой, не из пробелов;
///  - после Trim() длина от 1 до 200 символов.
///
/// Единственный источник правды для валидации заголовка: устраняет дублирование
/// между контрактом запроса и бизнес-логикой.
/// </summary>
public sealed record TaskTitle
{
    public const int MaxLength = 200;

    private TaskTitle(string value) => Value = value;

    /// <summary>Нормализованное (trim) значение заголовка.</summary>
    public string Value { get; }

    /// <summary>
    /// Создаёт значение или бросает <see cref="DomainException"/> с кодом "invalid_title".
    /// </summary>
    public static TaskTitle Create(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw DomainException.InvalidTitle("Title обязателен и не может быть пустым.");
        }

        var trimmed = title.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw DomainException.InvalidTitle($"Title не должен превышать {MaxLength} символов.");
        }

        return new TaskTitle(trimmed);
    }

    /// <summary>Безопасный вариант создания без исключения.</summary>
    public static bool TryCreate(string? title, out TaskTitle? result)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            result = null;
            return false;
        }

        var trimmed = title.Trim();
        if (trimmed.Length > MaxLength)
        {
            result = null;
            return false;
        }

        result = new TaskTitle(trimmed);
        return true;
    }

    /// <summary>Для материализации EF Core и сериализации — только внутреннее использование.</summary>
    internal static TaskTitle FromPersisted(string value) => new(value);

    public override string ToString() => Value;
}
