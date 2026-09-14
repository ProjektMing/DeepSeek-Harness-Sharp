using System.Runtime.CompilerServices;
using Dsh.Plugins;
using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Boot;

public sealed class HarnessPluginManager(
    PluginHost host,
    Composition composition,
    HarnessHome home,
    HarnessSettings settings) : IPluginManager, IDisposable
{
    private readonly Dictionary<string, PluginLoadContext> _loadedContexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _leaked = new(StringComparer.Ordinal);

    public IReadOnlyList<string> PackageNames => host.Catalog.PackageNames
        .Distinct()
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    public bool SupportsDynamicLoad => RuntimeFeature.IsDynamicCodeSupported;

    public string Describe(string package)
    {
        var activation = composition.Find(package);
        var state = activation?.State switch
        {
            ActivationState.Active => "active",
            ActivationState.Activating => "activating",
            ActivationState.Deactivating => "deactivating (unloading)",
            ActivationState.Failed => $"failed: {activation.Error}",
            ActivationState.Disposed => "removed",
            ActivationState.Pending => "pending (dependencies missing)",
            null when settings.Plugins.TryGetValue(package, out var setting) && !setting.Enabled => "disabled",
            _ => "available",
        };
        return _leaked.Contains(package) ? $"{state}, leaked" : state;
    }

    public async Task<string> AddAsync(string packageOrPath)
    {
        var spec = packageOrPath.Trim();
        if (spec.Length == 0)
            return "usage: /plugins add <package|path>";
        if (spec.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || File.Exists(spec))
            return await AddFromPathAsync(spec);
        if (host.Catalog.PackageNames.Contains(spec, StringComparer.Ordinal))
            return await ActivateAsync(spec);
        return $"plugin package not found: {spec}";
    }

    public async Task<string> RemoveAsync(string package, bool force = false)
    {
        var trimmed = package.Trim();
        if (trimmed.Length == 0)
            return "usage: /plugins remove <package>";
        if (composition.Root.Scheduler.Find(trimmed) is null)
            return $"plugin {trimmed} is not active";
        var (weak, leaked) = await UnloadDynamicAsync(trimmed, force);   // 内置插件 weak 为 null
        Persist(trimmed, weak is null ? new PluginSetting { Enabled = false } : null);
        if (leaked)
            _leaked.Add(trimmed);
        if (weak is null)
            return Removed(trimmed, leaked);
        _ = VerifyCollectionAsync(trimmed, weak);
        return Removed(trimmed, leaked) + "(加载上下文回收校验异步进行中;若泄漏会记录日志并在 /plugins list 标记 leaked)";
    }

    public async Task<string> DisableAsync(string package)
    {
        var trimmed = package.Trim();
        if (trimmed.Length == 0)
            return "usage: /plugins disable <package>";
        if (!IsKnown(trimmed))
            return $"plugin package not found: {trimmed}";
        if (composition.Root.Scheduler.Find(trimmed) is { } activation)
        {
            await composition.Root.Scheduler.UnloadAsync(trimmed);
            if (activation.State != ActivationState.Disposed)
            {
                _leaked.Add(trimmed);
                return $"plugin {trimmed} disabled with timeout; effects may still be running (leaked)";
            }
        }
        Persist(trimmed, new PluginSetting { Enabled = false });
        return $"plugin {trimmed} disabled (removed from settings.yaml)";
    }

    public async Task<string> EnableAsync(string package)
    {
        var trimmed = package.Trim();
        if (trimmed.Length == 0)
            return "usage: /plugins enable <package>";
        if (!IsKnown(trimmed))
            return $"plugin package not found: {trimmed}";
        Persist(trimmed, new PluginSetting { Enabled = true });
        if (composition.Root.Scheduler.Find(trimmed) is not null)
            return $"plugin {trimmed} enabled (already active)";
        return await ActivateAsync(trimmed);
    }

    private async Task<string> AddFromPathAsync(string spec)
    {
        var path = Path.GetFullPath(spec);
        if (!File.Exists(path))
            return $"plugin file not found: {path}";
        try
        {
            var context = new PluginLoadContext(path);
            var assembly = context.LoadFromAssemblyPath(path);
            var added = host.Catalog.RegisterAssembly(assembly, context, replace: true);
            if (added.Count == 0)
            {
                context.Unload();
                return $"assembly has no DSH plugin: {path}";
            }
            var messages = new List<string>();
            foreach (var package in added)
            {
                _loadedContexts[package] = context;
                messages.Add(await ActivateAsync(package));
            }
            return string.Join('\n', messages);
        }
        catch (PlatformNotSupportedException)
        {
            return "dynamic plugin loading is not supported in this build (NativeAOT); list the plugin in settings.yaml and restart";
        }
    }

    private async Task<string> ActivateAsync(string package)
    {
        if (composition.Find(package) is { } existing)
            return $"plugin {package} is already active ({existing.State})";
        if (!host.Catalog.TryCreateDefinition(package, out var definition))
            return $"plugin package not found: {package}";
        var activation = await composition.AddAsync(definition!);
        if (activation.State == ActivationState.Active)
        {
            Persist(package, new PluginSetting { Enabled = true });
            return $"plugin {package} activated";
        }
        return $"plugin {package} failed to activate: {activation.Error}";
    }

    /** 摘除动态插件并返回弱引用供回收验证;内置插件(无 ALC)返回 null。
     *  独立成帧并只返回弱引用,使插件类型/定义等强引用在本方法返回后即可回收。 */
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(WeakReference? Weak, bool Leaked)> UnloadDynamicAsync(string package, bool force)
    {
        var activation = await composition.Root.Scheduler.UnloadAsync(package, force: force);
        var leaked = activation is null || activation.State != ActivationState.Disposed;
        Release(ref activation);
        await composition.Root.Scheduler.SettleAsync();   // 排空调度泵,避免快照任务滞留插件引用
        if (!_loadedContexts.Remove(package, out var context))
            return (null, leaked);
        _ = host.Catalog.Remove(package);
        var weak = PluginUnloader.Unload(context);
        Release(ref context);
        return (weak, leaked);
    }

    /** 置空引用以断开 插件类型→程序集→ALC 的强引用,供后续回收校验。 */
    private static void Release<T>(ref T? value) where T : class
    {
        _ = value;
        value = null;
    }

    private bool IsKnown(string package)
        => composition.Root.Scheduler.Find(package) is not null || host.Catalog.PackageNames.Contains(package, StringComparer.Ordinal);

    /** 分离帧验证 ALC 回收:调用帧的残留引用(异步状态机/局部变量)会误判,故放到独立任务里做。 */
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task VerifyCollectionAsync(string package, WeakReference weak)
    {
        await Task.Yield();
        if (PluginUnloader.WaitForCollection(weak, out var report))
        {
            _leaked.Remove(package);
            return;
        }
        _leaked.Add(package);
        composition.Root.Logger.Error("%s",
            $"plugin {package} 的加载上下文未能回收:{report};静态引用或未退出的线程会阻止回收,"
            + "可用 `dotnet-dump analyze <pid>` 执行 `!dumpheap -type LoaderAllocator` 与 `!gcroot <addr>` 排查");
    }

    private void Persist(string package, PluginSetting? setting)
    {
        if (setting is null)
        {
            if (!settings.Plugins.Remove(package))
                return;
        }
        else
        {
            settings.Plugins[package] = setting;
        }
        settings.SavePlugins(home);
    }

    public void Dispose()
    {
        foreach (var context in _loadedContexts.Values.Distinct())
        {
            if (context.IsCollectible)
                context.Unload();
        }
        _loadedContexts.Clear();
    }

    private static string Removed(string package, bool leaked)
        => leaked
            ? $"plugin {package} removed, but settlement timed out (effects may still be running; marked leaked)"
            : $"plugin {package} removed";
}
