using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dsh.Llm;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Web;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebSearchResultValue))]
[JsonSerializable(typeof(WebSearchSource))]
[JsonSerializable(typeof(WebFetchMeta))]
[JsonSerializable(typeof(WebFetchResult))]
[JsonSerializable(typeof(WebFetchBody))]
[JsonSerializable(typeof(WebFetchRequest))]
internal sealed partial class DshWebJsonContext : JsonSerializerContext
{
}

internal static class DshWebJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshWebJsonContext.Default);
}
