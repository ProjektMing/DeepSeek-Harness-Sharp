using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dsh.Plugins.Native;

/** 示例原生插件:向宿主注册 native_echo 工具,返回 echo:<message>。 */
public static unsafe class SamplePlugin
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["message"],
          "properties": { "message": { "type": "string", "description": "Text to echo" } }
        }
        """;

    private static DshHostApi* _host;
    private static NativePluginContext? _context;
    private static byte* _package;

    static SamplePlugin() => _package = NativeUtf8.Persistent("sample.native-echo");

    [UnmanagedCallersOnly(EntryPoint = "dsh_plugin_package", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* Package() => _package;

    [UnmanagedCallersOnly(EntryPoint = "dsh_plugin_entry", CallConvs = [typeof(CallConvCdecl)])]
    public static int Entry(DshHostApi* host, DshPluginApi* api)
    {
        if (host is null || api is null || host->AbiVersion != DshNativePluginAbi.Version)
            return DshNativePluginAbi.Error;
        _host = host;
        api->AbiVersion = DshNativePluginAbi.Version;
        api->Package = _package;
        api->Activate = &Activate;
        api->Deactivate = &Deactivate;
        api->InvokeTool = &InvokeTool;
        return DshNativePluginAbi.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Activate(void* context)
    {
        _context = new NativePluginContext(_host, context);
        _context.Log(DshNativePluginAbi.LogInfo, "sample plugin activated");
        _context.RegisterTool("native_echo", "Echo the given message back (native ABI sample).", Schema, Echo);
        return DshNativePluginAbi.Ok;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Deactivate(void* context)
    {
        _context?.Log(DshNativePluginAbi.LogInfo, "sample plugin deactivated");
        _context = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int InvokeTool(void* context, int handle, byte* input, byte* output, int capacity)
    {
        var result = _context?.Dispatch(handle, NativeUtf8.Read(input)) ?? "";
        var bytes = Encoding.UTF8.GetBytes(result);
        if (output is null || capacity < bytes.Length)
            return bytes.Length;
        for (var index = 0; index < bytes.Length; index++)
            output[index] = bytes[index];
        return bytes.Length;
    }

    private static string? Echo(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        var message = document.RootElement.TryGetProperty("message", out var value) ? value.GetString() ?? "" : "";
        _context?.Log(DshNativePluginAbi.LogInfo, $"echo invoked: {message}");
        return new JsonObject { ["text"] = $"echo:{message}" }.ToJsonString();
    }
}
