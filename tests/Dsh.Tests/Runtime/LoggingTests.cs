using Dsh.Runtime;
using Dsh.Runtime.Logging;
using Microsoft.Extensions.Logging;

namespace Dsh.Tests.Runtime;

public class LoggingTests
{
    [Fact]
    public void LevelFilter_DropsBelowConfiguredMinimum()
    {
        using var setup = LoggingSetup.Create(new LoggingOptions { Level = "warn" });
        var ctx = new Context(setup);
        var logger = ctx.LoggerFor("test");
        logger.Debug("debug");
        logger.Info("info");
        logger.Warn("warn");
        logger.Error("error");
        Assert.Equal(["warn", "error"], ctx.Root.Logger.Buffer.Select(message => message.Text));
    }

    [Fact]
    public void Redaction_MasksApiKeys()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        ctx.LoggerFor("test").Info("key %s used", "sk-1234567890abcdef");
        var message = Assert.Single(ctx.Root.Logger.Buffer);
        Assert.Contains("sk-***", message.Text);
        Assert.DoesNotContain("1234567890abcdef", message.Text);
    }

    [Fact]
    public void Redaction_MasksBearerTokensInExceptions()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        ctx.LoggerFor("test").Error("call failed: %s", "Authorization: Bearer abcdef0123456789");
        var message = Assert.Single(ctx.Root.Logger.Buffer);
        Assert.Contains("Bearer ***", message.Text);
        Assert.DoesNotContain("abcdef0123456789", message.Text);
    }

    [Fact]
    public void Logger_FormatsPlaceholdersCompatibility()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        ctx.LoggerFor("format").Info("int %d float %f text %s %%", 7L, 1.5, "x");
        var message = Assert.Single(ctx.Root.Logger.Buffer);
        Assert.Equal("int 7 float 1.5 text x %", message.Text);
    }

    [Fact]
    public void Logger_AcceptsExceptionAsFirstArgument()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        ctx.LoggerFor("format").Error("%s", new InvalidOperationException("boom"));
        var message = Assert.Single(ctx.Root.Logger.Buffer);
        Assert.Contains("boom", message.Text);
        Assert.Equal(LoggerType.Error, message.Type);
    }

    [Fact]
    public void FileLogProvider_WritesAnnouncementAndEntries()
    {
        var directory = CreateScratchDirectory();
        try
        {
            using var setup = LoggingSetup.Create(new LoggingOptions(), directory);
            var ctx = new Context(setup);
            ctx.LoggerFor("test").Info("hello file");
            var file = Assert.Single(Directory.GetFiles(directory, "dsh-*.log"));
            var content = File.ReadAllText(file);
            Assert.Contains("logging started", content);
            Assert.Contains("test: hello file", content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FileLogProvider_RotatesWhenExceedingMaxBytes()
    {
        var directory = CreateScratchDirectory();
        try
        {
            using var setup = LoggingSetup.Create(new LoggingOptions { FileMaxBytes = 256 }, directory);
            var ctx = new Context(setup);
            for (var i = 0; i < 40; i++)
                ctx.LoggerFor("rotate").Info("entry %d", i);
            var files = Directory.GetFiles(directory, "dsh-*.log");
            Assert.True(files.Length > 1, $"expected rotation, got {string.Join(", ", files)}");
            Assert.Contains(files, path => path.EndsWith(".1.log", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FileLogProvider_RemovesExpiredFiles()
    {
        var directory = CreateScratchDirectory();
        try
        {
            var expired = Path.Combine(directory, $"dsh-{DateTime.Now.AddDays(-30):yyyyMMdd}.log");
            var fresh = Path.Combine(directory, $"dsh-{DateTime.Now:yyyyMMdd}.log");
            File.WriteAllText(expired, "old");
            File.WriteAllText(fresh, "new");
            using var provider = new FileLogProvider(directory, new LoggingOptions { KeepDays = 15 });
            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FileLogProvider_AppendsToExistingFile()
    {
        var directory = CreateScratchDirectory();
        try
        {
            var file = Path.Combine(directory, $"dsh-{DateTime.Now:yyyyMMdd}.log");
            File.WriteAllText(file, "previous run\n");
            using var setup = LoggingSetup.Create(new LoggingOptions(), directory);
            var ctx = new Context(setup);
            ctx.LoggerFor("test").Info("second run");
            var content = File.ReadAllText(file);
            Assert.StartsWith("previous run", content);
            Assert.Contains("second run", content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ConsoleProvider_RespectsAllowConsoleSwitch()
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            using (var setup = LoggingSetup.Create(new LoggingOptions { Console = true }, logDirectory: null, allowConsole: true))
                setup.Factory.CreateLogger("console-probe").LogInformation("console-on-marker");
            Assert.Contains("console-on-marker", writer.ToString());

            writer.GetStringBuilder().Clear();
            using (var setup = LoggingSetup.Create(new LoggingOptions { Console = true }, logDirectory: null, allowConsole: false))
                setup.Factory.CreateLogger("console-probe").LogInformation("console-off-marker");
            Assert.DoesNotContain("console-off-marker", writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string CreateScratchDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-scratch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
