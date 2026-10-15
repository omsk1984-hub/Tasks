namespace Tasks.Api.Application.Tasks.DeleteTask;

/// <summary>Команда удаления задачи по идентификатору.</summary>
public sealed record DeleteTaskCommand(Guid Id);
