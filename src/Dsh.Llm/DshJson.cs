using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dsh.Llm;

/** 统一的 JSON 配置:源生成上下文优先;JIT 下额外挂反射解析器,保证未注册类型与第三方托管插件仍可用。 */
public static class DshJson
{
    private static readonly List<IJsonTypeInfoResolver> Resolvers = [];
    private static readonly Lock Gate = new();
    private static JsonSerializerOptions? _options;

    /** 注册源生成上下文;可回收 ALC 中的插件上下文不注册,避免把插件加载上下文钉死导致无法卸载。 */
    public static void RegisterResolver(JsonSerializerContext context)
    {
        if (AssemblyLoadContext.GetLoadContext(context.GetType().Assembly) != AssemblyLoadContext.Default)
            return;
        lock (Gate)
        {
            Resolvers.Add(context);
            _options = null;
        }
    }

    public static JsonSerializerOptions Options
    {
        get
        {
            var options = _options;
            if (options is not null)
                return options;
            lock (Gate)
                return _options ??= Create();
        }
    }

    private static JsonSerializerOptions Create()
    {
        var resolvers = new List<IJsonTypeInfoResolver>(Resolvers);
        if (RuntimeFeature.IsDynamicCodeSupported)
            resolvers.Add(new DefaultJsonTypeInfoResolver());
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        if (resolvers.Count == 1)
            options.TypeInfoResolver = resolvers[0];
        else if (resolvers.Count > 1)
            options.TypeInfoResolver = JsonTypeInfoResolver.Combine([.. resolvers]);
        return options;
    }

    public static JsonTypeInfo Info(Type type) => Options.GetTypeInfo(type);

    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Info<T>());

    public static void Serialize<T>(Utf8JsonWriter writer, T value) => JsonSerializer.Serialize(writer, value, Info<T>());

    public static T? Deserialize<T>(JsonElement element) => element.Deserialize(Info<T>());

    public static T? Deserialize<T>(JsonNode? node) => node is null ? default : JsonSerializer.Deserialize(node, Info<T>());

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, Info<T>());

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Info<T>());

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Info<T>());

    /** 运行时类型已知(如工具返回值)时序列化:类型不在注册表内会抛出明确错误而不是静默降级。 */
    public static JsonElement ToElementRuntime(object? value) => value switch
    {
        null => JsonDocument.Parse("{}").RootElement,
        JsonElement element => element.Clone(),
        JsonNode node => JsonDocument.Parse(node.ToJsonString()).RootElement,
        _ => JsonSerializer.SerializeToElement(value, Info(value.GetType())),
    };

    public static JsonNode? ToNodeRuntime(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        _ => JsonSerializer.SerializeToNode(value, Info(value.GetType())),
    };
}
