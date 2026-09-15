using System.Text.Json.Serialization;

namespace Dsh.Subagent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SubagentDescriptorPayload))]
[JsonSerializable(typeof(AssistantOutputFold))]
internal sealed partial class DshSubagentJsonContext : JsonSerializerContext
{
}

