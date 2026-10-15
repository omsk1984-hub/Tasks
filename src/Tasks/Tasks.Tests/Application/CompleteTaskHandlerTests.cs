using Microsoft.Extensions.Logging.Abstractions;
using Tasks.Api.Application;
using Tasks.Api.Application.Abstractions;
using Tasks.Api.Application.Tasks.CompleteTask;
using Tasks.Api.Domain;
using Tasks.Api.Messaging;

namespace Tasks.Tests.Application;

/// <summary>Юнит-тесты use-case завершения задачи.</summary>
public sealed class CompleteTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static (CompleteTaskHandler Handler, FakeTaskRepository Repo, FakeUnitOfWork Uow, RecordingTaskEventPublisher Publisher)
        Build()
    {
        var repo = new FakeTaskRepository();
        var uow = new FakeUnitOfWork();
        var publisher = new RecordingTaskEventPublisher(NullLogger<RecordingTaskEventPublisher>.Instance);
        var handler = new CompleteTaskHandler(
            repo,
            uow,
            publisher,
            new FixedTimeProvider(Now),
            NullLogger<CompleteTaskHandler>.Instance);
        return (handler, repo, uow, publisher);
    }

    [Fact]
    public async Task Complete_Existing_Task_Publishes_Event_After_Save()
    {
        var (handler, repo, uow, publisher) = Build();
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.High, Now);
        repo.Seed(item);

        var result = await handler.HandleAsync(new CompleteTaskCommand(item.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsCompleted);
        Assert.Equal(1, uow.SaveCount);

        var evt = Assert.Single(publisher.Events);
        Assert.Equal(item.Id, evt.TaskId);
        Assert.Equal("Buy milk", evt.Title);
        Assert.Equal(Priority.High, evt.Priority);
    }

    [Fact]
    public async Task Complete_Missing_Task_Returns_NotFound()
    {
        var (handler, _, uow, publisher) = Build();

        var result = await handler.HandleAsync(new CompleteTaskCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal(0, uow.SaveCount);
        Assert.Empty(publisher.Events);
    }

    [Fact]
    public async Task Complete_Already_Completed_Returns_Conflict()
    {
        var (handler, repo, uow, _) = Build();
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.Low, Now);
        item.Complete(Now);
        item.ClearDomainEvents();
        repo.Seed(item);

        var result = await handler.HandleAsync(new CompleteTaskCommand(item.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Complete_With_Concurrency_Conflict_Returns_Conflict()
    {
        var (handler, repo, uow, publisher) = Build();
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.Low, Now);
        repo.Seed(item);
        uow.ThrowOnSave = new ConcurrencyConflictException("conflict");

        var result = await handler.HandleAsync(new CompleteTaskCommand(item.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Empty(publisher.Events);
    }

    /// <summary>Детерминированные часы для тестов.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
