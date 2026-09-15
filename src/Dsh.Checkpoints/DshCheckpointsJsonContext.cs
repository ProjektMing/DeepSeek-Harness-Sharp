using System.Text.Json.Serialization;

namespace Dsh.Checkpoints;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CheckpointPoint))]
internal sealed partial class DshCheckpointsJsonContext : JsonSerializerContext
{
}

