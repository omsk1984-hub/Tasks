using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tasks.Core.Messaging;

/// <summary>
/// Единые настройки JSON для обмена событиями задач (camelCase + enum строкой).
///
/// Ключевой момент: <see cref="JsonStringEnumConverter"/> обязателен, иначе
/// <c>JsonSerializerDefaults.Web</c> сериализует enum числом (например, <c>"priority": 2</c>),
/// а потребитель, ожидающий строку (<c>"High"</c>), падает с
/// <c>JsonException: Cannot get the value of a token type 'Number' as a string</c>.
///
/// Использоваться и продюсером, и потребителем — единый источник правды по формату.
/// </summary>
public static class JsonSerializationDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
