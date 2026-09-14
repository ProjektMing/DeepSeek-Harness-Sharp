using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dsh.Runtime.Logging;
using Microsoft.Extensions.Logging;

namespace Dsh.Runtime;

public enum LoggerType
{
    Error,
    Warn,
    Info,
    Debug,
}

public static class LoggerLevel
{
    public const int Error = 0;
    public const int Warn = 1;
    public const int Info = 2;
    public const int Debug = 3;
}

public sealed record Message
{
    public long Sn { get; init; }
    public long Ts { get; init; }
    public required string Name { get; init; }
    public LoggerType Type { get; init; }
    public int Level { get; init; }
    public required string Text { get; init; }
}

public sealed partial class Logger
{
    public const int DefaultMaxLength = 10240;

    [GeneratedRegex("%([a-zA-Z%])")]
    private static partial Regex Placeholder();

    public string Name { get; }
    private readonly ILogger _logger;

    internal Logger(string name, ILogger logger)
    {
        Name = name;
        _logger = logger;
    }

    public void Error(object? format, params object?[] args) => Log(LogLevel.Error, format, args);
    public void Warn(object? format, params object?[] args) => Log(LogLevel.Warning, format, args);
    public void Info(object? format, params object?[] args) => Log(LogLevel.Information, format, args);
    public void Debug(object? format, params object?[] args) => Log(LogLevel.Debug, format, args);

    private void Log(LogLevel level, object? format, object?[] args)
    {
        var allArgs = format is null ? args : [format, .. args];
        if (allArgs.Length == 1 && allArgs[0] is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                Log(level, inner, []);
            return;
        }
        if (!_logger.IsEnabled(level))
            return;
        var text = FormatText(allArgs);
        _logger.Log(level, 0, text, null, static (state, _) => state);
    }

    internal static string FormatText(object?[] allArgs)
    {
        var args = allArgs.ToList();
        if (args.Count > 0 && args[0] is Exception error)
        {
            args[0] = error.ToString();
            args.Insert(0, "%s");
        }
        else if (args.Count == 0 || args[0] is not string)
        {
            args.Insert(0, "%o");
        }

        var format = (string)args[0]!;
        args.RemoveAt(0);
        format = Placeholder().Replace(format, match =>
        {
            if (match.Value == "%%")
                return "%";
            var c = match.Groups[1].Value[0];
            var value = args.Count > 0 ? args[0] : null;
            if (args.Count > 0)
                args.RemoveAt(0);
            return c switch
            {
                's' => value?.ToString() ?? "null",
                'd' or 'i' => Convert.ToInt64(value).ToString(),
                'f' => Convert.ToDouble(value).ToString(CultureInfo.InvariantCulture),
                'o' or 'O' => JsonSerializer.Serialize(value),
                'c' => "",
                'C' => value?.ToString() ?? "null",
                _ => match.Value,
            };
        });

        foreach (var arg in args)
        {
            var text = arg is string or null ? arg?.ToString() : JsonSerializer.Serialize(arg);
            format += $" {text}";
        }

        return string.Join('\n', format.Split('\n').Select(line =>
            line.Length > DefaultMaxLength ? $"{line[..DefaultMaxLength]}..." : line));
    }

    public override string ToString() => $"Logger({Name})";
}

public sealed class LoggerService
{
    private readonly Context _ctx;
    private readonly LoggingSetup _logging;

    internal LoggerService(Context ctx, LoggingSetup logging)
    {
        _ctx = ctx;
        _logging = logging;
    }

    public ILoggerFactory Factory => _logging.Factory;

    public int BufferSize
    {
        get => _logging.Memory.BufferSize;
        set => _logging.Memory.BufferSize = value;
    }

    public IReadOnlyList<Message> Buffer => _logging.Memory.Snapshot();

    public Logger Invoke(string? name = null, Context? caller = null)
    {
        var fiber = caller ?? _ctx;
        name ??= Hyphenate(fiber.Activation.Name);
        return new Logger(name, _logging.Factory.CreateLogger(name));
    }

    public void Error(object? format, params object?[] args) => Invoke().Error(format, args);
    public void Warn(object? format, params object?[] args) => Invoke().Warn(format, args);
    public void Info(object? format, params object?[] args) => Invoke().Info(format, args);
    public void Debug(object? format, params object?[] args) => Invoke().Debug(format, args);

    internal static string Hyphenate(string name)
    {
        var chars = new List<char>(name.Length + 4);
        foreach (var c in name)
        {
            if (char.IsUpper(c) && chars.Count > 0)
            {
                chars.Add('-');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(char.ToLowerInvariant(c));
            }
        }
        return new string([.. chars]);
    }
}
