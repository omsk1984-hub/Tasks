using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tasks.Api.Contracts;
using Tasks.Consumer;
using Tasks.Consumer.Data;

namespace Tasks.Tests;

/// <summary>
/// Сквозной интеграционный тест полного пути из ТЗ:
///   API (create + complete) → публикация в RabbitMQ →
///   реальный Tasks.Consumer (тот же хост, что и в проде) → запись в БД (tasks_from_rmq).
///
/// Тест работает против ЛОКАЛЬНО запущенной инфраструктуры с текущими настройками
/// окружения (см. .env): PostgreSQL — localhost:5442/tasks_db, RabbitMQ — localhost:5673.
/// Никаких Testcontainers: сервисы поднимаются локально, как в run_Tasks.ps1 / run_Consumer.ps1.
///
/// Все записи, создаваемые тестом, помечаются префиксом "Test_", чтобы их можно было
/// отличить от боевых данных и вычистить.
/// </summary>
public sealed class CompleteToConsumerE2ETest : IAsyncLifetime
{
    private const string Exchange = "task.events";
    private const string RoutingKey = "task.completed";
    private const string Queue = "tasks.task.completed";

    // Префикс всех тестовых записей — требование проекта.
    private const string TitlePrefix = "Test_";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private IHost? _consumerHost;
    private string _connectionString = null!;
    private readonly List<Guid> _createdTaskIds = [];

    public async Task InitializeAsync()
    {
        // Текущие настройки окружения (.env): сначала переменные процесса, иначе дефолты,
        // совпадающие со значениями из .env.
        _connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5442;Database=tasks_db;Username=tasks_user;Password=tasks_password";

        var mqHost = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
        var mqPort = Environment.GetEnvironmentVariable("RABBITMQ__PORT") ?? "5673";
        var mqUser = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
        var mqPassword = Environment.GetEnvironmentVariable("RABBITMQ__PASSWORD") ?? "guest";

        // --- API: текущая БД и локальный RabbitMQ, реальная публикация ---
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                webHost.UseSetting("ConnectionStrings:Postgres", _connectionString);
                webHost.UseSetting("RabbitMq:Host", mqHost);
                webHost.UseSetting("RabbitMq:Port", mqPort);
                webHost.UseSetting("RabbitMq:User", mqUser);
                webHost.UseSetting("RabbitMq:Password", mqPassword);
                webHost.UseSetting("RabbitMq:Exchange", Exchange);
                webHost.UseSetting("RabbitMq:RoutingKey", RoutingKey);
                webHost.UseSetting("RabbitMq:UseRecordingPublisher", "false");
                webHost.UseSetting("Db:MigrateOnStartup", "true");
            });
        _client = _factory.CreateClient();

        // --- Реальный consumer: тот же хост, что запускает run_Consumer.ps1 ---
        _consumerHost = ConsumerHost.Build(Array.Empty<string>(), builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["Db:MigrateOnStartup"] = "true",
                ["RabbitMq:Host"] = mqHost,
                ["RabbitMq:Port"] = mqPort,
                ["RabbitMq:User"] = mqUser,
                ["RabbitMq:Password"] = mqPassword,
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Exchange"] = Exchange,
                ["RabbitMq:RoutingKey"] = RoutingKey,
                ["RabbitMq:Queue"] = Queue,
                ["Logs:Directory"] = Path.Combine(Path.GetTempPath(), "tasks-consumer-tests")
            });
        });

        await _consumerHost.MigrateDatabaseAsync();
        await _consumerHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        // Чистим созданные тестом записи, ПОКА consumer-хост ещё жив
        // (DbContext резолвится из его ServiceProvider).
        await CleanupTestRowsAsync();

        if (_consumerHost is not null)
        {
            await _consumerHost.StopAsync(TimeSpan.FromSeconds(10));
            _consumerHost.Dispose();
        }

        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Complete_Publishes_Event_And_Consumer_Writes_Row_To_Db()
    {
        // --- 1. Создание задачи с приоритетом High (заголовок с префиксом Test_) ---
        var title = $"{TitlePrefix}buy milk {Guid.NewGuid():N}";
        var createResponse = await _client!.PostAsJsonAsync("/tasks", new { title, priority = "High" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);
        Assert.NotNull(created);
        var taskId = created!.Id;
        _createdTaskIds.Add(taskId);
        Assert.Equal(Priority.High, created.Priority);

        // --- 2. Завершение задачи: публикует task.completed в RabbitMQ ---
        var completeResponse = await _client!.PutAsync($"/tasks/{taskId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        // --- 3. Ждём, пока consumer обработает сообщение и запишет строку ---
        var row = await WaitForRowAsync(taskId, TimeSpan.FromSeconds(30));
        Assert.NotNull(row);

        // --- 4. Проверяем содержимое проекции ---
        Assert.Equal(taskId, row!.TaskId);
        Assert.Equal(title, row.Title);
        Assert.Equal("High", row.Priority);                 // priority сохранён строкой
        Assert.Equal(RoutingKey, row.RoutingKey);
        Assert.NotEqual(default, row.CompletedAt);
        Assert.NotEqual(default, row.ProcessedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.RawPayload));

        // Оригинальное тело сообщения: priority должно быть строкой ("High"), не числом.
        using var payload = JsonDocument.Parse(row.RawPayload!);
        Assert.Equal("High", payload.RootElement.GetProperty("priority").GetString());
        Assert.Equal(title, payload.RootElement.GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------

    private async Task<TaskFromRmq?> WaitForRowAsync(Guid taskId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = _consumerHost!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ConsumerDbContext>();
            var row = await db.TasksFromRmq.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TaskId == taskId);
            if (row is not null)
            {
                return row;
            }

            await Task.Delay(250);
        }

        return null;
    }

    /// <summary>
    /// Удаляет из БД записи, созданные тестом (все с префиксом Test_):
    /// проекции tasks_from_rmq и исходные задачи tasks.
    /// </summary>
    private async Task CleanupTestRowsAsync()
    {
        if (_createdTaskIds.Count == 0 || _consumerHost is null)
        {
            return;
        }

        try
        {
            await using var scope = _consumerHost.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ConsumerDbContext>();
            foreach (var id in _createdTaskIds)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM tasks_from_rmq WHERE task_id = {id}");
                // tasks принадлежит API, но таблица — в той же БД: чистим по id,
                // ограничившись тестовыми записями (title начинается с Test_).
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM tasks WHERE id = {id} AND title LIKE 'Test!_%' ESCAPE '!'");
            }
        }
        catch (Exception ex)
        {
            // Cleanup не должен маскировать результат теста, но причину логируем.
            Console.WriteLine($"[E2E cleanup] Не удалось удалить тестовые записи: {ex.Message}");
        }
    }
}
