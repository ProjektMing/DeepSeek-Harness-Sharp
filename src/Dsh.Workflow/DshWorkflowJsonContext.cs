using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Workflow;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolWorkflowRunStartPayload))]
[JsonSerializable(typeof(ToolWorkflowRunEndPayload))]
[JsonSerializable(typeof(ToolWorkflowAgentStartPayload))]
[JsonSerializable(typeof(ToolWorkflowAgentEndPayload))]
internal sealed partial class DshWorkflowJsonContext : JsonSerializerContext
{
}

internal static class DshWorkflowJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshWorkflowJsonContext.Default);
}
