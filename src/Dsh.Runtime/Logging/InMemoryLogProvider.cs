using Microsoft.Extensions.Logging;

namespace Dsh.Runtime.Logging;

/** 内存环形缓冲:保留最近 N 条日志的格式化文本与元数据,供 TUI/失败聚合读取。 */
public sealed class InMemoryLogProvider : ILoggerProvider
{
    private readonly Lock _sync = new();
    private readonly List<Message> _buffer = [];
    private long _sn;
    private int _bufferSize = LoggingOptions.DefaultBufferSize;

    public int BufferSize
    {
        get
        {
            lock (_sync)
                return _bufferSize;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            lock (_sync)
            {
                _bufferSize = value;
                Trim();
            }
        }
    }

    public IReadOnlyList<Message> Snapshot()
    {
        lock (_sync)
            return [.. _buffer];
    }

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Append(Message message)
    {
        lock (_sync)
        {
            _buffer.Add(message);
            Trim();
        }
    }

    private void Trim()
    {
        var overflow = _buffer.Count - _bufferSize;
        if (overflow > 0)
            _buffer.RemoveRange(0, overflow);
    }

    private sealed class InMemoryLogger(InMemoryLogProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            provider.Append(new Message
            {
                Sn = Interlocked.Increment(ref provider._sn),
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = categoryName,
                Type = LogLevels.ToLoggerType(logLevel),
                Level = LogLevels.ToLoggerLevel(logLevel),
                Text = Redaction.Apply(formatter(state, exception)),
            });
        }
    }
}
