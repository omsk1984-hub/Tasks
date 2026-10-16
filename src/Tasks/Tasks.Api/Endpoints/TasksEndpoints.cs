using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tasks.Api.Application;
using Tasks.Api.Application.Tasks.CompleteTask;
using Tasks.Api.Application.Tasks.CreateTask;
using Tasks.Api.Application.Tasks.DeleteTask;
using Tasks.Api.Application.Tasks.GetTasks;
using Tasks.Api.Contracts;

namespace Tasks.Api.Endpoints;

/// <summary>
/// Роуты API задач (Minimal API). Тонкий слой Presentation: маппит HTTP-запрос
/// в команду/запрос, вызывает обработчик и преобразует Result в HTTP-ответ.
/// Бизнес-логики и доступа к данным здесь нет.
/// </summary>
public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tasks").WithTags("Tasks");

        group.MapPost("/", CreateTaskAsync).WithName("CreateTask");
        group.MapGet("/", GetAllTasksAsync).WithName("GetAllTasks");
        group.MapPut("/{id:guid}/complete", CompleteTaskAsync).WithName("CompleteTask");
        group.MapDelete("/{id:guid}", DeleteTaskAsync).WithName("DeleteTask");

        return app;
    }

    // POST /tasks — создать задачу.
    private static async Task<Results<Created<TaskResponse>, ValidationProblem>> CreateTaskAsync(
        CreateTaskRequest request,
        CreateTaskHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new CreateTaskCommand(request.Title, request.Priority), ct);

        return result.IsSuccess
            ? TypedResults.Created($"/tasks/{result.Value!.Id}", TaskResponse.From(result.Value))
            : ToValidationProblem(result.Error!);
    }

    // GET /tasks?{page}&{pageSize} — задачи (с пагинацией при указании параметров).
    private static async Task<IResult> GetAllTasksAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        GetTasksHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetTasksQuery(page, pageSize), ct);

        if (!result.IsSuccess)
        {
            return TypedResults.BadRequest();
        }

        var paginated = result.Value!;
        var responses = paginated.Items.Select(TaskResponse.From).ToList();

        if (page.HasValue && pageSize.HasValue)
        {
            // Возвращаем пагинированный ответ с метаданными
            var paginatedResponse = new PaginatedTaskResponse(
                responses,
                paginated.TotalCount,
                paginated.Page,
                paginated.PageSize,
                paginated.TotalPages);

            return TypedResults.Ok(paginatedResponse);
        }

        // Без параметров — как раньше, просто список
        return TypedResults.Ok<IReadOnlyList<TaskResponse>>(responses);
    }

    // PUT /tasks/{id}/complete — завершить задачу.
    private static async Task<Results<Ok<TaskResponse>, NotFound, Conflict<string>, ValidationProblem>> CompleteTaskAsync(
        Guid id,
        CompleteTaskHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new CompleteTaskCommand(id), ct);

        if (result.IsSuccess)
        {
            return TypedResults.Ok(TaskResponse.From(result.Value!));
        }

        return result.Error!.Type switch
        {
            ErrorType.NotFound => TypedResults.NotFound(),
            ErrorType.Validation => ToValidationProblem(result.Error),
            _ => TypedResults.Conflict(result.Error.Message)
        };
    }

    // DELETE /tasks/{id} — жёсткое удаление.
    private static async Task<Results<NoContent, NotFound>> DeleteTaskAsync(
        Guid id,
        DeleteTaskHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new DeleteTaskCommand(id), ct);
        return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static ValidationProblem ToValidationProblem(Error error)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = [error.Message]
        });
}
