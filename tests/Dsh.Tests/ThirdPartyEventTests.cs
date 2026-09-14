using System.Runtime.CompilerServices;
using Dsh.Plugins;
using Dsh.Runtime;

namespace Dsh.Tests;

public class ThirdPartyEventTests
{
    [Fact]
    public async Task CollectibleAssembly_RegistersHandlerAndReceivesItsOwnNotification()
    {
        var ctx = new Context();
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "Dsh.Tests.dll");
        Assert.True(File.Exists(pluginPath), $"plugin assembly not found: {pluginPath}");

        var received = await LoadAndActivateAsync(ctx, pluginPath);

        Assert.Equal("third-party-hello", received);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string?> LoadAndActivateAsync(Context ctx, string pluginPath)
    {
        var catalog = new PluginCatalog();
        var context = new PluginLoadContext(pluginPath);
        var assembly = context.LoadFromAssemblyPath(pluginPath);
        var added = catalog.RegisterAssembly(assembly, context);
        Assert.Contains("test/local", added);
        var definition = catalog.CreateDefinition("test/local");
        var activation = await ctx.Scheduler.AddAsync(definition);
        Assert.Equal(ActivationState.Active, activation.State);
        var pluginType = assembly.GetType("Dsh.Tests.TestPlugin")!;
        return (string?)pluginType.GetProperty("ReceivedText")!.GetValue(null);
    }
}
