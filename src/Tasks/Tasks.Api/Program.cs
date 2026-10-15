using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Tasks.Api.Data;
using Tasks.Api.Endpoints;
using Tasks.Api.Messaging;

// UTF-8 для консоли: иначе кириллица в логах приложения ломается при запуске
// из дочернего процесса (см. правило проекта про кодировки .NET-процессов).
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Конфигурация
// ---------------------------------------------------------------------
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["Db:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Не задана строка подключения к PostgreSQL. Укажите ConnectionStrings:Postgres " +
        "или переменную окружения ConnectionStrings__Postgres / Db__ConnectionString.");

// ---------------------------------------------------------------------
// Сервисы
// ---------------------------------------------------------------------
builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly(typeof(TasksDbContext).Assembly.FullName)));

// Абстракция времени — удобно для тестов и корректного UTC.
builder.Services.AddSingleton(TimeProvider.System);

// JSON: enum-ы сериализуем/десериализуем как строки ("High", "Medium", "Low"),
// как в примере сообщения из ТЗ ("priority": "High").
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Публикация в RabbitMQ (publish-only). Singleton: живут одно соединение и канал.
// Флаг RabbitMq:UseRecordingPublisher=true переключает на in-memory реализацию:
// удобно для тестов/окружений без доступного брокера.
var useRecordingPublisher = builder.Configuration.GetValue<bool>("RabbitMq:UseRecordingPublisher");
if (useRecordingPublisher)
{
    builder.Services.AddSingleton<ITaskEventPublisher, RecordingTaskEventPublisher>();
}
else
{
    builder.Services.AddSingleton<ITaskEventPublisher, RabbitMqTaskEventPublisher>();
}

// ProblemDetails для 400/409 и т.п.
builder.Services.AddProblemDetails();

// Health-checks: liveness (сам процесс) и readiness (БД + RabbitMQ).
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddRabbitMQ(
        serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
            // Строка подключения RabbitMQ в формате amqp://.
            return Task.FromResult<RabbitMQ.Client.IConnection>(new RabbitMQ.Client.ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.User,
                Password = options.Password,
                VirtualHost = options.VirtualHost
            }.CreateConnectionAsync().GetAwaiter().GetResult());
        },
        name: "rabbitmq",
        tags: ["ready"]);

// Swagger — не требуется ТЗ, но полезен для ручной проверки (только в Development).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------
// Применение миграций при старте (упрощение для тестового задания).
// В проде так делать не стоит — миграции обычно выносят в отдельный шаг deploy.
// ---------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
    await db.Database.MigrateAsync();
}

// ---------------------------------------------------------------------
// Пайплайн
// ---------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------------------------------------------------------------------
// Эндпоинты
// ---------------------------------------------------------------------
app.MapTasksEndpoints();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // liveness: сам процесс жив, без внешних зависимостей
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

/// <summary>
/// Точка входа вынесена в partial-класс, чтобы интеграционные тесты могли
/// использовать <c>WebApplicationFactory<Program></c>.
/// </summary>
public partial class Program;
