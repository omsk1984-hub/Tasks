namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Результат запроса с пагинацией.
/// Содержит элементы страницы и метаданные для навигации.
/// </summary>
public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
