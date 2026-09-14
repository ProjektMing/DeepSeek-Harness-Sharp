namespace Dsh.Runtime;

public sealed class EffectHandle
{
    private Func<Task>? _dispose;
    private bool _active = true;

    public string Label { get; }
    public bool IsActive => _active;

    internal EffectHandle(string label, Func<Task> dispose)
    {
        Label = label;
        _dispose = dispose;
    }

    public async Task DisposeAsync()
    {
        if (!_active) return;
        _active = false;
        var dispose = _dispose;
        _dispose = null;
        if (dispose is not null)
            await dispose();
    }

    public void Dispose() => _ = Observe(DisposeAsync());

    private static async Task Observe(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // dispose 失败不应成为未处理异常
        }
    }
}

internal sealed class EffectScope
{
    private readonly List<EffectHandle> _effects = [];
    private readonly Lock _sync = new();

    public void Add(EffectHandle handle)
    {
        lock (_sync)
            _effects.Add(handle);
    }

    public async Task DisposeAllAsync(Action<Exception>? onError = null)
    {
        List<EffectHandle> items;
        lock (_sync)
        {
            items = [.. _effects];
            _effects.Clear();
        }
        items.Reverse();
        foreach (var item in items)
        {
            try
            {
                await item.DisposeAsync();
            }
            catch (Exception error)
            {
                onError?.Invoke(error);
            }
        }
    }

    internal static EffectHandle Normalize(object? result, string label)
    {
        return result switch
        {
            null => new EffectHandle(label, () => Task.CompletedTask),
            EffectHandle handle => handle,
            Action action => new EffectHandle(label, () =>
            {
                action();
                return Task.CompletedTask;
            }),
            Func<Task> asyncDispose => new EffectHandle(label, asyncDispose),
            IDisposable disposable => new EffectHandle(label, new RawDisposable(disposable).DisposeAsync),
            IEnumerable<object?> items => new EffectHandle(label, () => DisposeMany(items, label)),
            _ => throw new RuntimeException("INVALID_EFFECT", $"invalid effect result from {label}: {result.GetType().Name}"),
        };
    }

    /** 包一层宿主类型:Dispose 时立即丢弃对插件对象的引用,避免协作式卸载时插件程序集被滞后引用。 */
    private sealed class RawDisposable(IDisposable target) : IDisposable
    {
        private IDisposable? _target = target;

        public void Dispose()
        {
            var current = _target;
            _target = null;
            current?.Dispose();
        }

        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }
    }

    private static async Task DisposeMany(IEnumerable<object?> items, string label)
    {
        foreach (var item in items)
        {
            await Normalize(item, label).DisposeAsync();
        }
    }
}
