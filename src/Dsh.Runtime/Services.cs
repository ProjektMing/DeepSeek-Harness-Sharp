namespace Dsh.Runtime;

internal sealed class ServiceImpl
{
    public required string Name { get; init; }
    public object? Value { get; set; }
    public Func<bool>? Check { get; init; }
    public required PluginActivation Owner { get; init; }
}

internal sealed class ServiceTable
{
    private readonly Dictionary<string, ServiceImpl> _services = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    public object? Get(string name, bool strict)
    {
        ServiceImpl? impl;
        lock (_sync)
            _services.TryGetValue(name, out impl);
        if (impl is null)
            return null;
        if (strict && impl.Owner.State != ActivationState.Active)
            return null;
        return impl.Value;
    }

    public bool IsInjectable(string name)
    {
        ServiceImpl? impl;
        lock (_sync)
            _services.TryGetValue(name, out impl);
        if (impl is null || impl.Owner.State != ActivationState.Active)
            return false;
        if (impl.Check is null)
            return true;
        try
        {
            return impl.Check();
        }
        catch (Exception error)
        {
            impl.Owner.Ctx.Logger.Error("%s", error);
            return false;
        }
    }

    public EffectHandle Provide(Context ctx, string name, object? value, Func<bool>? check)
    {
        var impl = new ServiceImpl { Name = name, Value = value, Check = check, Owner = ctx.Activation };
        lock (_sync)
        {
            if (_services.TryGetValue(name, out var occupied))
                throw new RuntimeException("SERVICE_REGISTERED", $"service \"{name}\" has been registered at <{occupied.Owner.Name}>");
            _services[name] = impl;
        }
        ctx.Activation.TrackProvided(name);
        var effect = new EffectHandle($"provide({name})", () =>
        {
            lock (_sync)
                _services.Remove(name);
            ctx.Root.NotifyServiceChanged();
            return Task.CompletedTask;
        });
        ctx.Activation.Effects.Add(effect);
        if (ctx.Activation.State == ActivationState.Active)
            ctx.Root.NotifyServiceChanged();
        return effect;
    }

    public void Set(string name, object? value)
    {
        ServiceImpl? impl;
        lock (_sync)
            _services.TryGetValue(name, out impl);
        if (impl is null)
            throw new RuntimeException("NOT_PROVIDED", $"cannot set property \"{name}\" without provide");
        impl.Value = value;
    }
}
