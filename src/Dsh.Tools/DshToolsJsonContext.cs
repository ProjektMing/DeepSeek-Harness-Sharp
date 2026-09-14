using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Tools;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TodoWritePayload))]
[JsonSerializable(typeof(TodoItem))]
[JsonSerializable(typeof(BashStreamOutput))]
[JsonSerializable(typeof(BashRunValue))]
[JsonSerializable(typeof(ReadResultValue))]
[JsonSerializable(typeof(ReadFileLine))]
[JsonSerializable(typeof(SubprocessOutputRead))]
[JsonSerializable(typeof(WriteResultValue))]
[JsonSerializable(typeof(EditResultValue))]
[JsonSerializable(typeof(GlobResultValue))]
[JsonSerializable(typeof(GrepMatchValue))]
[JsonSerializable(typeof(GrepResultValue))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DshToolsJsonContext : JsonSerializerContext
{
}

internal static class DshToolsJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshToolsJsonContext.Default);
}
