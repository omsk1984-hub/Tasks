using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Tasks.Consumer;
using Tasks.Consumer.Data;
using Tasks.Consumer.Messaging;

// UTF-8 для консоли: иначе кириллица в логах приложения ломается при запуске
// из дочернего процесса (см. правило проекта про кодировки .NET-процессов).
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------
// Конфигурация
// ---------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["Db:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Не задана строка подключения к PostgreSQL. Укажите ConnectionStrings:Postgres " +
        "или переменную окружения ConnectionStrings__Postgres.");

// ---------------------------------------------------------------------
// Логирование (Serilog): консоль + файл в общий каталог логов.
// Каталог берётся из Logs:Directory (пробрасывается из .env: LOGS_DIR ->
// Logs__Directory скриптом run_Consumer.ps1). Если не задан — <repoRoot>/logs.
// ---------------------------------------------------------------------
var logsDirectory = ResolveLogsDirectory(builder.Configuration);
Directory.CreateDirectory(logsDirectory);
var logFilePath = Path.Combine(logsDirectory, "consumer-.log");

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
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
builder.Services.AddDbContext<ConsumerDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsAssembly(typeof(ConsumerDbContext).Assembly.FullName)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TaskFromRmqRepository>();

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddHostedService<TaskCompletedConsumer>();

var host = builder.Build();

// ---------------------------------------------------------------------
// Применение миграций при старте (владелец таблицы tasks_from_rmq — consumer).
// ---------------------------------------------------------------------
await host.MigrateDatabaseAsync();

await host.RunAsync();

/// <summary>
/// Определяет каталог для файловых логов.
/// Приоритет: конфигурация <c>Logs:Directory</c> (из .env: LOGS_DIR -> Logs__Directory),
/// иначе каталог <c>logs</c> в корне репозитория.
/// </summary>
static string ResolveLogsDirectory(IConfiguration configuration)
{
    var configured = configuration["Logs:Directory"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    // Fallback: <repoRoot>/logs. Приложение запускается из src/Consumer/Tasks.Consumer,
    // поднимаемся на 3 уровня вверх до корня репозитория.
    var current = Directory.GetCurrentDirectory();
    var repoRoot = Path.GetFullPath(Path.Combine(current, "..", "..", ".."));
    return Path.Combine(repoRoot, "logs");
}
