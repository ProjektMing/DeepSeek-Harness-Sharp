using Dsh.Runtime;
using Dsh.Runtime.Events;
using Dsh.Plugins;

[assembly: DshPlugin("test/local")]

namespace Dsh.Tests;

public sealed class PluginHostTests
{
    [Fact]
    public void RegisterAssembly_TryCreate_AppliesPlugin()
    {
        var catalog = new PluginCatalog();
        catalog.RegisterAssembly(typeof(PluginHostTests).Assembly);

        Assert.True(catalog.TryCreate("test/local", out var plugin));
        var testPlugin = Assert.IsType<TestPlugin>(plugin);
        Assert.Empty(testPlugin.Inject);

        using var registration = testPlugin.Apply(new Context(), null);
        Assert.True(TestPlugin.Applied);
    }

    [Fact]
    public async Task ScanDirectory_RegistersDefinitionAndActivates()
    {
        TestPlugin.Applied = false;
        var host = new PluginHost();
        host.ScanDirectory(AppContext.BaseDirectory);

        Assert.True(host.Catalog.TryCreateDefinition("test/local", out var definition));

        var ctx = new Context();
        var activation = ctx.Plugin(definition!);
        await activation.WaitAsync();
        Assert.True(TestPlugin.Applied);
        Assert.Equal(ActivationState.Active, activation.State);
    }
}

public sealed record ThirdPartyNotification(string Text) : INotification
{
    public static string EventName => "third-party/notification";
}

public sealed class TestPlugin : IDshPlugin
{
    public static bool Applied { get; set; }
    public static string? ReceivedText { get; set; }

    public string[] Inject => [];

    public IDisposable Apply(Context ctx, object? config)
    {
        Applied = true;
        ctx.On<ThirdPartyNotification>(notification => ReceivedText = notification.Text);
        ctx.Emit(new ThirdPartyNotification("third-party-hello"));
        return new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
