using Dsh.Boot;
using Dsh.Core;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class DefaultCompositionTests
{
    [Fact]
    public async Task DiscoveredPlugins_ActivateAndRegisterIdeHistoryTool()
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
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-tool-web")?.State);
            Assert.NotNull(tools.Get("web_search"));
            Assert.NotNull(tools.Get("web_fetch"));
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-telemetry")?.State);
            Assert.NotNull(app.Ctx.Get<Dsh.Telemetry.TelemetryService>(Dsh.Telemetry.TelemetryService.ServiceName, false));
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-tool-session-query")?.State);
            Assert.NotNull(tools.Get("session_search"));
            Assert.NotNull(app.Ctx.Get<Dsh.SessionQuery.SessionQueryService>(Dsh.SessionQuery.SessionQueryService.ServiceName, false));
            Assert.NotNull(app.Ctx.Get<Dsh.Core.IMemoryStore>(Dsh.Core.MemoryServices.Store, false));
            Assert.NotNull(tools.Get("memory_save"));

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
