using Microsoft.EntityFrameworkCore;
using Tasks.Api.Application.Abstractions;
using Tasks.Api.Application.Tasks.CompleteTask;
using Tasks.Api.Application.Tasks.CreateTask;
using Tasks.Api.Application.Tasks.DeleteTask;
using Tasks.Api.Application.Tasks.GetTasks;
using Tasks.Api.Data;
using Tasks.Api.Messaging;

namespace Tasks.Api.Extensions;

/// <summary>
/// Регистрация сервисов приложения. Вынесена из Program.cs по SRP:
/// точка входа занимается только композицией, а конкретные регистрации — здесь.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTasksInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddDbContext<TasksDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(TasksDbContext).Assembly.FullName)));

        // Абстракция времени — удобно для тестов и корректного UTC.
        services.AddSingleton(TimeProvider.System);

        // Persistence-адаптеры (DIP: Application зависит от интерфейсов, не от EF).
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Публикация в RabbitMQ (publish-only). Singleton: живут одно соединение и канал.
        // Флаг RabbitMq:UseRecordingPublisher=true переключает на in-memory реализацию
        // (для тестов/окружений без доступного брокера).
        var useRecordingPublisher = configuration.GetValue<bool>("RabbitMq:UseRecordingPublisher");
        if (useRecordingPublisher)
        {
            services.AddSingleton<RecordingTaskEventPublisher>();
            services.AddSingleton<ITaskEventPublisher>(sp => sp.GetRequiredService<RecordingTaskEventPublisher>());
            services.AddSingleton<IBrokerHealthProbe>(sp => sp.GetRequiredService<RecordingTaskEventPublisher>());
        }
        else
        {
            services.AddSingleton<RabbitMqTaskEventPublisher>();
            services.AddSingleton<ITaskEventPublisher>(sp => sp.GetRequiredService<RabbitMqTaskEventPublisher>());
            services.AddSingleton<IBrokerHealthProbe>(sp => sp.GetRequiredService<RabbitMqTaskEventPublisher>());
        }

        return services;
    }

    public static IServiceCollection AddTasksApplication(this IServiceCollection services)
    {
        // Обработчики use-case'ов (CQRS-lite): по одной ответственности на класс.
        services.AddScoped<CreateTaskHandler>();
        services.AddScoped<GetTasksHandler>();
        services.AddScoped<CompleteTaskHandler>();
        services.AddScoped<DeleteTaskHandler>();

        return services;
    }
}
