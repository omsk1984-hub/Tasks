using Microsoft.Extensions.Logging.Abstractions;
using Tasks.Api.Application;
using Tasks.Api.Application.Tasks.CreateTask;
using Tasks.Api.Domain;

namespace Tasks.Tests.Application;

/// <summary>Юнит-тесты use-case создания задачи.</summary>
public sealed class CreateTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static (CreateTaskHandler Handler, FakeTaskRepository Repo, FakeUnitOfWork Uow) Build()
    {
        var repo = new FakeTaskRepository();
        var uow = new FakeUnitOfWork();
        var handler = new CreateTaskHandler(repo, uow, new FixedTimeProvider(Now));
        return (handler, repo, uow);
    }

    [Fact]
    public async Task Create_Valid_Command_Succeeds()
    {
        var (handler, _, uow) = Build();

        var result = await handler.HandleAsync(new CreateTaskCommand("Buy milk", Priority.High), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Buy milk", result.Value!.Title);
        Assert.Equal(Priority.High, result.Value.Priority);
        Assert.False(result.Value.IsCompleted);
        Assert.Null(result.Value.CompletedAt);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Create_Default_Priority_Is_Medium()
    {
        var (handler, _, _) = Build();

        var result = await handler.HandleAsync(new CreateTaskCommand("Task", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Priority.Medium, result.Value!.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_Invalid_Title_Returns_Validation(string? title)
    {
        var (handler, _, uow) = Build();

        var result = await handler.HandleAsync(new CreateTaskCommand(title, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(0, uow.SaveCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
