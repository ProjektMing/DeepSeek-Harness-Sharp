using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Tests.Runtime;

public class RuntimeCompositionTests
{
    [Fact]
    public async Task Composition_ActivatesEntriesInDependencyOrder()
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
        var directory = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cordis.yml"), """
                - name: second
                - name: first
                  config:
                    answer: 42
                """);
            var ctx = new Context();
            var composition = Composition.Load(
                ctx,
                Path.Combine(directory, "cordis.yml"),
                builtins: null,
                name => definitions.GetValueOrDefault(name));

            Assert.Equal(["first", "second"], activated);
            Assert.Equal(2, composition.Activations.Count);
            Assert.All(composition.Activations, activation => Assert.Equal(ActivationState.Active, activation.State));
            var configured = composition.Find("first")!;
            Assert.Equal(42L, (configured.Config as IDictionary<string, object?>)?["answer"]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Composition_AppliesPatches()
    {
        var received = new List<object?>();
        var definitions = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal)
        {
            ["alpha"] = PluginDefinition.From((_, config) =>
            {
                received.Add(config);
                return null;
            }, "alpha"),
        };
        var directory = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cordis.yml"), """
                - id: alpha
                  name: alpha
                  config:
                    mode: base

                - name: disabled-entry
                  disabled: true
                """);
            var patches = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = "alpha", ["config"] = new Dictionary<string, object?> { ["mode"] = "patched" } },
                new() { ["insert"] = new List<object?> { new Dictionary<string, object?> { ["name"] = "alpha" } } },
            };
            var ctx = new Context();
            var composition = Composition.Load(
                ctx,
                Path.Combine(directory, "cordis.yml"),
                builtins: null,
                name => definitions.GetValueOrDefault(name),
                patches);

            Assert.Equal(2, composition.Activations.Count);
            Assert.Equal(2, received.Count);
            Assert.Equal("patched", (received[0] as IDictionary<string, object?>)?["mode"]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Composition_MissingPluginFailsBoot()
    {
        var directory = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cordis.yml"), """
                - name: missing-plugin
                """);
            var ctx = new Context();
            var error = Assert.Throws<RuntimeException>(() => Composition.Load(
                ctx,
                Path.Combine(directory, "cordis.yml"),
                builtins: null,
                _ => null));
            Assert.Equal("BOOT_FAILED", error.Code);
            Assert.Contains("missing-plugin", error.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Composition_ActivationFailureFailsBoot()
    {
        var definitions = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal)
        {
            ["broken"] = PluginDefinition.From((_, _) => throw new InvalidOperationException("kaboom"), "broken"),
        };
        var directory = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cordis.yml"), """
                - name: broken
                """);
            var ctx = new Context();
            var error = Assert.Throws<RuntimeException>(() => Composition.Load(
                ctx,
                Path.Combine(directory, "cordis.yml"),
                builtins: null,
                name => definitions.GetValueOrDefault(name)));
            Assert.Equal("BOOT_FAILED", error.Code);
            Assert.Contains("kaboom", error.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Composition_BuiltinsTakePrecedence()
    {
        var usedBuiltin = false;
        var directory = CreateDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cordis.yml"), """
                - name: builtin-plugin
                """);
            var ctx = new Context();
            var composition = Composition.Load(
                ctx,
                Path.Combine(directory, "cordis.yml"),
                new Dictionary<string, PluginDefinition>(StringComparer.Ordinal)
                {
                    ["builtin-plugin"] = PluginDefinition.From((_, _) =>
                    {
                        usedBuiltin = true;
                        return null;
                    }, "builtin-plugin"),
                },
                _ => null);
            Assert.True(usedBuiltin);
            Assert.Single(composition.Activations);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dsh-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
