using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tasks.Api.Contracts;
using Tasks.Api.Data;
using Tasks.Api.Domain;
using Testcontainers.PostgreSql;

namespace Tasks.Tests;

/// <summary>
/// Интеграционный тест сквозного сценария с outbox:
///   1) создать задачу через API;
///   2) завершить её;
///   3) проверить, что событие записано в таблицу outbox_messages
///      (публикацию в RabbitMQ выполняет отдельный CDC-воркер Tasks.CDC).
///
/// Работает против реальной БД (Testcontainers PostgreSQL). Если Docker-эндпоинт
/// недоступен процессу теста, используется строка подключения из окружения
/// (TASKS_TEST_POSTGRES); при её отсутствии проверка outbox пропускается.
/// </summary>
public sealed class CompleteTaskIntegrationTest : IAsyncLifetime
{
    // API отдаёт enum-ы строками ("High") — тестовый десериализатор должен это учитывать.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private bool _dbAvailable;

    // Id задач, созданных тестом (все с префиксом Test_): для очистки из БД после прогона.
    private readonly List<Guid> _createdTaskIds = [];

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("tasks_db")
                .WithUsername("tasks_user")
                .WithPassword("tasks_password")
                .WithName($"Tasks_test_pg_{Guid.NewGuid():N}")
                .Build();

            await _postgres.StartAsync();
            _dbAvailable = true;
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            await SafeDisposeContainersAsync();
            _dbAvailable = false;
        }

        var connectionString = _dbAvailable
            ? _postgres!.GetConnectionString()
            : Environment.GetEnvironmentVariable("TASKS_TEST_POSTGRES");
        _dbAvailable = !string.IsNullOrWhiteSpace(connectionString);

        var builder = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                if (_dbAvailable)
                {
                    webHost.UseSetting("ConnectionStrings:Postgres", connectionString);
                }
                webHost.UseSetting("Db:MigrateOnStartup", "true");
            });

        _factory = builder;
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        // Чистим созданные тестом записи, пока фабрика (и её DI) ещё живы.
        await CleanupTestRowsAsync();

        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await SafeDisposeContainersAsync();
    }

    /// <summary>
    /// Удаляет из БД созданные тестом задачи и соответствующие строки outbox
    /// (title с префиксом Test_).
    /// </summary>
    private async Task CleanupTestRowsAsync()
    {
        if (_createdTaskIds.Count == 0 || _factory is null)
        {
            return;
        }

        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            foreach (var id in _createdTaskIds)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM outbox_messages WHERE id = {id}");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM tasks WHERE id = {id} AND title LIKE 'Test!_%' ESCAPE '!'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IntegrationTest cleanup] Не удалось удалить тестовые записи: {ex.Message}");
        }
    }

    [Fact]
    public async Task Create_Then_Complete_Writes_Event_To_Outbox()
    {
        // --- 1. Создание задачи ---
        var createResponse = await _client.PostAsJsonAsync("/tasks", new { title = "Test_Buy milk", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);
        Assert.NotNull(created);
        _createdTaskIds.Add(created!.Id);
        Assert.Equal("Test_Buy milk", created.Title);
        Assert.False(created.IsCompleted);
        Assert.Null(created.CompletedAt);            // CompletedAt НЕ заполняется при создании
        Assert.Equal(Priority.High, created.Priority);

        // --- 2. Завершение задачи ---
        var completeResponse = await _client.PutAsync($"/tasks/{created.Id}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var completed = await completeResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);
        Assert.NotNull(completed);
        Assert.True(completed!.IsCompleted);
        Assert.NotNull(completed.CompletedAt);       // CompletedAt заполнен при завершении

        // --- 3. Проверка записи в outbox (публикует CDC-воркер) ---
        if (!_dbAvailable)
        {
            // БД недоступна из процесса теста — сценарий outbox не проверяем.
            return;
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var outbox = await db.OutboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == created.Id);

        Assert.NotNull(outbox);
        Assert.Equal("task.completed", outbox!.EventType);
        Assert.False(string.IsNullOrWhiteSpace(outbox.Payload));

        // Payload — готовый JSON контракта (camelCase, priority строкой).
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(created.Id, payload.RootElement.GetProperty("taskId").GetGuid());
        Assert.Equal("Test_Buy milk", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal("High", payload.RootElement.GetProperty("priority").GetString());
        Assert.True(payload.RootElement.TryGetProperty("completedAt", out var completedAtProp));
        Assert.True(DateTimeOffset.TryParse(completedAtProp.GetString(), out _));
    }

    [Fact]
    public async Task Complete_Twice_Returns_Conflict()
    {
        var createResponse = await _client.PostAsJsonAsync("/tasks", new { title = "Test_Double complete" });
        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);
        Assert.NotNull(created);
        _createdTaskIds.Add(created!.Id);

        var first = await _client.PutAsync($"/tasks/{created!.Id}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PutAsync($"/tasks/{created.Id}/complete", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_With_Empty_Title_Returns_BadRequest()
    {
        var response = await _client.PostAsJsonAsync("/tasks", new { title = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------

    private static bool IsDockerUnavailable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DotNet.Testcontainers.Builders.DockerUnavailableException)
            {
                return true;
            }
            if (current.Message.Contains("docker", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task SafeDisposeContainersAsync()
    {
        if (_postgres is not null)
        {
            try { await _postgres.DisposeAsync(); } catch { /* ignore */ }
            _postgres = null;
        }
    }
}
