using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Tests.Runtime;

public class RuntimeCompositionTests
{
    [Fact]
    public async Task Start_ActivatesEntriesInDependencyOrder()
    {
        var activated = new List<string>();
        var definitions = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal)
        {
            ["first"] = PluginDefinition.From((ctx, _) =>
            {
                activated.Add("first");
                ctx.Provide("service", "value");
                return null;
            }, "first"),
            ["second"] = PluginDefinition.From((ctx, _) =>
            {
                activated.Add("second");
                Assert.NotNull(ctx.Get("service"));
                return null;
            }, "second", ["service"]),
        };
        var ctx = new Context();
        var composition = await Composition.StartAsync(ctx,
        [
            new PluginEntry(definitions["second"], null),
            new PluginEntry(definitions["first"], new Dictionary<string, object?> { ["answer"] = 42L }),
        ]);

        Assert.Equal(["first", "second"], activated);
        Assert.Equal(2, composition.Activations.Count);
        Assert.All(composition.Activations, activation => Assert.Equal(ActivationState.Active, activation.State));
        var configured = composition.Find("first")!;
        Assert.Equal(42L, (configured.Config as IDictionary<string, object?>)?["answer"]);
    }

    [Fact]
    public async Task Start_ActivationFailureFailsBoot()
    {
        var ctx = new Context();
        var error = await Assert.ThrowsAsync<RuntimeException>(() => Composition.StartAsync(ctx,
        [
            new PluginEntry(PluginDefinition.From((_, _) => throw new InvalidOperationException("kaboom"), "broken"), null),
        ]));
        Assert.Equal("BOOT_FAILED", error.Code);
        Assert.Contains("kaboom", error.Message);
    }
}
