using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain.Exceptions;
using Tasks.Api.Domain.Events;
using Tasks.Api.Messaging;

namespace Tasks.Api.Application.Tasks.CompleteTask;

/// <summary>
/// Use-case «завершить задачу».
/// Порядок: загрузить → применить доменное поведение → в ОДНОЙ транзакции
/// сохранить изменение и записать integration-событие в outbox.
/// Публикацию в RabbitMQ выполняет CDC-воркер (Tasks.CDC), читающий WAL.
/// Конкурентность (xmin) обрабатывается здесь и отображается в 409.
/// </summary>
public sealed class CompleteTaskHandler(
    ITaskRepository repository,
    IUnitOfWork unitOfWork,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CompleteTaskHandler> logger)
{
    public async Task<Result<TaskDto>> HandleAsync(CompleteTaskCommand command, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (item is null)
        {
            return Result<TaskDto>.Failure(Error.NotFound($"Задача {command.Id} не найдена."));
        }

        // Доменные события формируются в памяти до транзакции: валидацию состояния
        // (уже завершена?) проверяем до обращения к БД.
        try
        {
            item.Complete(clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<TaskDto>.Failure(Error.Conflict(ex.Message));
        }

        // Записываем доменные события в outbox в той же транзакции, что и SaveChanges.
        foreach (var domainEvent in item.DomainEvents)
        {
            if (domainEvent is TaskCompletedDomainEvent completed)
            {
                outbox.Add(completed.ToIntegrationEvent());
            }
        }

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                () => unitOfWork.SaveChangesAsync(cancellationToken),
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Кто-то другой завершил задачу между нашим SELECT и UPDATE.
            // Транзакция откатилась — запись в outbox не сохранена.
            logger.LogInformation("Конкурентное завершение задачи {TaskId}: возвращаем 409.", command.Id);
            return Result<TaskDto>.Failure(Error.Conflict("Задача была изменена другим запросом."));
        }

        item.ClearDomainEvents();

        return Result<TaskDto>.Success(item.ToDto());
    }
}
