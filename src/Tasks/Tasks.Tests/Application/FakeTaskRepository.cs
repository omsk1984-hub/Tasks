using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain;
using Tasks.Core.Messaging;

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

    public Task<(int TotalCount, IReadOnlyList<TaskItem> Items)> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var sorted = _items.OrderByDescending(i => i.CreatedAt).ToList();
        var totalCount = sorted.Count;
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(int, IReadOnlyList<TaskItem>)>((totalCount, items));
    }
}

/// <summary>Fake IUnitOfWork, фиксирующий факт вызова сохранения и транзакции.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    /// <summary>Сколько раз выполнялась транзакция (ExecuteInTransactionAsync).</summary>
    public int TransactionCount { get; private set; }

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

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        TransactionCount++;
        // Эмуляция транзакции: при исключении в action изменения считаются откаченными,
        // но FakeOutboxWriter хранит события в памяти — тест проверяет только сценарии без сбоя.
        await action();
    }
}

/// <summary>
/// Fake IOutboxWriter: складывает события в список, чтобы тесты могли проверить,
/// что complete пишет в outbox (а не публикует в брокер напрямую).
/// </summary>
internal sealed class FakeOutboxWriter : IOutboxWriter
{
    private readonly List<TaskCompletedEvent> _events = [];

    public IReadOnlyList<TaskCompletedEvent> Events => _events;

    public void Add(TaskCompletedEvent @event) => _events.Add(@event);
}
