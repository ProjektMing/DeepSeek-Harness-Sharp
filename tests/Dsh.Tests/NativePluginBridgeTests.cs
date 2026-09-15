using System.Text.Json;
using Dsh.Core;
using Dsh.Llm;
using Dsh.Plugins;
using Dsh.Plugins.Native;
using Dsh.Plugins.Native.Host;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class NativePluginBridgeTests
{
    [Fact]
    public async Task RegisteredTool_InvokesPluginAndRendersText()
    {
        var ctx = new Context();
        _ = new SystemPrompt(ctx, new SystemPromptConfig());
        var tools = new ToolRuntime(ctx);
        var plugin = new FakeNativePlugin();

        var registration = (IDisposable)NativePluginBridge.CreateDefinition(plugin).Apply(ctx, null)!;

        Assert.NotNull(tools.Get("native_echo"));
        var result = await tools.Execute(new ToolExecutionInput
        {
            CallId = ToolCallId.Create($"call-{Guid.NewGuid():N}"),
            Name = "native_echo",
            Arguments = JsonDocument.Parse("""{"message":"hi"}""").RootElement,
            Agent = null,
            Signal = default,
        });
        Assert.False(result.IsError);
        Assert.Contains("echo:hi", string.Concat(result.Content.OfType<TextBlock>().Select(block => block.Text)));
        Assert.Contains(ctx.Root.Logger.Buffer, message => message.Text.Contains("native plugin \"test.native\""));

        registration.Dispose();
        Assert.True(plugin.Deactivated);
        Assert.Null(tools.Get("native_echo"));
    }

    [Fact]
    public void CatalogDefinition_ExposesNativePackage()
    {
        var catalog = new PluginCatalog();
        var plugin = new FakeNativePlugin();
        catalog.Register(
            PluginDescriptor.For(plugin.Package, PluginForm.NativeLibrary),
            () => NativePluginBridge.AsPlugin(plugin));

        Assert.Contains(plugin.Package, catalog.PackageNames);
        Assert.True(catalog.TryCreateDefinition(plugin.Package, out var definition));
        Assert.Equal(plugin.Package, definition!.Name);
        Assert.True(catalog.TryGet(plugin.Package, out var create));
        Assert.Empty(create().Inject);
    }

    private sealed class FakeNativePlugin : INativePlugin
    {
        public string Package => "test.native";

        public bool Deactivated { get; private set; }

        public void Activate(INativePluginHost host)
        {
            host.Log(DshNativePluginAbi.LogInfo, "activated");
            host.RegisterTool(
                "native_echo",
                "Echo a message",
                """{"type":"object","properties":{"message":{"type":"string"}}}""",
                input =>
                {
                    using var document = JsonDocument.Parse(input);
                    var message = document.RootElement.GetProperty("message").GetString();
                    return JsonSerializer.Serialize(new { text = $"echo:{message}" });
                });
        }

        public void Deactivate() => Deactivated = true;

        public void Dispose()
        {
        }
    }
}
