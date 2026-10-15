using Tasks.Api.Domain;
using Tasks.Api.Domain.Exceptions;

namespace Tasks.Tests.Domain;

/// <summary>Юнит-тесты инвариантов и поведения сущности TaskItem.</summary>
public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Defaults_And_Does_Not_Complete()
    {
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.High, Now);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.False(item.IsCompleted);
        Assert.Null(item.CompletedAt);
        Assert.Equal(Now, item.CreatedAt);
        Assert.Equal(Priority.High, item.Priority);
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void Complete_Sets_State_And_Raises_Domain_Event()
    {
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.Medium, Now);

        item.Complete(Now.AddMinutes(5));

        Assert.True(item.IsCompleted);
        Assert.Equal(Now.AddMinutes(5), item.CompletedAt);

        var evt = Assert.Single(item.DomainEvents);
        var completed = Assert.IsType<Tasks.Api.Domain.Events.TaskCompletedDomainEvent>(evt);
        Assert.Equal(item.Id, completed.TaskId);
        Assert.Equal("Buy milk", completed.Title);
        Assert.Equal(Now.AddMinutes(5), completed.CompletedAt);
        Assert.Equal(Priority.Medium, completed.Priority);
    }

    [Fact]
    public void Complete_Twice_Throws()
    {
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.Low, Now);
        item.Complete(Now);

        var ex = Assert.Throws<DomainException>(() => item.Complete(Now.AddMinutes(1)));
        Assert.Equal("task_already_completed", ex.Code);
    }

    [Fact]
    public void ClearDomainEvents_Removes_Events()
    {
        var item = TaskItem.Create(TaskTitle.Create("Buy milk"), Priority.Low, Now);
        item.Complete(Now);

        item.ClearDomainEvents();

        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void Title_Is_Trimmed()
    {
        var item = TaskItem.Create(TaskTitle.Create("  Buy milk  "), Priority.Low, Now);
        Assert.Equal("Buy milk", item.Title.Value);
    }
}
