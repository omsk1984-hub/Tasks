namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Единица работы: фиксация изменений, накопленных репозиториями, и управление
/// транзакционной границей. Позволяет Application управлять транзакцией,
/// не зная об EF Core.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Сохраняет изменения. Возвращает число затронутых записей.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет <paramref name="action"/> в одной транзакции БД.
    /// Нужно, чтобы изменение агрегата и запись события в outbox фиксировались
    /// атомарно (transactional outbox). Транзакция коммитится при успехе
    /// и откатывается при исключении.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken);
}
