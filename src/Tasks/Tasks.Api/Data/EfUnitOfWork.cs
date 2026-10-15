using Microsoft.EntityFrameworkCore;
using Tasks.Api.Application.Abstractions;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core реализация <see cref="IUnitOfWork"/>. Транслирует
/// <see cref="DbUpdateConcurrencyException"/> в доменно-нейтральный
/// <see cref="ConcurrencyConflictException"/> и управляет транзакционной
/// границей для transactional outbox.
/// </summary>
public sealed class EfUnitOfWork(TasksDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("Конфликт конкурентности при сохранении изменений.", ex);
        }
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        // Транзакция нужна, чтобы изменение агрегата и запись события в outbox
        // фиксировались атомарно (transactional outbox).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await action();
        await transaction.CommitAsync(cancellationToken);
    }
}
