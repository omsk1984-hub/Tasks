namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Единица работы: фиксация изменений, накопленных репозиториями.
/// Позволяет Application управлять транзакционной границей, не зная об EF Core.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Сохраняет изменения. Возвращает число затронутых записей.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
