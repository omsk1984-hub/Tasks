namespace Tasks.Consumer;

/// <summary>
/// Точка входа Worker-сервиса Tasks.Consumer.
///
/// Явный класс (а не top-level statements) нужен, чтобы тип точки входа жил
/// в namespace <c>Tasks.Consumer</c> и не конфликтовал с <c>Tasks.Api.Program</c>
/// при совместной ссылке обоих проектов из интеграционных тестов.
/// Вся композиция хоста — в <see cref="ConsumerHost"/>.
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // UTF-8 для консоли: иначе кириллица в логах приложения ломается при запуске
        // из дочернего процесса (см. правило проекта про кодировки .NET-процессов).
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var host = ConsumerHost.Build(args);
        await host.MigrateDatabaseAsync();
        await host.RunAsync();
    }
}
