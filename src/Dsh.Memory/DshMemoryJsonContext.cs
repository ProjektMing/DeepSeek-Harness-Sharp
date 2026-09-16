using System.Text.Json.Serialization;

namespace Dsh.Memory;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemorySaveResult))]
internal sealed partial class DshMemoryJsonContext : JsonSerializerContext
{
}
