using Dsh.Boot;
using Dsh.Core;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class StandardProfileTests
{
    [Fact]
    public async Task StandardProfile_ActivatesPluginsAndRegistersIdeHistoryTool()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dsh-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var home = HarnessHome.Resolve(Path.Combine(directory, "home"));
            using var app = await ConfigBoot.ComposeProfile(
                "standard",
                patches: null,
                new HarnessOptions(home, Cwd: directory));
            var composition = app.Composition!;
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-checkpoints")?.State);
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-ide-history")?.State);
            Assert.Equal(ActivationState.Active, composition.Find("@deepseek-ai/dsh-core")?.State);
            var tools = app.Ctx.Get<ToolRuntime>(ToolRuntime.ServiceName);
            Assert.NotNull(tools);
            Assert.NotNull(tools.Get("ide_history"));
            Assert.NotNull(tools.Get("todo_write"));
            Assert.Equal(ActivationState.Pending, composition.Find("@deepseek-ai/dsh-goal")?.State);
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
