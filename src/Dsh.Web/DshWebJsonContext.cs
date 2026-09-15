using System.Text.Json.Serialization;

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

