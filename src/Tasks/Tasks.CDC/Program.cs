using Tasks.CDC;
using Tasks.CDC.Extensions;

namespace Tasks.CDC;

/// <summary>
/// Точка входа CDC-воркера. Оформлена как явный класс в namespace <c>Tasks.CDC</c>
/// (не top-level Program), чтобы не конфликтовать в глобальном namespace с
/// <c>Tasks.Api.Program</c> при совместной ссылке проектов из тестов.
/// </summary>
public static class CdcProgram
{
    public static async Task Main(string[] args)
    {
        // UTF-8 для консоли: иначе кириллица в логах ломается при запуске
        // из дочернего процесса (см. правило проекта про кодировки .NET-процессов).
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var host = CdcHost.Build(args);

        // Готовим публикацию и служебную таблицу дедупликации до старта воркера.
        await host.EnsureReplicationSetupAsync();

        await host.RunAsync();
    }
}
