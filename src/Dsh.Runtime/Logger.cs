using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    public required object?[] Args { get; init; }
}

internal sealed class LoggerState
{
    public readonly List<Message> Buffer = [];
    public readonly Lock Sync = new();
    public long Sn;
    public int BufferSize = 1000;

    public void Append(Message message)
    {
        lock (Sync)
        {
            Buffer.Add(message);
            var overflow = Buffer.Count - BufferSize;
            if (overflow > 0)
                Buffer.RemoveRange(0, overflow);
        }
    }
}

public sealed class Logger
{
    public const int DefaultMaxLength = 10240;

    private static readonly Regex Placeholder = new("%([a-zA-Z%])", RegexOptions.Compiled);

    public string Name { get; }
    private readonly LoggerState _state;

    internal Logger(string name, LoggerState state)
    {
        Name = name;
        _state = state;
    }

    public void Error(object? format, params object?[] args) => Log(LoggerType.Error, LoggerLevel.Error, format, args);
    public void Warn(object? format, params object?[] args) => Log(LoggerType.Warn, LoggerLevel.Warn, format, args);
    public void Info(object? format, params object?[] args) => Log(LoggerType.Info, LoggerLevel.Info, format, args);
    public void Debug(object? format, params object?[] args) => Log(LoggerType.Debug, LoggerLevel.Debug, format, args);

    private void Log(LoggerType type, int level, object? format, object?[] args)
    {
        var allArgs = format is null ? args : [format, .. args];
        if (allArgs.Length == 1 && allArgs[0] is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                Log(type, level, inner, []);
            return;
        }
        long sn;
        lock (_state.Sync)
            sn = ++_state.Sn;
        _state.Append(new Message
        {
            Sn = sn,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Type = type,
            Level = level,
            Name = Name,
            Args = allArgs,
        });
    }

    public static string Format(Message message)
    {
        var args = message.Args.ToList();
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
        format = Placeholder.Replace(format, match =>
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
    private readonly LoggerState _state;

    internal LoggerService(Context ctx, LoggerState state)
    {
        _ctx = ctx;
        _state = state;
    }

    public int BufferSize
    {
        get => _state.BufferSize;
        set => _state.BufferSize = value;
    }

    public IReadOnlyList<Message> Buffer
    {
        get
        {
            lock (_state.Sync)
                return [.. _state.Buffer];
        }
    }

    public Logger Invoke(string? name = null, Context? caller = null)
    {
        var fiber = caller ?? _ctx;
        name ??= Hyphenate(fiber.Activation.Name);
        return new Logger(name, _state);
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
