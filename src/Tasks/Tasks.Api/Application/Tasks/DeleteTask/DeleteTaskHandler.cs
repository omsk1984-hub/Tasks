using Tasks.Api.Application.Abstractions;

namespace Tasks.Api.Application.Tasks.DeleteTask;

/// <summary>Use-case «удалить задачу» (жёсткое удаление).</summary>
public sealed class DeleteTaskHandler(ITaskRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(DeleteTaskCommand command, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound($"Задача {command.Id} не найдена."));
        }

        repository.Remove(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
