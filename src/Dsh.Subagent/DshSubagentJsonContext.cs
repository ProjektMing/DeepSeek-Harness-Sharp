using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Subagent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SubagentDescriptorPayload))]
[JsonSerializable(typeof(AssistantOutputFold))]
internal sealed partial class DshSubagentJsonContext : JsonSerializerContext
{
}

internal static class DshSubagentJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshSubagentJsonContext.Default);
}
