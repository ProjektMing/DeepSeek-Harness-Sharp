using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Dsh.Plugins.Native;

namespace Dsh.Plugins;

/** 原生插件在托管侧的能力回调:宿主在插件 Activate 时提供。 */
public interface INativePluginHost
{
    void Log(int level, string message);

    void RegisterTool(string name, string description, string parametersJson, Func<string, string?> invoke);
}

/** 原生插件句柄:包名、生命周期与工具调用。 */
public interface INativePlugin : IDisposable
{
    string Package { get; }

    void Activate(INativePluginHost host);

    void Deactivate();
}

/** 原生共享库插件:NativeLibrary.Load + dsh_plugin_entry 握手。 */
public sealed unsafe class NativePluginLibrary : INativePlugin
{
    private static readonly delegate* unmanaged[Cdecl]<void*, int, byte*, void> LogCallback = &LogTrampoline;
    private static readonly delegate* unmanaged[Cdecl]<void*, byte*, byte*, byte*, int, int> RegisterToolCallback = &RegisterToolTrampoline;
    private static readonly DshHostApi* HostApi;

    private readonly nint _library;
    private readonly DshPluginApi _api;
    private INativePluginHost? _host;
    private GCHandle _self;

    static NativePluginLibrary()
    {
        HostApi = (DshHostApi*)Marshal.AllocHGlobal(sizeof(DshHostApi));
        HostApi->AbiVersion = DshNativePluginAbi.Version;
        HostApi->Context = null;
        HostApi->Log = LogCallback;
        HostApi->RegisterTool = RegisterToolCallback;
    }

    private NativePluginLibrary(nint library, DshPluginApi api)
    {
        _library = library;
        _api = api;
        Package = NativeUtf8.Read(api.Package);
    }

    public string Package { get; }

    /** 文件不是原生插件(缺导出)时返回 null。 */
    public static NativePluginLibrary? TryLoad(string path)
    {
        if (!NativeLibrary.TryLoad(path, out var library))
            return null;
        try
        {
            if (!NativeLibrary.TryGetExport(library, DshNativePluginAbi.PackageExport, out _)
                || !NativeLibrary.TryGetExport(library, DshNativePluginAbi.EntryPoint, out var entry))
            {
                NativeLibrary.Free(library);
                return null;
            }
            var api = new DshPluginApi { AbiVersion = 0 };
            var enter = (delegate* unmanaged[Cdecl]<DshHostApi*, DshPluginApi*, int>)entry;
            if (enter(HostApi, &api) != DshNativePluginAbi.Ok
                || api.AbiVersion != DshNativePluginAbi.Version
                || api.Activate is null
                || api.Deactivate is null
                || api.InvokeTool is null)
            {
                NativeLibrary.Free(library);
                return null;
            }
            return new NativePluginLibrary(library, api);
        }
        catch (Exception)
        {
            NativeLibrary.Free(library);
            return null;
        }
    }

    public void Activate(INativePluginHost host)
    {
        _host = host;
        _self = GCHandle.Alloc(this);
        if (_api.Activate(GCHandle.ToIntPtr(_self).ToPointer()) != DshNativePluginAbi.Ok)
        {
            _self.Free();
            _host = null;
            throw new InvalidOperationException($"native plugin \"{Package}\" refused activation");
        }
    }

    public void Deactivate()
    {
        if (_host is null)
            return;
        _api.Deactivate(GCHandle.ToIntPtr(_self).ToPointer());
        _self.Free();
        _host = null;
    }

    public void Dispose()
    {
        Deactivate();
        NativeLibrary.Free(_library);
    }

    private string? InvokeTool(int handle, string inputJson)
    {
        var bytes = Encoding.UTF8.GetBytes(inputJson);
        fixed (byte* pinned = bytes)
        {
            var context = GCHandle.ToIntPtr(_self).ToPointer();
            var needed = _api.InvokeTool(context, handle, pinned, null, 0);
            if (needed < 0)
                return null;
            var buffer = new byte[needed + 1];
            fixed (byte* target = buffer)
            {
                var written = _api.InvokeTool(context, handle, pinned, target, needed + 1);
                return written < 0 ? null : Encoding.UTF8.GetString(buffer, 0, written);
            }
        }
    }

    private static NativePluginLibrary? FromHandle(void* context)
        => context is null ? null : (NativePluginLibrary?)GCHandle.FromIntPtr((nint)context).Target;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void LogTrampoline(void* context, int level, byte* message)
        => FromHandle(context)?._host?.Log(level, NativeUtf8.Read(message));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RegisterToolTrampoline(void* context, byte* name, byte* description, byte* parameters, int handle)
    {
        var plugin = FromHandle(context);
        if (plugin is null)
            return DshNativePluginAbi.Error;
        plugin._host?.RegisterTool(
            NativeUtf8.Read(name),
            NativeUtf8.Read(description),
            NativeUtf8.Read(parameters),
            input => plugin.InvokeTool(handle, input));
        return DshNativePluginAbi.Ok;
    }
}
