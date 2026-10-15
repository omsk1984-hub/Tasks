namespace Tasks.Api.Application.Tasks.CompleteTask;

/// <summary>Команда завершения задачи по идентификатору.</summary>
public sealed record CompleteTaskCommand(Guid Id);
