using System.Text.Json.Serialization;

namespace Dsh.Interaction;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApprovalAskedPayload))]
[JsonSerializable(typeof(ApprovalDecidedPayload))]
[JsonSerializable(typeof(ApprovalPolicyPayload))]
[JsonSerializable(typeof(CommandRunPayload))]
[JsonSerializable(typeof(CommandDonePayload))]
internal sealed partial class DshInteractionJsonContext : JsonSerializerContext
{
}

