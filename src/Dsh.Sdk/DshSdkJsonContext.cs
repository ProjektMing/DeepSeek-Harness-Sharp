using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dsh.Sdk;

/** SDK 线格式(JSON-RPC 帧是开放字典,另有 SDK 记录类型):AOT 下必须能由源生成上下文提供元数据。 */
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(List<object?>))]
[JsonSerializable(typeof(IReadOnlyList<object?>))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(SessionPromptParams))]
[JsonSerializable(typeof(SessionPromptResult))]
[JsonSerializable(typeof(SessionEventNotification))]
[JsonSerializable(typeof(SessionStatusNotification))]
[JsonSerializable(typeof(SubagentStartedNotification))]
[JsonSerializable(typeof(SubagentFinishedNotification))]
[JsonSerializable(typeof(ServiceCallParams))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DshSdkJsonContext : JsonSerializerContext;
