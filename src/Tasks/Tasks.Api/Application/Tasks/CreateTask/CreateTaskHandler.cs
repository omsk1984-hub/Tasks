using Tasks.Api.Application.Abstractions;
using Tasks.Api.Domain;
using Tasks.Api.Domain.Exceptions;

namespace Tasks.Api.Application.Tasks.CreateTask;

/// <summary>
/// Use-case «создать задачу». Единственная ответственность: валидация заголовка,
/// конструирование агрегата и сохранение.
/// </summary>
public sealed class CreateTaskHandler(
    ITaskRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<TaskDto>> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken)
    {
        TaskTitle title;
        try
        {
            title = TaskTitle.Create(command.Title);
        }
        catch (DomainException ex)
        {
            return Result<TaskDto>.Failure(Error.Validation(ex.Message));
        }

        var item = TaskItem.Create(
            title,
            command.Priority ?? Priority.Medium,
            clock.GetUtcNow());

        repository.Add(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TaskDto>.Success(item.ToDto());
    }
}
