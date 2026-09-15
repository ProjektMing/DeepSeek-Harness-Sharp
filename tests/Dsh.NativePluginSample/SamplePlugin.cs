using System.Text.Json;
using System.Text.Json.Nodes;
using Dsh.Plugins.Native;

namespace Dsh.NativePluginSample;

/** 声明式原生插件样例:只声明包名与工具;
 *  导出握手、句柄表、两段式缓冲协议与异常拦截由生成器产出(见 DshNativePluginExports)。 */
public sealed class SamplePlugin : IDshNativePlugin
{
    private const string EchoSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["message"],
          "properties": { "message": { "type": "string", "description": "Text to echo" } }
        }
        """;

    private static readonly NativePluginTool[] RegisteredTools =
    [
        new("native_echo", "Echo the given message back (native ABI sample).", EchoSchema, Echo),
    ];

    public string Package => "sample.native-echo";

    public IReadOnlyList<NativePluginTool> Tools => RegisteredTools;

    private static string? Echo(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        var message = document.RootElement.TryGetProperty("message", out var value) ? value.GetString() ?? "" : "";
        DshNativePluginRuntime.Log(DshNativePluginAbi.LogInfo, $"echo invoked: {message}");
        return new JsonObject { ["text"] = $"echo:{message}" }.ToJsonString();
    }
}
