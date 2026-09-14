using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Telemetry;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TelemetryService.TelemetryRecord))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DshTelemetryJsonContext : JsonSerializerContext
{
}

internal static class DshTelemetryJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshTelemetryJsonContext.Default);
}
