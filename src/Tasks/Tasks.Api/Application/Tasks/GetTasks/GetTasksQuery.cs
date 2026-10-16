namespace Tasks.Api.Application.Tasks.GetTasks;

/// <summary>
/// Запрос списка задач.
/// Без параметров — возвращает все задачи (как раньше).
/// С параметрами — возвращает постраничный результат.
/// </summary>
public sealed record GetTasksQuery(
    int? Page = null,
    int? PageSize = null);
