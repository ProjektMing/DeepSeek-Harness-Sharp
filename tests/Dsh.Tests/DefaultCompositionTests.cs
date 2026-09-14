using Dsh.Boot;
using Dsh.Core;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class DefaultCompositionTests
{
    [Fact]
    public async Task DefaultManifest_ActivatesPluginsAndRegistersIdeHistoryTool()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dsh-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var home = HarnessHome.Resolve(Path.Combine(directory, "home"));
            using var app = await ConfigBoot.Compose(new HarnessOptions(home, Cwd: directory));
            var composition = app.Composition!;
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-checkpoints")?.State);
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-ide-history")?.State);
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-core")?.State);
            var tools = app.Ctx.Get<ToolRuntime>(ToolRuntime.ServiceName);
            Assert.NotNull(tools);
            Assert.NotNull(tools.Get("ide_history"));
            Assert.NotNull(tools.Get("todo_write"));
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-goal")?.State);
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-tool-goal")?.State);

            var settings = File.ReadAllText(Path.Combine(home.Root, "settings.yaml"));
            Assert.Contains("plugins:", settings);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task EntrypointPlugin_ActivatesAlongsideDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dsh-entrypoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var home = HarnessHome.Resolve(Path.Combine(directory, "home"));
            using var app = await ConfigBoot.Compose(new HarnessOptions(
                home,
                Cwd: directory,
                IsTui: true,
                EntrypointPlugin: "@deepseek-ai/dsh-tui"));
            Assert.Equal(ActivationState.Active, app.Composition!.Find("@deepseek-ai/dsh-tui")?.State);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
