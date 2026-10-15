using Tasks.Api.Application.Abstractions;

namespace Tasks.Api.Application.Tasks.GetTasks;

/// <summary>Use-case «получить все задачи».</summary>
public sealed class GetTasksHandler(ITaskRepository repository)
{
    public async Task<Result<IReadOnlyList<TaskDto>>> HandleAsync(
        GetTasksQuery query,
        CancellationToken cancellationToken)
    {
        var items = await repository.GetAllAsync(cancellationToken);
        IReadOnlyList<TaskDto> dtos = items.Select(i => i.ToDto()).ToList();
        return Result<IReadOnlyList<TaskDto>>.Success(dtos);
    }
}
