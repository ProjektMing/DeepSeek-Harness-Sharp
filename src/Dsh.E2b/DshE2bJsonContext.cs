using System.Text.Json.Serialization;

namespace Dsh.E2b;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(E2bCreateSandboxRequest))]
[JsonSerializable(typeof(E2bExecuteCommandRequest))]
[JsonSerializable(typeof(E2bCommandResult))]
[JsonSerializable(typeof(E2bRunResult))]
internal sealed partial class DshE2bJsonContext : JsonSerializerContext
{
}

