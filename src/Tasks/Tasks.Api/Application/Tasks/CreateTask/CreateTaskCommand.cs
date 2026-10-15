using Tasks.Api.Domain;

namespace Tasks.Api.Application.Tasks.CreateTask;

/// <summary>Команда создания задачи. Title валидируется в обработчике через TaskTitle.</summary>
public sealed record CreateTaskCommand(string? Title, Priority? Priority);
