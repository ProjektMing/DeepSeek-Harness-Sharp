using Dsh.Runtime;
using Dsh.Runtime.Events;
using Dsh.Plugins;

[assembly: DshPlugin("test/local")]

namespace Dsh.Tests;

public sealed class PluginHostTests
{
    [Fact]
    public async Task RegisterCompiledIn_RegistersDefinitionAndActivates()
    {
        TestPlugin.Applied = false;
        var host = new PluginHost();
        host.RegisterCompiledIn();

        Assert.True(host.Catalog.TryDescribe("test/local", out var descriptor));
        Assert.Equal(PluginForm.CompiledIn, descriptor.Form);
        Assert.True(host.Catalog.TryCreateDefinition("test/local", out var definition));

        // 入口描述符随插件清单生成:丢掉接线(如 Web/Lsp 在重构中丢过)会在这里暴露。
        Assert.Contains(host.Catalog.Descriptors, entry => entry.Entry == "web");
        Assert.Contains(host.Catalog.Descriptors, entry => entry.Entry == "lsp");
        Assert.Contains(host.Catalog.Descriptors, entry => entry.Package == "@deepseek-ai/dsh-tool-web");

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
