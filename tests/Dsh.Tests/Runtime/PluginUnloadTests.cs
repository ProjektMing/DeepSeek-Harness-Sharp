using System.Reflection;
using System.Runtime.CompilerServices;
using Dsh.Plugins;
using Dsh.Runtime;

namespace Dsh.Tests.Runtime;

public class PluginUnloadTests
{
    [Fact]
    public async Task DynamicPlugin_UnloadCollectsLoadContext()
    {
        var ctx = new Context();
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "Dsh.Checkpoints.dll");
        Assert.True(File.Exists(pluginPath), $"plugin assembly not found: {pluginPath}");
        var weak = await LoadActivateUnloadAsync(ctx, pluginPath);
        Assert.True(PluginUnloader.WaitForCollection(weak, out var report), report);
    }

    [Fact]
    public async Task DynamicPlugin_UnloadRemovesProvidedServices()
   {
        var ctx = new Context();
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "Dsh.Checkpoints.dll");
        var (activation, provided) = await LoadAndUnloadTrackedAsync(ctx, pluginPath);
        Assert.Equal(ActivationState.Disposed, activation.State);
        foreach (var name in provided)
            Assert.Null(ctx.Get(name, strict: false));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> LoadActivateUnloadAsync(Context ctx, string pluginPath)
    {
        var catalog = new PluginCatalog();
        var context = new PluginLoadContext(pluginPath);
        var assembly = context.LoadFromAssemblyPath(pluginPath);
        var added = catalog.RegisterAssembly(assembly, context);
        Assert.Contains("@deepseek-ai/dsh-checkpoints", added);
        var definition = catalog.CreateDefinition("@deepseek-ai/dsh-checkpoints");
        var activation = await ctx.Scheduler.AddAsync(definition);
        await ctx.Scheduler.UnloadAsync(activation.Name);
        Assert.Equal(ActivationState.Disposed, activation.State);
        catalog.Remove(activation.Name);
        return PluginUnloader.Unload(context);
    }

    [Fact]
    public async Task LeakedReference_IsReportedTruthfully()
    {
        var ctx = new Context();
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "Dsh.Tests.dll");
        var weak = await LoadAndPinAsync(ctx, pluginPath);
        try
        {
            Assert.False(PluginUnloader.WaitForCollection(weak, out var report));
            Assert.Contains("still referenced", report);
        }
        finally
        {
            PinnedAssembly = null;
        }
    }

    private static Assembly? PinnedAssembly;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> LoadAndPinAsync(Context ctx, string pluginPath)
    {
        var catalog = new PluginCatalog();
        var context = new PluginLoadContext(pluginPath);
        var assembly = context.LoadFromAssemblyPath(pluginPath);
        catalog.RegisterAssembly(assembly, context);
        var definition = catalog.CreateDefinition("test/local");
        var activation = await ctx.Scheduler.AddAsync(definition);
        await ctx.Scheduler.UnloadAsync(activation.Name);
        catalog.Remove(activation.Name);
        PinnedAssembly = assembly;   // 模拟插件静态引用:卸载后仍被宿主侧强引用
        return PluginUnloader.Unload(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(PluginActivation Activation, IReadOnlyList<string> Provided)> LoadAndUnloadTrackedAsync(Context ctx, string pluginPath)
    {
        var catalog = new PluginCatalog();
        var context = new PluginLoadContext(pluginPath);
        var assembly = context.LoadFromAssemblyPath(pluginPath);
        var added = catalog.RegisterAssembly(assembly, context);
        Assert.Contains("@deepseek-ai/dsh-checkpoints", added);
        var definition = catalog.CreateDefinition("@deepseek-ai/dsh-checkpoints");
        var activation = await ctx.Scheduler.AddAsync(definition);
        var provided = activation.ProvidedNames.ToList();
        await ctx.Scheduler.UnloadAsync(activation.Name);
        catalog.Remove(activation.Name);
        _ = PluginUnloader.Unload(context);
        return (activation, provided);
    }
}
