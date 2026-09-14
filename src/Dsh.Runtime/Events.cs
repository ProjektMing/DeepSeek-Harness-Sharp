namespace Dsh.Runtime;

public delegate ValueTask<object?> EventListener(object? thisArg, object?[] args);

public sealed record EventOptions
{
    public bool Prepend { get; init; }
    public bool Global { get; init; }
}

public static class EventNames
{
    public const string Service = "internal/service";
    public const string Update = "internal/update";
}

internal sealed class EventTable
{
    internal readonly Dictionary<object, List<Hook>> Hooks = [];
    internal readonly Lock Sync = new();

    internal int Register(object name, Hook hook)
    {
        lock (Sync)
        {
            if (!Hooks.TryGetValue(name, out var hooks))
                hooks = Hooks[name] = [];
            if (hook.Options.Prepend)
                hooks.Insert(0, hook);
            else
                hooks.Add(hook);
            return hooks.Count;
        }
    }

    internal void Unregister(object name, EventListener callback)
    {
        lock (Sync)
        {
            if (!Hooks.TryGetValue(name, out var hooks))
                return;
            var index = hooks.FindIndex(hook => ReferenceEquals(hook.Callback, callback));
            if (index < 0)
                return;
            hooks.RemoveAt(index);
            if (hooks.Count == 0)
                Hooks.Remove(name);
        }
    }

    internal List<EventListener> Resolve(object name, Func<Context, bool>? filter)
    {
        List<Hook>? hooks;
        lock (Sync)
        {
            hooks = Hooks.TryGetValue(name, out var found) ? [.. found] : null;
        }
        if (hooks is null)
            return [];
        return hooks
            .Where(hook => hook.Options.Global || filter is null || filter(hook.Ctx))
            .Select(hook => hook.Callback)
            .ToList();
    }
}

internal sealed record Hook(Context Ctx, EventListener Callback, EventOptions Options);

public sealed class EventsService
{
    private readonly Context _owner;
    private readonly EventTable _table;

    internal EventsService(Context owner, EventTable table)
    {
        _owner = owner;
        _table = table;
    }

    public static bool IsBailed(object? value) => value is not null and not false;

    public Func<bool> On(object name, EventListener listener, EventOptions? options = null)
    {
        var resolved = options ?? new EventOptions();
        _owner.Activation.AssertActive();
        var hook = new Hook(_owner, listener, resolved);
        _table.Register(name, hook);
        var effect = new EffectHandle($"on({name})", () =>
        {
            _table.Unregister(name, listener);
            return Task.CompletedTask;
        });
        _owner.Activation.Effects.Add(effect);
        return () =>
        {
            effect.Dispose();
            return true;
        };
    }

    public void Emit(object? thisArg, object name, params object?[] args)
    {
        foreach (var callback in Resolve(thisArg, name))
        {
            InvokeObserved(callback, thisArg, args);
        }
    }

    public async Task Parallel(object? thisArg, object name, params object?[] args)
    {
        var callbacks = Resolve(thisArg, name);
        var results = await Task.WhenAll(callbacks.Select(InvokeSafely));
        var errors = results.Where(error => error is not null).Cast<Exception>().ToList();
        if (errors.Count > 0)
            throw new AggregateException(errors);

        async Task<Exception?> InvokeSafely(EventListener callback)
        {
            try
            {
                await callback(thisArg, args);
                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }
    }

    public async ValueTask<object?> Serial(object? thisArg, object name, params object?[] args)
    {
        foreach (var callback in Resolve(thisArg, name))
        {
            var result = await callback(thisArg, args);
            if (IsBailed(result))
                return result;
        }
        return null;
    }

    public async ValueTask<object?> Waterfall(object? thisArg, object name, object?[] args, Func<ValueTask<object?>> inner)
    {
        var callbacks = Resolve(thisArg, name);
        var index = 0;
        async ValueTask<object?> Dispatch()
        {
            if (index >= callbacks.Count)
                return await inner();
            var callback = callbacks[index++];
            var called = false;
            ValueTask<object?> Next()
            {
                if (called)
                    throw new InvalidOperationException("next() called multiple times");
                called = true;
                return Dispatch();
            }
            return await callback(thisArg, [.. args, (Func<ValueTask<object?>>)Next]);
        }
        return await Dispatch();
    }

    private List<EventListener> Resolve(object? thisArg, object name)
    {
        var filter = (thisArg as Context)?.Filter;
        return _table.Resolve(name, filter);
    }

    private void InvokeObserved(EventListener callback, object? thisArg, object?[] args)
    {
        try
        {
            var task = callback(thisArg, args);
            if (!task.IsCompleted)
                _ = Observe(task);
            else
                task.GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            _owner.Root.Logger.Error("%s", error);
        }
    }

    private async Task Observe(ValueTask<object?> task)
    {
        try
        {
            await task;
        }
        catch (Exception error)
        {
            _owner.Root.Logger.Error("%s", error);
        }
    }
}
