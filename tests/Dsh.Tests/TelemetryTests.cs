using System.Text.Json;
using Dsh.Runtime;
using Dsh.Telemetry;
using TelemetryPlugin = Dsh.Telemetry.Plugin;

namespace Dsh.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void RecordsLifecycleEventsAsJsonLines()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "telemetry-test", $"{Guid.NewGuid():N}.jsonl");
        var ctx = new Context();
        try
        {
            using (var telemetry = new TelemetryService(ctx, path))
            {
                telemetry.AgentSessionStarted("s1", "tui");
                telemetry.AgentDisposed("s1");
                telemetry.AgentFailed("s1", 2, 3, new InvalidOperationException("boom"));
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal("agent/session-start", first.RootElement.GetProperty("event").GetString());
            Assert.Equal("tui", first.RootElement.GetProperty("payload").GetProperty("source").GetString());
            using var third = JsonDocument.Parse(lines[2]);
            var failure = third.RootElement.GetProperty("payload");
            Assert.Equal("agent/error", third.RootElement.GetProperty("event").GetString());
            Assert.Equal("InvalidOperationException", failure.GetProperty("errorType").GetString());
            Assert.Equal("boom", failure.GetProperty("errorMessage").GetString());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Plugin_SkipsWhenDisabled()
    {
        var ctx = new Context();
        ctx.SetOwn("dshHomePath", Path.Combine(AppContext.BaseDirectory, $"telemetry-off-{Guid.NewGuid():N}"));
        using var registration = new TelemetryPlugin().Apply(ctx, new Dictionary<string, object?>
        {
            ["enabled"] = false,
        });

        Assert.Null(ctx.Get<TelemetryService>(TelemetryService.ServiceName, false));
    }

    [Fact]
    public void Plugin_ProvidesServiceAndWritesToHomeByDefault()
    {
        var home = Path.Combine(AppContext.BaseDirectory, $"telemetry-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        var ctx = new Context();
        ctx.SetOwn("dshHomePath", home);
        try
        {
            using var registration = new TelemetryPlugin().Apply(ctx, null);

            Assert.NotNull(ctx.Get<TelemetryService>(TelemetryService.ServiceName, false));
            var telemetry = ctx.Get<TelemetryService>(TelemetryService.ServiceName, false)!;
            telemetry.AgentSessionStarted("s2", "headless");
            Assert.True(File.Exists(Path.Combine(home, TelemetryConfig.DefaultFileName)));
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
