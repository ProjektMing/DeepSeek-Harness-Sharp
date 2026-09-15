using System.Text.Json.Serialization;

namespace Dsh.Compaction;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CompactionStartPayload))]
[JsonSerializable(typeof(CompactionEndPayload))]
[JsonSerializable(typeof(CompactionSummaryPayload))]
[JsonSerializable(typeof(CompactionPrunePayload))]
internal sealed partial class DshCompactionJsonContext : JsonSerializerContext
{
}

