using Microsoft.Extensions.Logging;

namespace Dsh.Runtime.Logging;

internal static class LogLevels
{
    public static LogLevel Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "info" or "information" => LogLevel.Information,
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "warn" or "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" or "fatal" => LogLevel.Critical,
        "off" or "none" => LogLevel.None,
        _ => throw new ArgumentException($"unknown logging level: {name}", nameof(name)),
    };

    public static LoggerType ToLoggerType(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => LoggerType.Error,
        LogLevel.Warning => LoggerType.Warn,
        LogLevel.Debug or LogLevel.Trace => LoggerType.Debug,
        _ => LoggerType.Info,
    };

    public static int ToLoggerLevel(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => LoggerLevel.Error,
        LogLevel.Warning => LoggerLevel.Warn,
        LogLevel.Debug or LogLevel.Trace => LoggerLevel.Debug,
        _ => LoggerLevel.Info,
    };

    public static string ToLabel(LogLevel level) => level switch
    {
        LogLevel.Critical => "FATAL",
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARN",
        LogLevel.Debug => "DEBUG",
        LogLevel.Trace => "TRACE",
        LogLevel.None => "NONE",
        _ => "INFO",
    };
}
