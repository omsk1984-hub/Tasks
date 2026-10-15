using System.Text.Json;
using Npgsql.Replication.PgOutput.Messages;
using Tasks.Core.Messaging;

namespace Tasks.CDC.Replication;

/// <summary>
/// Преобразует INSERT в таблицу outbox_messages (типизированное сообщение pgoutput
/// от Npgsql) в интеграционное событие <see cref="TaskCompletedEvent"/>.
///
/// Опорные колонки outbox: id (uuid), event_type (varchar), payload (jsonb),
/// occurred_at (timestamptz). Значения читаются через Npgsql-типы (Get<T>),
/// payload — уже готовый JSON контракта.
/// </summary>
public sealed class OutboxInsertDecoder(ILogger<OutboxInsertDecoder> logger)
{
    // Единые настройки десериализации (camelCase + enum строкой).
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializationDefaults.Options;

    /// <summary>Тип события task.completed (routing key в RabbitMQ).</summary>
    public const string TaskCompletedEventType = "task.completed";

    /// <summary>
    /// Пытается декодировать INSERT в outbox как событие task.completed.
    /// Возвращает null, если строка не относится к ожидаемому типу или payload
    /// некорректен — такие строки пропускаются без падения.
    /// </summary>
    public async Task<DecodedOutboxEvent?> TryDecodeAsync(
        InsertMessage insert,
        CancellationToken cancellationToken)
    {
        var relation = insert.Relation;
        if (relation is null)
        {
            logger.LogWarning("Insert без Relation: пропускаю.");
            return null;
        }

        // Собираем значения колонок по именам: tuple идёт в порядке колонок relation.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var index = 0;

        await using var enumerator = insert.NewRow.GetAsyncEnumerator(cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            var value = enumerator.Current;
            var columnName = index < relation.Columns.Count
                ? relation.Columns[index].ColumnName
                : $"col{index}";

            values[columnName] = value.IsDBNull
                ? null
                : await value.Get<string>(cancellationToken);

            index++;
        }

        if (!values.TryGetValue("event_type", out var eventType) || eventType is null)
        {
            logger.LogWarning("В outbox-строке нет event_type: пропускаю.");
            return null;
        }

        if (!string.Equals(eventType, TaskCompletedEventType, StringComparison.Ordinal))
        {
            // Другие события outbox пока не обрабатываем — не ошибка.
            logger.LogDebug("Пропускаю событие outbox типа '{EventType}'.", eventType);
            return null;
        }

        if (!values.TryGetValue("id", out var idText) || !Guid.TryParse(idText, out var id))
        {
            logger.LogWarning("В outbox-строке некорректный id='{Id}': пропускаю.", idText);
            return null;
        }

        if (!values.TryGetValue("payload", out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            logger.LogWarning("В outbox-строке пустой payload для id={Id}: пропускаю.", id);
            return null;
        }

        try
        {
            var @event = JsonSerializer.Deserialize<TaskCompletedEvent>(payload, JsonOptions);
            if (@event is null)
            {
                logger.LogWarning("payload для id={Id} десериализовался в null: пропускаю.", id);
                return null;
            }

            return new DecodedOutboxEvent(id, eventType, @event, payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось десериализовать payload outbox для id={Id}.", id);
            return null;
        }
    }
}

/// <summary>
/// Декодированное из outbox событие: id строки, тип (routing key),
/// распознанный контракт и исходный JSON (публикуется «как есть»).
/// </summary>
public sealed record DecodedOutboxEvent(
    Guid Id,
    string EventType,
    TaskCompletedEvent Event,
    string RawPayload);
