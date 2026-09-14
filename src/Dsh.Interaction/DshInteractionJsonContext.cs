using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

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

internal static class DshInteractionJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshInteractionJsonContext.Default);
}
