using System.Text.Json.Serialization;

namespace Dsh.Jobs;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PublicJobSnapshot))]
[JsonSerializable(typeof(List<PublicJobSnapshot>))]
[JsonSerializable(typeof(JobOutputResult))]
[JsonSerializable(typeof(JobKillResult))]
internal sealed partial class DshJobsJsonContext : JsonSerializerContext
{
}
