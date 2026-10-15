using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Serilog;
using Serilog.Events;
using Tasks.Api.Endpoints;
using Tasks.Api.Extensions;
using Tasks.Api.Messaging;

// UTF-8 для консоли: иначе кириллица в логах приложения ломается при запуске
// из дочернего процесса (см. правило проекта про кодировки .NET-процессов).
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Конфигурация
// ---------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["Db:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Не задана строка подключения к PostgreSQL. Укажите ConnectionStrings:Postgres " +
        "или переменную окружения ConnectionStrings__Postgres / Db__ConnectionString.");

// ---------------------------------------------------------------------
// Логирование (Serilog): консоль + файл.
// Каталог логов берётся из Logs:Directory (пробрасывается из .env: LOGS_DIR ->
// Logs__Directory скриптом run_Tasks.ps1). Если не задан — fallback <repoRoot>/logs.
// ---------------------------------------------------------------------
var logsDirectory = ResolveLogsDirectory(builder.Configuration);
Directory.CreateDirectory(logsDirectory);
var logFilePath = Path.Combine(logsDirectory, "tasks-.log");

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        // Переопределяем путь файлового sink, подставляя вычисленный каталог
        // (в appsettings.json путь-заглушка, т.к. подстановка env-переменных там не работает).
        .WriteTo.File(
            path: logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
});

// ---------------------------------------------------------------------
// Сервисы (композиционный корень)
// ---------------------------------------------------------------------
builder.Services.AddTasksInfrastructure(builder.Configuration, connectionString);
builder.Services.AddTasksApplication();

// JSON: enum-ы сериализуем/десериализуем как строки ("High", "Medium", "Low"),
// как в примере сообщения из ТЗ ("priority": "High").
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ProblemDetails для 400/409 и т.п.
builder.Services.AddProblemDetails();

// Health-checks: liveness (сам процесс) и readiness (БД + RabbitMQ).
// RabbitMQ проверяется через абстракцию IBrokerHealthProbe (DIP), без ручного
// создания соединения в лямбде.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddRabbitMQ(
        serviceProvider => serviceProvider
            .GetRequiredService<RabbitMqTaskEventPublisher>()
            .GetConnectionAsync(),
        name: "rabbitmq",
        tags: ["ready"]);

// Swagger — не требуется ТЗ, но полезен для ручной проверки (только в Development).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------
// Применение миграций при старте (управляется флагом Db:MigrateOnStartup).
// В проде миграции обычно выносят в отдельный шаг deploy.
// ---------------------------------------------------------------------
await app.MigrateDatabaseAsync();

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

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // liveness: сам процесс жив, без внешних зависимостей
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

/// <summary>
/// Определяет каталог для файловых логов.
/// Приоритет: конфигурация <c>Logs:Directory</c> (пробрасывается из .env: LOGS_DIR ->
/// Logs__Directory), иначе каталог <c>logs</c> в корне репозитория.
/// </summary>
static string ResolveLogsDirectory(IConfiguration configuration)
{
    var configured = configuration["Logs:Directory"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    // Fallback: <repoRoot>/logs. Т.к. приложение запускается из src/Tasks/Tasks.Api,
    // поднимаемся на 3 уровня вверх до корня репозитория.
    var current = Directory.GetCurrentDirectory();
    var repoRoot = Path.GetFullPath(Path.Combine(current, "..", "..", ".."));
    return Path.Combine(repoRoot, "logs");
}

/// <summary>
/// Точка входа вынесена в partial-класс, чтобы интеграционные тесты могли
/// использовать <c>WebApplicationFactory<Program></c>.
/// </summary>
public partial class Program;
