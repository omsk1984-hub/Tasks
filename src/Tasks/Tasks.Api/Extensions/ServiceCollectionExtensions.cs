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
        services.AddDbContext<TasksDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(TasksDbContext).Assembly.FullName)));

        // Абстракция времени — удобно для тестов и корректного UTC.
        services.AddSingleton(TimeProvider.System);

        // Persistence-адаптеры (DIP: Application зависит от интерфейсов, не от EF).
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Transactional outbox: события пишутся в БД в одной транзакции с агрегатом,
        // а публикует их CDC-воркер (Tasks.CDC) через logical replication.
        // API больше НЕ публикует в RabbitMQ напрямую.
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();

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
