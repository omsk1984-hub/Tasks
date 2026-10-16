using Microsoft.EntityFrameworkCore;
using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core реализация <see cref="ITaskRepository"/>. Инкапсулирует DbContext,
/// не выпуская его в Application/Presentation.
/// </summary>
public sealed class TaskRepository(TasksDbContext db) : ITaskRepository
{
    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken)
        => await db.Tasks
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<(int TotalCount, IReadOnlyList<TaskItem> Items)> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Tasks.AsNoTracking().OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (totalCount, items);
    }

    public void Add(TaskItem item) => db.Tasks.Add(item);

    public void Remove(TaskItem item) => db.Tasks.Remove(item);
}
