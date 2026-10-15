using Microsoft.EntityFrameworkCore;
using Tasks.Api.Application.Abstractions;

namespace Tasks.Api.Data;

/// <summary>
/// EF Core реализация <see cref="IUnitOfWork"/>. Транслирует
/// <see cref="DbUpdateConcurrencyException"/> в доменно-нейтральный
/// <see cref="ConcurrencyConflictException"/>.
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
}
