namespace Dsh.Runtime;

public sealed record PluginDefinition
{
    public string? Name { get; init; }
    public IReadOnlyList<string> Inject { get; init; } = [];
    public required Func<Context, object?, object?> Apply { get; init; }

    public static PluginDefinition From(
        Func<Context, object?, object?> apply,
        string? name = null,
        IReadOnlyList<string>? inject = null)
    {
        return new PluginDefinition
        {
            Name = name ?? apply.Method.Name,
            Inject = inject ?? [],
            Apply = apply,
        };
    }
}

public enum ActivationState
{
    Pending,
    Active,
    Failed,
    Disposed,
}

public sealed class PluginActivation
{
    private readonly PluginDefinition? _definition;
    private readonly List<string> _provided = [];
    private readonly Lock _sync = new();
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; }
    public IReadOnlyList<string> Inject { get; }
    public object? Config { get; }
    public ActivationState State { get; private set; }
    public object? Error { get; private set; }
    public Context Ctx { get; private set; } = null!;

    internal EffectScope Effects { get; } = new();

    internal PluginActivation(string name, IReadOnlyList<string> inject, object? config, PluginDefinition? definition)
    {
        Name = name;
        Inject = inject;
        Config = config;
        _definition = definition;
        if (definition is null)
        {
            State = ActivationState.Active;
            _settled.SetResult();
        }
    }

    public Task WaitAsync() => _settled.Task;

    public bool IsActive => State == ActivationState.Active;

    public void AssertActive()
    {
        if (State is ActivationState.Failed or ActivationState.Disposed)
            throw new RuntimeException(RuntimeException.InactiveEffect, "cannot create effect on inactive context");
    }

    internal void Attach(Context ctx) => Ctx = ctx;

    internal void TrackProvided(string name)
    {
        lock (_sync)
            _provided.Add(name);
    }

    internal IReadOnlyList<string> ProvidedNames
    {
        get
        {
            lock (_sync)
                return [.. _provided];
        }
    }

    internal EffectHandle Effect(Func<object?> execute, string label)
    {
        AssertActive();
        var handle = EffectScope.Normalize(execute(), label);
        Effects.Add(handle);
        return handle;
    }

    internal void TryStart()
    {
        if (State != ActivationState.Pending || _definition is null)
            return;
        foreach (var name in Inject)
        {
            if (!Ctx.Root.IsServiceInjectable(name))
                return;
        }
        Activate(_definition);
    }

    private void Activate(PluginDefinition definition)
    {
        object? result = null;
        var failed = false;
        try
        {
            result = definition.Apply(Ctx, Config);
        }
        catch (Exception error)
        {
            Error = error;
            failed = true;
            Ctx.Logger.Error("%s", error);
        }
        State = failed ? ActivationState.Failed : ActivationState.Active;
        if (result is not null)
        {
            try
            {
                Effects.Add(EffectScope.Normalize(result, $"apply({Name})"));
            }
            catch (Exception error)
            {
                Error = error;
                State = ActivationState.Failed;
                Ctx.Logger.Error("%s", error);
            }
        }
        _settled.TrySetResult();
        Ctx.Root.NotifyServiceChanged();
    }

    public async Task DeactivateAsync()
    {
        if (State == ActivationState.Disposed)
            return;
        State = ActivationState.Disposed;
        await Effects.DisposeAllAsync(error => Ctx.Logger.Error("%s", error));
        Ctx.Root.NotifyServiceChanged();
        Ctx.Root.RemoveActivation(this);
    }

    public override string ToString() => $"PluginActivation <{Name}> ({State})";
}
