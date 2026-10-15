using Microsoft.Extensions.Options;
using Npgsql;

namespace Tasks.CDC.Replication;

/// <summary>
/// Идемпотентно создаёт публикацию для таблицы outbox, если её ещё нет.
/// Публикация — часть логической репликации: слот передаёт изменения только тех
/// таблиц, что включены в publication. Создавать слот вручную не нужно —
/// он создаётся командой START_REPLICATION с опцией CREATE_SLOT неявно (мы создаём
/// его отдельно, см. <see cref="LogicalReplicationReader"/>).
/// </summary>
public sealed class PublicationInitializer(
    NpgsqlDataSource dataSource,
    IOptions<ReplicationOptions> options,
    ILogger<PublicationInitializer> logger)
{
    private readonly ReplicationOptions _options = options.Value;

    /// <summary>
    /// Создаёт публикацию (IF NOT EXISTS недоступен для CREATE PUBLICATION,
    /// поэтому проверяем каталог pg_publication).
    /// </summary>
    public async Task EnsurePublicationAsync(CancellationToken cancellationToken)
    {
        var publication = _options.PublicationName;
        var table = _options.OutboxTable;

        const string existsSql = "SELECT 1 FROM pg_publication WHERE pubname = @name;";
        await using (var exists = dataSource.CreateCommand(existsSql))
        {
            exists.Parameters.AddWithValue("name", publication);
            var result = await exists.ExecuteScalarAsync(cancellationToken);
            if (result is not null)
            {
                logger.LogInformation("Публикация '{Publication}' уже существует.", publication);
                return;
            }
        }

        // Имена идентификаторов не параметризуются — подставляем из конфигурации.
        // Значения приходят из доверенного appsettings/.env (не из пользовательского ввода).
        var createSql = $"CREATE PUBLICATION {Quote(publication)} FOR TABLE {Quote(table)};";
        await using (var create = dataSource.CreateCommand(createSql))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation(
            "Создана публикация '{Publication}' для таблицы '{Table}'.", publication, table);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
