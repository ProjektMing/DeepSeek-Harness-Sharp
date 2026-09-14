using System.Text.Json;
using System.Text.Json.Nodes;
using Dsh.Core;
using Dsh.Llm;
using Dsh.Runtime;

namespace Dsh.Plugins.Native.Host;

/** 把原生 ABI 插件桥接成托管插件定义:激活时注册工具,注销时移除注册并停用插件。 */
public static class NativePluginBridge
{
    private static readonly JsonObject ResultSchema = Parse("""
        { "type": "object", "additionalProperties": true, "properties": { "text": { "type": "string" } } }
        """);

    public static PluginDefinition CreateDefinition(INativePlugin plugin)
        => PluginDefinition.From((ctx, _) =>
        {
            var tools = ctx.Get<ToolRuntime>(ToolRuntime.ServiceName)
                ?? throw new InvalidOperationException($"native plugin \"{plugin.Package}\" requires the tools service");
            var logger = ctx.LoggerFor("native-plugin");
            var registrations = new List<IDisposable>();
            try
            {
                plugin.Activate(new Host(plugin, tools, registrations, logger));
            }
            catch
            {
                foreach (var registration in registrations)
                    registration.Dispose();
                throw;
            }
            return new Deactivation(plugin, registrations);
        }, name: plugin.Package);

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static string Render(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? ""
                : value.GetRawText();

    private sealed class Host(
        INativePlugin plugin,
        ToolRuntime tools,
        List<IDisposable> registrations,
        Logger logger) : INativePluginHost
    {
        public void Log(int level, string message)
        {
            var line = $"native plugin \"{plugin.Package}\": {message}";
            switch (level)
            {
                case DshNativePluginAbi.LogError:
                    logger.Error("%s", line);
                    break;
                case DshNativePluginAbi.LogWarn:
                    logger.Warn("%s", line);
                    break;
                case DshNativePluginAbi.LogDebug:
                    logger.Debug("%s", line);
                    break;
                default:
                    logger.Info("%s", line);
                    break;
            }
        }

        public void RegisterTool(string name, string description, string parametersJson, Func<string, string?> invoke)
        {
            JsonObject parameters;
            try
            {
                parameters = JsonNode.Parse(parametersJson)?.AsObject() ?? [];
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    $"native plugin \"{plugin.Package}\" tool \"{name}\" has an invalid parameters schema: {error.Message}");
            }
            registrations.Add(tools.Register(new ToolDefinition
            {
                Name = name,
                Description = description,
                Parameters = parameters,
                Output = new ToolOutputDefinition(ResultSchema, (_, value) => [new TextBlock(Render(value))]),
                Execute = (args, _) =>
                {
                    try
                    {
                        var result = invoke(args.GetRawText());
                        if (result is null)
                            throw new InvalidOperationException($"native plugin \"{plugin.Package}\" tool \"{name}\" failed");
                        return Task.FromResult<object?>(JsonDocument.Parse(result).RootElement.Clone());
                    }
                    catch (Exception error)
                    {
                        logger.Error("%s", $"native plugin \"{plugin.Package}\" tool \"{name}\" invoke failed: {error}");
                        throw;
                    }
                },
            }));
        }
    }

    private sealed class Deactivation(INativePlugin plugin, List<IDisposable> registrations) : IDisposable
    {
        public void Dispose()
        {
            foreach (var registration in registrations)
                registration.Dispose();
            plugin.Deactivate();
        }
    }
}
