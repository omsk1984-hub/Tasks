using Tasks.Api.Domain;

namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Абстракция доступа к задачам. Отделяет Application от EF Core (DIP):
/// обработчики use-case не знают про DbContext.
/// </summary>
public interface ITaskRepository
{
    /// <summary>Возвращает задачу по идентификатору или null.</summary>
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Возвращает все задачи, отсортированные по CreatedAt (убывание).</summary>
    Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает задачи с пагинацией, отсортированные по CreatedAt (убывание).
    /// </summary>
    /// <param name="page">Номер страницы (начиная с 1).</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <param name="cancellationToken">Tokent для отмены.</param>
    /// <returns>Общее количество записей и элементы страницы.</returns>
    Task<(int TotalCount, IReadOnlyList<TaskItem> Items)> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Добавляет новую задачу в трекинг.</summary>
    void Add(TaskItem item);

    /// <summary>Помечает задачу на удаление.</summary>
    void Remove(TaskItem item);
}
