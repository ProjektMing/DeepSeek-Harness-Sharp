using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Compaction;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CompactionStartPayload))]
[JsonSerializable(typeof(CompactionEndPayload))]
[JsonSerializable(typeof(CompactionSummaryPayload))]
[JsonSerializable(typeof(CompactionPrunePayload))]
internal sealed partial class DshCompactionJsonContext : JsonSerializerContext
{
}

internal static class DshCompactionJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshCompactionJsonContext.Default);
}
