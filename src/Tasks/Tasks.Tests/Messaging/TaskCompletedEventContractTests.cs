using System.Text.Json;
using Tasks.Core;
using Tasks.Core.Messaging;

namespace Tasks.Tests.Messaging;

/// <summary>
/// Фиксирует контракт обмена событием task.completed между Tasks.Api (продюсер)
/// и Tasks.Consumer (потребитель). Защищает от регрессии, когда enum Priority
/// сериализуется числом, а потребитель ожидает строку
/// (JsonException: Cannot get the value of a token type 'Number' as a string).
/// </summary>
public sealed class TaskCompletedEventContractTests
{
    [Fact]
    public void Serialize_WritesPriorityAsString()
    {
        var @event = new TaskCompletedEvent(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Buy milk",
            DateTimeOffset.Parse("2026-10-15T10:00:00+00:00"),
            Priority.High);

        var json = JsonSerializer.Serialize(@event, JsonSerializationDefaults.Options);

        // priority сериализуется строкой ("High"), а не числом (2).
        Assert.Contains("\"priority\":\"High\"", json);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new TaskCompletedEvent(
            Guid.NewGuid(),
            "Buy milk",
            DateTimeOffset.Parse("2026-10-15T10:00:00+00:00"),
            Priority.Medium);

        var json = JsonSerializer.Serialize(original, JsonSerializationDefaults.Options);
        var restored = JsonSerializer.Deserialize<TaskCompletedEvent>(json, JsonSerializationDefaults.Options);

        Assert.NotNull(restored);
        Assert.Equal(original.TaskId, restored!.TaskId);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.CompletedAt, restored.CompletedAt);
        Assert.Equal(Priority.Medium, restored.Priority);
    }

    [Fact]
    public void Deserialize_FromProducerPayload_WithStringPriority_Succeeds()
    {
        // Реальный payload продюсера после исправления: priority строкой.
        const string payload = """
            {"taskId":"11111111-1111-1111-1111-111111111111","title":"Buy milk","completedAt":"2026-10-15T10:00:00+00:00","priority":"High"}
            """;

        var @event = JsonSerializer.Deserialize<TaskCompletedEvent>(payload, JsonSerializationDefaults.Options);

        Assert.NotNull(@event);
        Assert.Equal(Priority.High, @event!.Priority);
    }
}
