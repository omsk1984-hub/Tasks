namespace Tasks.Api.Contracts;

/// <summary>
/// Пагинированный ответ API. Содержит элементы страницы и метаданные для навигации.
/// </summary>
public sealed record PaginatedTaskResponse(
    IReadOnlyList<TaskResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
