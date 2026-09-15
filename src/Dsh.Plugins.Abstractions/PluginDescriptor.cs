using System.Runtime.CompilerServices;

namespace Dsh.Plugins;

/** 插件制品形态:决定装载后端。 */
public enum PluginForm
{
    /** 与主机一起编译、由生成目录静态引用的插件。 */
    CompiledIn,

    /** 运行期从托管 dll 装载的插件(需要可回收 ALC 的主机)。 */
    ManagedAssembly,

    /** 由 ilc 编译、经原生 ABI 装载的共享库插件。 */
    NativeLibrary,
}

/** 插件在宿主中的运行期能力,按"形态 × 主机发布档"推导。 */
[Flags]
public enum PluginCapabilities
{
    None = 0,

    /** 停用时能回收制品本身(可回收 ALC)。 */
    Unload = 1 << 0,
}

/** 插件的运行期描述符:形态与能力是宿主行为的唯一判据,不在别处反推。 */
public sealed record PluginDescriptor(
    string Package,
    PluginForm Form,
    PluginCapabilities Capabilities)
{
    /** 生成目录输出的描述符版本:宿主据此拒绝不兼容的插件制品。 */
    public const int CurrentVersion = 1;

    /** 入口名(如 tui/gui);非入口插件为 null。 */
    public string? Entry { get; init; }

    public int Version { get; init; } = CurrentVersion;

    public static PluginDescriptor For(string package, PluginForm form)
        => new(package, form, form switch
        {
            PluginForm.ManagedAssembly when RuntimeFeature.IsDynamicCodeSupported
                => PluginCapabilities.Unload,
            _ => PluginCapabilities.None,
        });
}
