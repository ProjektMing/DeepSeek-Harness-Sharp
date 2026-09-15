using System.Runtime.InteropServices;
using Dsh.Plugins.Generated;
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

/** 原生共享库插件:NativeLibrary.Load + 生成胶水的握手、句柄与两段式调用。 */
public sealed unsafe class NativePluginLibrary : INativePlugin
{
    private static readonly Sink Dispatcher = new();

    private readonly nint _library;
    private readonly DshPluginApiBinding _binding;
    private INativePluginHost? _host;
    private GCHandle _self;

    static NativePluginLibrary() => DshNativeHostApi.Calls = Dispatcher;

    private NativePluginLibrary(nint library, DshPluginApi api)
    {
        _library = library;
        _binding = new DshPluginApiBinding(api);
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
            if (!DshPluginApiBinding.TryHandshake(library, out var api))
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
        if (_binding.Activate(GCHandle.ToIntPtr(_self)) != DshNativePluginAbi.Ok)
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
        _binding.Deactivate(GCHandle.ToIntPtr(_self));
        _self.Free();
        _host = null;
    }

    public void Dispose()
    {
        Deactivate();
        NativeLibrary.Free(_library);
    }

    private string? InvokeTool(int handle, string inputJson)
        => _binding.InvokeTool(GCHandle.ToIntPtr(_self), handle, inputJson);

    private static NativePluginLibrary? FromHandle(nint context)
        => context == 0 ? null : (NativePluginLibrary?)GCHandle.FromIntPtr(context).Target;

    /** 生成蹦床的落地:从 ABI 上下文句柄找回插件实例,再走托管回调。 */
    private sealed class Sink : IDshNativeHostCalls
    {
        public void Log(nint context, int level, string message)
            => FromHandle(context)?._host?.Log(level, message);

        public int RegisterTool(nint context, string name, string description, string parametersJson, int handle)
        {
            var plugin = FromHandle(context);
            if (plugin?._host is null)
                return DshNativePluginAbi.Error;
            plugin._host.RegisterTool(name, description, parametersJson, input => plugin.InvokeTool(handle, input));
            return DshNativePluginAbi.Ok;
        }
    }
}
