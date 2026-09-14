using Dsh.Boot;
using Dsh.Core;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class HotPlugTests
{
    [Fact]
    public async Task PluginManager_RemovesAndReactivatesPlugin()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dsh-hotplug-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "hotplug.cordis.yml"), """
                - name: '@deepseek-ai/dsh-core'
                - name: '@deepseek-ai/dsh-interaction'
                - name: '@deepseek-ai/dsh-persistence'
                - name: '@deepseek-ai/dsh-tool-todo'
                """);
            var home = HarnessHome.Resolve(Path.Combine(dir, "home"));
            using var app = await ConfigBoot.Compose(Path.Combine(dir, "hotplug.cordis.yml"), new HarnessOptions(home, Cwd: dir));
            var manager = app.Ctx.Get<HarnessPluginManager>("pluginManager")!;
            Assert.Contains("@deepseek-ai/dsh-tool-todo", manager.PackageNames);
            var tools = app.Ctx.Get<ToolRuntime>(ToolRuntime.ServiceName)!;
            Assert.NotNull(tools.Get("todo_write"));

            var removed = await manager.RemoveAsync("@deepseek-ai/dsh-tool-todo");
            Assert.Contains("removed", removed);
            Assert.Null(tools.Get("todo_write"));
            Assert.Null(app.Composition!.Find("@deepseek-ai/dsh-tool-todo"));

            var added = await manager.AddAsync("@deepseek-ai/dsh-tool-todo");
            Assert.Contains("activated", added);
            Assert.NotNull(tools.Get("todo_write"));
            Assert.Equal(ActivationState.Active, app.Composition.Find("@deepseek-ai/dsh-tool-todo")!.State);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task PluginManager_ReportsUnknownPackage()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dsh-hotplug-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "hotplug.cordis.yml"), """
                - name: '@deepseek-ai/dsh-core'
                """);
            var home = HarnessHome.Resolve(Path.Combine(dir, "home"));
            using var app = await ConfigBoot.Compose(Path.Combine(dir, "hotplug.cordis.yml"), new HarnessOptions(home, Cwd: dir));
            var manager = app.Ctx.Get<HarnessPluginManager>("pluginManager")!;
            Assert.Contains("not found", await manager.AddAsync("@deepseek-ai/dsh-missing"));
            Assert.Contains("not active", await manager.RemoveAsync("@deepseek-ai/dsh-missing"));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
