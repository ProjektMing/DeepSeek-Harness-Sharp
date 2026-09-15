using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dsh.Telemetry;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TelemetryService.TelemetryRecord))]
[JsonSerializable(typeof(TelemetryAgentSession))]
[JsonSerializable(typeof(TelemetryAgentDisposed))]
[JsonSerializable(typeof(TelemetryAgentFailure))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DshTelemetryJsonContext : JsonSerializerContext
{
}

