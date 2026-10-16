using Tasks.Api.Application;
using Tasks.Api.Application.Tasks.GetTasks;
using Tasks.Api.Domain;

namespace Tasks.Tests.Application;

/// <summary>Тесты пагинации для GetTasksHandler.</summary>
public sealed class GetTasksHandlerTests
{
    private static TaskItem CreateTask(string title, Priority priority, DateTimeOffset? createdAt = null)
    {
        return TaskItem.Create(TaskTitle.Create(title), priority, createdAt ?? DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_WithoutPagination_ReturnsAllItems()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        repo.Seed(CreateTask("Task 1", Priority.Low));
        repo.Seed(CreateTask("Task 2", Priority.Medium, DateTimeOffset.UtcNow.AddMinutes(1)));
        repo.Seed(CreateTask("Task 3", Priority.High, DateTimeOffset.UtcNow.AddMinutes(2)));

        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(3);
        result.Value!.TotalCount.Should().Be(3);
        result.Value!.Page.Should().Be(1);
        result.Value!.PageSize.Should().Be(3);
        result.Value!.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithPagination_ReturnsPagedItems()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        for (int i = 0; i < 10; i++)
        {
            repo.Seed(CreateTask($"Task {i}", Priority.Low, DateTimeOffset.UtcNow.AddMinutes(i)));
        }

        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 2, PageSize: 3), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(3);
        result.Value!.TotalCount.Should().Be(10);
        result.Value!.Page.Should().Be(2);
        result.Value!.PageSize.Should().Be(3);
        result.Value!.TotalPages.Should().Be(4); // 10 / 3 = 4 (округление вверх)
    }

    [Fact]
    public async Task HandleAsync_Page1_ReturnsFirstPage()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        for (int i = 0; i < 5; i++)
        {
            repo.Seed(CreateTask($"Task {i}", Priority.Low, DateTimeOffset.UtcNow.AddMinutes(i)));
        }

        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 1, PageSize: 2), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value!.Page.Should().Be(1);
        result.Value!.TotalPages.Should().Be(3); // 5 / 2 = 3
    }

    [Fact]
    public async Task HandleAsync_LastPage_ReturnsRemainingItems()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        for (int i = 0; i < 7; i++)
        {
            repo.Seed(CreateTask($"Task {i}", Priority.Low, DateTimeOffset.UtcNow.AddMinutes(i)));
        }

        var handler = new GetTasksHandler(repo);

        // Act — страница 4 из 4 (последняя неполная)
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 4, PageSize: 2), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1); // 7 % 2 = 1
        result.Value!.TotalCount.Should().Be(7);
        result.Value!.TotalPages.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsync_EmptyList_ReturnsEmptyPaginatedResult()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value!.TotalCount.Should().Be(0);
        result.Value!.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_InvalidPage_ReturnsFailure()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 0, PageSize: 10), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error!.Message.Should().Contain("Page must be greater than 0");
    }

    [Fact]
    public async Task HandleAsync_InvalidPageSize_ReturnsFailure()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        var handler = new GetTasksHandler(repo);

        // Act — pageSize > 100
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 1, PageSize: 101), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error!.Message.Should().Contain("PageSize must be between 1 and 100");
    }

    [Fact]
    public async Task HandleAsync_ZeroPageSize_ReturnsFailure()
    {
        // Arrange
        var repo = new FakeTaskRepository();
        var handler = new GetTasksHandler(repo);

        // Act
        var result = await handler.HandleAsync(new GetTasksQuery(Page: 1, PageSize: 0), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
    }
}
