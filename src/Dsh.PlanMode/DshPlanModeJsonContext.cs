using System.Text.Json.Serialization;

namespace Dsh.PlanMode;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PlanModePayload))]
internal sealed partial class DshPlanModeJsonContext : JsonSerializerContext
{
}

