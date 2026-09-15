using System.Text.Json.Serialization;

namespace Dsh.SessionQuery;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionQueryHit))]
[JsonSerializable(typeof(SessionSearchResult))]
internal sealed partial class DshSessionQueryJsonContext : JsonSerializerContext
{
}
