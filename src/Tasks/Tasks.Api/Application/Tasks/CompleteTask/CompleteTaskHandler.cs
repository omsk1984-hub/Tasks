using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain.Exceptions;
using Tasks.Api.Domain.Events;
using Tasks.Api.Messaging;

namespace Tasks.Api.Application.Tasks.CompleteTask;

/// <summary>
/// Use-case «завершить задачу».
/// Порядок: загрузить → применить доменное поведение → сохранить → опубликовать
/// integration-событие (после успешного сохранения, fail-silently внутри publisher).
/// Конкурентность (xmin) обрабатывается здесь и отображается в 409.
/// </summary>
public sealed class CompleteTaskHandler(
    ITaskRepository repository,
    IUnitOfWork unitOfWork,
    ITaskEventPublisher publisher,
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

        try
        {
            item.Complete(clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<TaskDto>.Failure(Error.Conflict(ex.Message));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Кто-то другой завершил задачу между нашим SELECT и UPDATE.
            logger.LogInformation("Конкурентное завершение задачи {TaskId}: возвращаем 409.", command.Id);
            return Result<TaskDto>.Failure(Error.Conflict("Задача была изменена другим запросом."));
        }

        // Публикуем доменные события как integration-сообщения ПОСЛЕ сохранения.
        foreach (var domainEvent in item.DomainEvents)
        {
            if (domainEvent is TaskCompletedDomainEvent completed)
            {
                await publisher.PublishTaskCompletedAsync(completed.ToIntegrationEvent(), cancellationToken);
            }
        }
        item.ClearDomainEvents();

        return Result<TaskDto>.Success(item.ToDto());
    }
}
