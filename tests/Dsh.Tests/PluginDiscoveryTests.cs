using Dsh.Boot;
using Dsh.Plugins;

namespace Dsh.Tests;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void ScanPluginDirectory_RegistersPluginFromFolder()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Dsh.Goal.dll");
        Assert.True(File.Exists(source), $"missing build output: {source}");
        var folder = Path.Combine(Path.GetTempPath(), $"dsh-plugin-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(source, Path.Combine(folder, "Dsh.Goal.dll"));
            var host = new PluginHost();

            var discovered = host.ScanPluginDirectory(folder);

            Assert.Contains("@deepseek-ai/dsh-goal", discovered);
            Assert.True(host.Catalog.TryCreateDefinition("@deepseek-ai/dsh-goal", out var definition));
            Assert.NotNull(definition);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void IncludeDiscovered_EnablesOnlyUnlistedPlugins()
    {
        var plugins = new Dictionary<string, PluginSetting>(StringComparer.Ordinal)
        {
            ["@deepseek-ai/dsh-plan-mode"] = new() { Enabled = false },
        };

        PluginManifest.IncludeDiscovered(plugins, ["@deepseek-ai/dsh-plan-mode", "@deepseek-ai/dsh-lsp"]);

        Assert.False(plugins["@deepseek-ai/dsh-plan-mode"].Enabled);
        Assert.True(plugins["@deepseek-ai/dsh-lsp"].Enabled);
    }
}
