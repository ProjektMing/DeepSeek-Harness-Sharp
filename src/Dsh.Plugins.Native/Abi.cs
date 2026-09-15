using System.Runtime.InteropServices;
using System.Text;

namespace Dsh.Plugins.Native;

/** 原生插件 ABI v1:宿主与插件用 C 调用约定交换函数表;字符串统一为 UTF-8,无终止符,长度由协议决定。 */
public static class DshNativePluginAbi
{
    public const int Version = 1;
    public const string EntryPoint = "dsh_plugin_entry";
    public const string PackageExport = "dsh_plugin_package";

    public const int Ok = 0;
    public const int Error = -1;
    public const int LogDebug = 0;
    public const int LogInfo = 1;
    public const int LogWarn = 2;
    public const int LogError = 3;
}

/** 标注函数表字段在协议中的角色:两侧胶水由生成器按角色产出,不靠字段名约定。 */
[AttributeUsage(AttributeTargets.Field)]
public sealed class DshAbiRoleAttribute(string role) : Attribute
{
    public string Role { get; } = role;
}

/** ABI v1 的字段角色名。 */
public static class DshAbiRoles
{
    public const string Package = "package";
    public const string Activate = "activate";
    public const string Deactivate = "deactivate";
    public const string InvokeTool = "invoke_tool";
    public const string Log = "log";
    public const string RegisterTool = "register_tool";
}

/** 宿主提供给插件的函数表;Context 是不透明句柄,插件只负责原样回传。 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DshHostApi
{
    public int AbiVersion;
    public void* Context;
    [DshAbiRole(DshAbiRoles.Log)]
    public delegate* unmanaged[Cdecl]<void*, int, byte*, void> Log;
    [DshAbiRole(DshAbiRoles.RegisterTool)]
    public delegate* unmanaged[Cdecl]<void*, byte*, byte*, byte*, int, int> RegisterTool;
}

/** 插件提供给宿主的函数表;Package 指向插件内静态 UTF-8 缓冲区,插件卸载前一直有效。 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DshPluginApi
{
    public int AbiVersion;
    [DshAbiRole(DshAbiRoles.Package)]
    public byte* Package;
    [DshAbiRole(DshAbiRoles.Activate)]
    public delegate* unmanaged[Cdecl]<void*, int> Activate;
    [DshAbiRole(DshAbiRoles.Deactivate)]
    public delegate* unmanaged[Cdecl]<void*, void> Deactivate;
    [DshAbiRole(DshAbiRoles.InvokeTool)]
    public delegate* unmanaged[Cdecl]<void*, int, byte*, byte*, int, int> InvokeTool;
}

/** 插件侧工具注册与调度的便利包装:插件在 Activate 里注册工具,由 Dispatch 派发调用。 */
public sealed unsafe class NativePluginContext(DshHostApi* host, void* context)
{
    private readonly Dictionary<int, Func<string, string?>> _handlers = [];
    private int _nextHandle;

    public void Log(int level, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        fixed (byte* pointer = bytes)
            host->Log(context, level, pointer);
    }

    public void RegisterTool(string name, string description, string parametersJson, Func<string, string?> invoke)
    {
        var handle = Interlocked.Increment(ref _nextHandle);
        _handlers[handle] = invoke;
        fixed (byte* namePointer = Encoding.UTF8.GetBytes(name))
        fixed (byte* descriptionPointer = Encoding.UTF8.GetBytes(description))
        fixed (byte* parametersPointer = Encoding.UTF8.GetBytes(parametersJson))
            host->RegisterTool(context, namePointer, descriptionPointer, parametersPointer, handle);
    }

    public string? Dispatch(int handle, string inputJson)
        => _handlers.TryGetValue(handle, out var invoke) ? invoke(inputJson) : null;
}

/** 插件侧 UTF-8 互操作助手。 */
public static unsafe class NativeUtf8
{
    public static string Read(byte* pointer)
    {
        if (pointer is null)
            return "";
        var length = 0;
        while (pointer[length] != 0)
            length++;
        return Encoding.UTF8.GetString(pointer, length);
    }

    /** 常驻字符串(如包名):分配一次并固定,供导出函数返回稳定指针。 */
    public static byte* Persistent(string value)
        => (byte*)Marshal.StringToCoTaskMemUTF8(value);

    /** 两段式缓冲协议:output 为 null 或容量不足时只返回所需字节数,否则写出内容并返回写入长度。 */
    public static int Write(string? value, byte* output, int capacity)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        if (output is null || capacity < bytes.Length)
            return bytes.Length;
        for (var index = 0; index < bytes.Length; index++)
            output[index] = bytes[index];
        return bytes.Length;
    }
}
