using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Tasks.Consumer.Data;
using Tasks.Consumer.Messaging;

namespace Tasks.Consumer;

/// <summary>
/// Композиционный корень Worker-сервиса Tasks.Consumer.
///
/// Вынесен из top-level <c>Program.cs</c> в отдельный тип, чтобы:
///   - интеграционные тесты могли собрать точно такой же хост программно
///     (без запуска точки входа);
///   - не конфликтовать по имени неявного класса <c>Program</c> с
///     <c>Tasks.Api.Program</c> при совместной ссылке обоих проектов из тестов.
/// </summary>
public static class ConsumerHost
{
    /// <summary>
    /// Собирает (но не запускает) хост consumer-а: конфигурация, Serilog,
    /// DbContext, репозиторий и <see cref="TaskCompletedConsumer"/>.
    /// Миграции применяются отдельно через <see cref="Extensions.HostExtensions.MigrateDatabaseAsync"/>.
    /// </summary>
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        Configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Собирает хост с дополнительной настройкой конфигурации (используется тестами
    /// для подстановки строки подключения к БД и параметров RabbitMQ контейнеров).
    /// </summary>
    public static IHost Build(string[] args, Action<HostApplicationBuilder>? configure)
    {
        var builder = Host.CreateApplicationBuilder(args);
        Configure(builder);
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static void Configure(HostApplicationBuilder builder)
    {
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
    }

    /// <summary>
    /// Определяет каталог для файловых логов.
    /// Приоритет: конфигурация <c>Logs:Directory</c> (из .env: LOGS_DIR -> Logs__Directory),
    /// иначе каталог <c>logs</c> в корне репозитория.
    /// </summary>
    private static string ResolveLogsDirectory(IConfiguration configuration)
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
}
