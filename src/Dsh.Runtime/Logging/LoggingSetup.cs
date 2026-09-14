using Microsoft.Extensions.Logging;

namespace Dsh.Runtime.Logging;

/** MEL 日志装配:内存缓冲 + 可选文件/控制台 provider;Context 通过它取得 LoggerFactory 与缓冲。 */
public sealed class LoggingSetup : IDisposable
{
    private LoggingSetup(ILoggerFactory factory, InMemoryLogProvider memory)
    {
        Factory = factory;
        Memory = memory;
    }

    public ILoggerFactory Factory { get; }
    public InMemoryLogProvider Memory { get; }

    public static LoggingSetup Create(LoggingOptions? options = null, string? logDirectory = null, bool allowConsole = false)
    {
        options ??= new LoggingOptions();
        var memory = new InMemoryLogProvider { BufferSize = options.BufferSize };
        var file = options.File && logDirectory is not null ? new FileLogProvider(logDirectory, options) : null;
        var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevels.Parse(options.Level));
            builder.AddProvider(memory);
            if (file is not null)
                builder.AddProvider(file);
            if (allowConsole && options.Console)
            {
                builder.AddSimpleConsole(console =>
                {
                    console.SingleLine = true;
                    console.TimestampFormat = "HH:mm:ss ";
                });
            }
        });
        var setup = new LoggingSetup(factory, memory);
        if (logDirectory is not null)
            setup.Announce(options, logDirectory);
        return setup;
    }

    private void Announce(LoggingOptions options, string? logDirectory)
        => Factory.CreateLogger("dsh").LogInformation(
            "logging started: level={Level}, buffer={BufferSize}, file={File}, console={Console}",
            LogLevels.Parse(options.Level),
            options.BufferSize,
            logDirectory ?? "off",
            options.Console ? "on" : "off");

    public void Dispose() => Factory.Dispose();
}
