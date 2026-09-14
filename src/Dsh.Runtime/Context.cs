namespace Dsh.Runtime;

public sealed class Context
{
    private static readonly object FilterKey = new();

    private readonly Dictionary<object, object?> _own = new(ReferenceEqualityComparer.Instance);
    private readonly Context? _prototype;
    private readonly List<PluginActivation> _activations;
    private readonly Lock _activationSync;
    private EventsService? _events;
    private LoggerService? _logger;

    public Context Root { get; }
    public PluginActivation Activation { get; }

    internal ServiceTable ServiceTable { get; }
    internal EventTable EventTable { get; }
    internal LoggerState LoggerState { get; }

    public Context()
    {
        Root = this;
        ServiceTable = new ServiceTable();
        EventTable = new EventTable();
        LoggerState = new LoggerState();
        _activations = [];
        _activationSync = new Lock();
        Activation = new PluginActivation("root", [], null, definition: null);
        Activation.Attach(this);
    }

    private Context(Context prototype, PluginActivation? activation = null)
    {
        _prototype = prototype;
        Root = prototype.Root;
        ServiceTable = prototype.ServiceTable;
        EventTable = prototype.EventTable;
        LoggerState = prototype.LoggerState;
        _activations = prototype._activations;
        _activationSync = prototype._activationSync;
        Activation = activation ?? prototype.Activation;
    }

    public Context Extend(params (object Key, object? Value)[] meta)
    {
        var child = new Context(this);
        foreach (var (key, value) in meta)
        {
            child._own[key] = value;
        }
        return child;
    }

    public object? GetProp(object key)
    {
        for (var ctx = this; ctx is not null; ctx = ctx._prototype)
        {
            if (ctx._own.TryGetValue(key, out var value))
                return value;
        }
        return null;
    }

    public void SetOwn(object key, object? value) => _own[key] = value;

    public Context WithFilter(Func<Context, bool> filter) => Extend((FilterKey, filter));

    public Func<Context, bool>? Filter => GetProp(FilterKey) as Func<Context, bool>;

    public EventsService Events => _events ??= new EventsService(this, EventTable);

    public LoggerService Logger => _logger ??= new LoggerService(this, LoggerState);

    public Logger LoggerFor(string? name = null) => Logger.Invoke(name, this);

    public object? Get(string name, bool strict = true) => ServiceTable.Get(name, strict);

    public T? Get<T>(string name, bool strict = true) where T : class => ServiceTable.Get(name, strict) as T;

    public EffectHandle Provide(string name, object? value = null, Func<bool>? check = null)
        => ServiceTable.Provide(this, name, value, check);

    public Func<bool> On(object name, EventListener listener, EventOptions? options = null)
        => Events.On(name, listener, options);

    public void Emit(object name, params object?[] args) => Events.Emit(null, name, args);

    public Task Parallel(object name, params object?[] args) => Events.Parallel(null, name, args);

    public ValueTask<object?> Serial(object name, params object?[] args) => Events.Serial(null, name, args);

    public EffectHandle Effect(Func<object?> execute, string label = "anonymous")
        => Activation.Effect(execute, label);

    public PluginActivation Plugin(PluginDefinition definition, object? config = null)
    {
        var activation = new PluginActivation(definition.Name ?? "anonymous", definition.Inject, config, definition);
        activation.Attach(new Context(this, activation));
        AddActivation(activation);
        activation.TryStart();
        return activation;
    }

    internal bool IsServiceInjectable(string name) => ServiceTable.IsInjectable(name);

    internal void NotifyServiceChanged()
    {
        foreach (var activation in ActivationsSnapshot())
            activation.TryStart();
    }

    internal void AddActivation(PluginActivation activation)
    {
        lock (_activationSync)
            _activations.Add(activation);
    }

    internal void RemoveActivation(PluginActivation activation)
    {
        lock (_activationSync)
            _activations.Remove(activation);
    }

    internal IReadOnlyList<PluginActivation> ActivationsSnapshot()
    {
        lock (_activationSync)
            return [.. _activations];
    }

    public override string ToString() => $"Context <{Activation.Name}>";
}
