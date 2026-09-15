using System.Reflection;
using System.Runtime.CompilerServices;

namespace Dsh.Plugins;

/** 一次目录扫描的结果:装载的托管插件、持有的原生句柄与逐文件跳过原因。 */
public sealed record PluginScanResult(
    IReadOnlyList<ManagedPlugin> Managed,
    IReadOnlyList<INativePlugin> Native,
    IReadOnlyList<PluginSkip> Skipped);

/** 一次托管装载的结果:登记成功的包、逐项跳过原因与持有的加载上下文。 */
public sealed record PluginLoadResult(
    IReadOnlyList<string> Packages,
    IReadOnlyList<PluginSkip> Skipped,
    PluginLoadContext? Context);

/** 托管插件的一次装载:包名与持有它的可回收加载上下文。 */
public sealed record ManagedPlugin(string Package, PluginLoadContext Context);

/** 被跳过的插件制品与原因,供宿主逐文件 WARN。 */
public sealed record PluginSkip(string File, string Reason);

public sealed class PluginHost
{
    private const string ManifestTypeName = "Dsh.Plugins.Generated.DshPluginManifest";
    private const string RegistrationsMethodName = "Registrations";

    public PluginCatalog Catalog { get; } = new();

    /** 镜像内插件:由生成目录在模块初始化时自注册。 */
    public void RegisterCompiledIn()
    {
        foreach (var registration in DshPluginCatalogRegistry.Snapshot())
            Catalog.Register(Descriptor(registration, PluginForm.CompiledIn), registration.Create);
    }

    /** 从文件装载托管插件:按类型名读取插件自带的生成清单,不反射遍历插件类型。 */
    public PluginLoadResult TryLoad(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var context = new PluginLoadContext(fullPath);
        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyPath(fullPath);
        }
        catch
        {
            context.Unload();
            throw;
        }
        var registrations = ReadManifest(assembly, out var failure);
        if (failure is not null)
        {
            context.Unload();
            return new PluginLoadResult([], [new PluginSkip(fullPath, failure)], null);
        }
        if (registrations.Count == 0)
        {
            context.Unload();
            return new PluginLoadResult([], [], null);
        }
        var packages = new List<string>();
        var skipped = new List<PluginSkip>();
        foreach (var registration in registrations)
        {
            if (registration.DescriptorVersion != PluginDescriptor.CurrentVersion)
            {
                skipped.Add(new PluginSkip(fullPath,
                    $"插件描述符版本 {registration.DescriptorVersion} 与宿主 {PluginDescriptor.CurrentVersion} 不兼容"));
                continue;
            }
            Catalog.Register(Descriptor(registration, PluginForm.ManagedAssembly), registration.Create);
            packages.Add(registration.Package);
        }
        if (packages.Count == 0)
        {
            context.Unload();
            return new PluginLoadResult(packages, skipped, null);
        }
        return new PluginLoadResult(packages, skipped, context);
    }

    /** 统一扫描插件目录:托管程序集装入可回收上下文,原生共享库经 nativeBridge 转成插件登记。 */
    public PluginScanResult Scan(string directory, Func<INativePlugin, IDshPlugin>? nativeBridge)
    {
        var packages = new List<string>();
        var managed = new List<ManagedPlugin>();
        var nativePlugins = new List<INativePlugin>();
        var skipped = new List<PluginSkip>();
        if (!Directory.Exists(directory))
            return new PluginScanResult(managed, nativePlugins, skipped);
        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".dll" or ".so" or ".dylib"))
                continue;
            if (extension == ".dll")
            {
                if (!RuntimeFeature.IsDynamicCodeSupported)
                {
                    if (nativeBridge is not null && TryLoadNative(file, nativeBridge, packages, nativePlugins))
                        continue;
                    skipped.Add(new PluginSkip(file,
                        "NativeAOT 构建不能在运行期装载托管程序集;请把它编译进镜像,或改用原生插件"));
                    continue;
                }
                TryLoadManaged(file, packages, managed, skipped, out var handled);
                if (handled)
                    continue;
            }
            if (nativeBridge is not null)
                _ = TryLoadNative(file, nativeBridge, packages, nativePlugins);
        }
        return new PluginScanResult(managed, nativePlugins, skipped);
    }

    private void TryLoadManaged(
        string file,
        List<string> packages,
        List<ManagedPlugin> managed,
        List<PluginSkip> skipped,
        out bool handled)
    {
        PluginLoadResult result;
        try
        {
            result = TryLoad(file);
        }
        catch (BadImageFormatException)
        {
            handled = false;   // 原生共享库(Windows 下同样是 .dll)交给原生装载
            return;
        }
        catch (Exception error)
        {
            skipped.Add(new PluginSkip(file, $"托管插件装载失败:{error.Message}"));
            handled = true;
            return;
        }
        skipped.AddRange(result.Skipped);
        handled = result.Packages.Count > 0 || result.Skipped.Count > 0;
        foreach (var package in result.Packages)
        {
            managed.Add(new ManagedPlugin(package, result.Context!));
            packages.Add(package);
        }
    }

    private bool TryLoadNative(
        string file,
        Func<INativePlugin, IDshPlugin> bridge,
        List<string> packages,
        List<INativePlugin> nativePlugins)
    {
        var plugin = NativePluginLibrary.TryLoad(file);
        if (plugin is null)
            return false;
        if (Catalog.PackageNames.Contains(plugin.Package, StringComparer.Ordinal))
        {
            plugin.Dispose();
            return true;
        }
        Catalog.Register(PluginDescriptor.For(plugin.Package, PluginForm.NativeLibrary), () => bridge(plugin));
        nativePlugins.Add(plugin);
        packages.Add(plugin.Package);
        return true;
    }

    private static PluginDescriptor Descriptor(DshPluginRegistration registration, PluginForm form)
        => PluginDescriptor.For(registration.Package, form) with
        {
            Entry = registration.Entry,
            Version = registration.DescriptorVersion,
        };

    private static IReadOnlyList<DshPluginRegistration> ReadManifest(Assembly assembly, out string? failure)
    {
        failure = null;
        var method = assembly.GetType(ManifestTypeName)?.GetMethod(RegistrationsMethodName, Type.EmptyTypes);
        if (method is null)
            return [];
        try
        {
            return method.Invoke(null, null) as IReadOnlyList<DshPluginRegistration> ?? [];
        }
        catch (Exception error)
        {
            failure = $"插件清单读取失败:{error.GetBaseException().Message}";
            return [];
        }
    }
}
