using Npgsql;
using Serilog;
using Serilog.Events;
using Tasks.CDC.Messaging;
using Tasks.CDC.Replication;

namespace Tasks.CDC;

/// <summary>
/// Композиционный корень Worker-сервиса Tasks.CDC.
///
/// Вынесен из top-level <c>Program.cs</c> в отдельный тип, чтобы интеграционные
/// тесты могли собрать такой же хост программно (по образцу Tasks.Consumer.ConsumerHost).
/// </summary>
public static class CdcHost
{
    /// <summary>Собирает (но не запускает) хост CDC-воркера.</summary>
    public static IHost Build(string[] args) => Build(args, configure: null);

    /// <summary>
    /// Собирает хост с дополнительной настройкой конфигурации (используется тестами
    /// для подстановки строки подключения к БД и параметров RabbitMQ).
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
        // ---------------------------------------------------------------------
        var logsDirectory = ResolveLogsDirectory(builder.Configuration);
        Directory.CreateDirectory(logsDirectory);
        var logFilePath = Path.Combine(logsDirectory, "cdc-.log");

        builder.Services.AddSerilog((_, loggerConfiguration) =>
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

        // Источник данных Npgsql для служебных таблиц (cdc_processed) и публикации.
        builder.Services.AddSingleton(_ =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            return dataSourceBuilder.Build();
        });

        // Настройки репликации (секция Cdc) + строка подключения.
        builder.Services.AddOptions<ReplicationOptions>()
            .Configure(options =>
            {
                options.ConnectionString = connectionString;
                builder.Configuration.GetSection(ReplicationOptions.SectionName).Bind(options);
            });

        builder.Services.Configure<RabbitMqOptions>(
            builder.Configuration.GetSection(RabbitMqOptions.SectionName));

        // Компоненты CDC (singletons: живут всё время работы воркера).
        builder.Services.AddSingleton<PublicationInitializer>();
        builder.Services.AddSingleton<LogicalReplicationReader>();
        builder.Services.AddSingleton<OutboxInsertDecoder>();
        builder.Services.AddSingleton<ReplicationPositionStore>();

        builder.Services.AddSingleton<RabbitMqPublisher>();
        builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());

        builder.Services.AddHostedService<CdcWorker>();

        // Health-checks (readiness перенесён из API): доступность RabbitMQ.
        // Периодический отчёт в лог выполняет HealthReporterService.
        builder.Services.AddHealthChecks()
            .AddRabbitMQ(
                sp => sp.GetRequiredService<RabbitMqPublisher>().GetConnectionAsync(),
                name: "rabbitmq",
                tags: ["ready"]);

        builder.Services.AddHostedService<HealthReporterService>();
    }

    /// <summary>
    /// Определяет каталог для файловых логов.
    /// Приоритет: <c>Logs:Directory</c> (из .env: LOGS_DIR -> Logs__Directory),
    /// иначе <c>logs</c> в корне репозитория.
    /// </summary>
    private static string ResolveLogsDirectory(IConfiguration configuration)
    {
        var configured = configuration["Logs:Directory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var current = Directory.GetCurrentDirectory();
        var repoRoot = Path.GetFullPath(Path.Combine(current, "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "logs");
    }
}
