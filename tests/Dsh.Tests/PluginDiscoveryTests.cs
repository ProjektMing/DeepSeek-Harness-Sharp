using Dsh.Plugins;

namespace Dsh.Tests;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void Scan_RegistersManagedPluginFromFolderAsManagedAssembly()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Dsh.Goal.dll");
        Assert.True(File.Exists(source), $"missing build output: {source}");
        var folder = Path.Combine(Path.GetTempPath(), $"dsh-plugin-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(source, Path.Combine(folder, "Dsh.Goal.dll"));
            var host = new PluginHost();

            var result = host.Scan(folder, nativeBridge: null);

            Assert.Empty(result.Skipped);
            Assert.Contains(result.Managed, entry => entry.Package == "@deepseek-ai/dsh-goal");
            Assert.True(host.Catalog.TryDescribe("@deepseek-ai/dsh-goal", out var descriptor));
            Assert.Equal(PluginForm.ManagedAssembly, descriptor.Form);
            Assert.True(descriptor.Capabilities.HasFlag(PluginCapabilities.Unload));
            Assert.True(host.Catalog.TryCreateDefinition("@deepseek-ai/dsh-goal", out var definition));
            Assert.NotNull(definition);
            foreach (var entry in result.Managed)
                entry.Context.Unload();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
