using System.Text.Json.Serialization;

namespace Dsh.Goal;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GoalChangePayload))]
internal sealed partial class DshGoalJsonContext : JsonSerializerContext
{
}

