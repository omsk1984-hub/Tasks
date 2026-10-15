using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain;

namespace Tasks.Tests.Application;

/// <summary>In-memory реализация ITaskRepository для юнит-тестов обработчиков.</summary>
internal sealed class FakeTaskRepository : ITaskRepository
{
    private readonly List<TaskItem> _items = [];

    public int SaveCount { get; private set; }

    public void Seed(TaskItem item) => _items.Add(item);

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TaskItem>>(_items.OrderByDescending(i => i.CreatedAt).ToList());

    public void Add(TaskItem item) => _items.Add(item);

    public void Remove(TaskItem item) => _items.Remove(item);
}

/// <summary>Fake IUnitOfWork, фиксирующий факт вызова сохранения.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    /// <summary>Если задано, SaveChangesAsync выбросит это исключение (для проверки 409).</summary>
    public Exception? ThrowOnSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }
        return Task.FromResult(1);
    }
}
