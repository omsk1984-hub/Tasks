using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tasks.Api.Contracts;
using Tasks.Api.Data;
using Tasks.Api.Domain;
using Tasks.Api.Messaging;

namespace Tasks.Api.Endpoints;

/// <summary>
/// Роуты API задач (Minimal API). Сгруппированы в одном месте, чтобы нужное
/// находилось без Ctrl+F: create / list / complete / delete.
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

    // ---------------------------------------------------------------------
    // POST /tasks — создать задачу.
    // По умолчанию IsCompleted=false, Priority=Medium, CompletedAt=null.
    // ---------------------------------------------------------------------
    private static async Task<Results<Created<TaskResponse>, ValidationProblem>> CreateTaskAsync(
        CreateTaskRequest request,
        TasksDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        // Title: не пустой, не пробельный, максимум 200 (DataAnnotations + явная проверка на whitespace).
        var errors = ValidateTitle(request.Title);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title!.Trim(),
            IsCompleted = false,
            CreatedAt = clock.GetUtcNow(),
            CompletedAt = null,          // ВАЖНО: не заполняем при создании.
            Priority = request.Priority ?? Priority.Medium
        };

        db.Tasks.Add(item);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/tasks/{item.Id}", TaskResponse.From(item));
    }

    // ---------------------------------------------------------------------
    // GET /tasks — все задачи (без пагинации, как в ТЗ).
    // ---------------------------------------------------------------------
    private static async Task<Ok<IReadOnlyList<TaskResponse>>> GetAllTasksAsync(
        TasksDbContext db,
        CancellationToken ct)
    {
        var items = await db.Tasks
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var response = items.Select(TaskResponse.From).ToList();
        return TypedResults.Ok<IReadOnlyList<TaskResponse>>(response);
    }

    // ---------------------------------------------------------------------
    // PUT /tasks/{id}/complete — завершить задачу.
    // Порядок: сохраняем в БД, ПОТОМ публикуем в RabbitMQ.
    // Конкурентность: xmin (RowVersion) -> второй параллельный вызов получает 409.
    // ---------------------------------------------------------------------
    private static async Task<Results<Ok<TaskResponse>, NotFound, Conflict<string>, ValidationProblem>> CompleteTaskAsync(
        Guid id,
        TasksDbContext db,
        ITaskEventPublisher publisher,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Tasks.Api.Endpoints.CompleteTask");

        var item = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        // Быстрая проверка для "уже завершено" (дружелюбный 409 без попытки UPDATE).
        if (item.IsCompleted)
        {
            return TypedResults.Conflict("Задача уже завершена.");
        }

        item.IsCompleted = true;
        item.CompletedAt = clock.GetUtcNow(); // заполняется ТОЛЬКО здесь

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Кто-то другой завершил задачу между нашим SELECT и UPDATE.
            logger.LogInformation("Конкурентное завершение задачи {TaskId}: возвращаем 409.", id);
            return TypedResults.Conflict("Задача была изменена другим запросом.");
        }

        // Публикуем ПОСЛЕ успешного сохранения. Ошибки брокера не влияют на ответ (fail silently + лог).
        var evt = new TaskCompletedEvent(item.Id, item.Title, item.CompletedAt!.Value, item.Priority);
        await publisher.PublishTaskCompletedAsync(evt, ct);

        return TypedResults.Ok(TaskResponse.From(item));
    }

    // ---------------------------------------------------------------------
    // DELETE /tasks/{id} — жёсткое удаление.
    // ---------------------------------------------------------------------
    private static async Task<Results<NoContent, NotFound>> DeleteTaskAsync(
        Guid id,
        TasksDbContext db,
        CancellationToken ct)
    {
        var item = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        db.Tasks.Remove(item);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Валидация Title: обязателен, не пустой, не из пробелов, максимум 200 символов.
    /// DataAnnotations не ловит строку из пробелов и строку длиннее лимита после trim,
    /// поэтому проверяем явно.
    /// </summary>
    private static Dictionary<string, string[]> ValidateTitle(string? title)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors["title"] = ["Title обязателен и не может быть пустым."];
            return errors;
        }

        var trimmed = title.Trim();
        if (trimmed.Length > 200)
        {
            errors["title"] = ["Title не должен превышать 200 символов."];
        }

        return errors;
    }
}
