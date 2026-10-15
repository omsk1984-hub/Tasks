using Tasks.Core.Messaging;

namespace Tasks.Api.Application.Abstractions;

/// <summary>
/// Запись integration-события в transactional outbox.
/// Application добавляет событие в outbox в рамках той же транзакции, что и
/// изменение агрегата (см. <see cref="IUnitOfWork.SaveChangesAsync"/>),
/// а публикацией в брокер занимается CDC-воркер (Tasks.CDC).
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Добавляет событие в outbox. Не сохраняет в БД — фиксация происходит
    /// вызовом <see cref="IUnitOfWork.SaveChangesAsync"/> внутри одной транзакции.
    /// </summary>
    void Add(TaskCompletedEvent @event);
}
