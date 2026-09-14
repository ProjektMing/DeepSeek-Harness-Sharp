using DryIoc;

namespace Dsh.Runtime.Ioc;

/**
 * IServiceRegistry 的 DryIoc 6.0 实现。
 * - 解释模式(WithUseInterpretation):不生成动态代码,AOT/裁剪发布可用。
 * - 所有实例统一以内部包装类型 ServiceEntry 注册(实例或委托),serviceKey = (name, isolate);
 *   因此仓库自身不做任何类型注册,规避 NativeAOT 全量裁剪下构造函数被裁的问题。
 * - 注销使用 asResolutionCall,避免解析结果被内联缓存。
 */
public sealed class DryIocServiceRegistry : IServiceRegistry, IDisposable
{
    private sealed record ServiceEntry(object? Instance);

    private readonly Container _container;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, List<object?>> _isolates = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, object? Isolate), Type> _entryTypes = [];
    private readonly Dictionary<Type, List<(string Name, object? Isolate)>> _types = [];
    private bool _disposed;

    public DryIocServiceRegistry()
        => _container = new Container(rules => rules.WithUseInterpretation());

    public void Register(string name, object? instance, object? isolateKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
            Track(name, isolateKey, instance);
        _container.RegisterInstance(
            new ServiceEntry(instance),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace,
            setup: Setup.With(asResolutionCall: true),
            serviceKey: (name, isolateKey));
    }

    public void RegisterFactory<T>(string name, Func<IServiceResolver, T> factory, ServiceLifetime lifetime, object? isolateKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync)
            Track(name, isolateKey, null);
        _container.RegisterDelegate<ServiceEntry>(
            context => new ServiceEntry(factory(new DryIocResolver(this, context))),
            reuse: ToReuse(lifetime),
            setup: Setup.With(asResolutionCall: true),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace,
            serviceKey: (name, isolateKey));
    }

    public object? Resolve(string name, object? isolateKey = null)
    {
        if (!IsRegistered(name, isolateKey))
            return null;
        var entry = _container.Resolve<ServiceEntry>(serviceKey: (name, isolateKey), ifUnresolved: IfUnresolved.ReturnDefault);
        return entry?.Instance;
    }

    public IReadOnlyList<object?> ResolveMany(string name) => ResolveManyIn(name, this);

    public bool Unregister(string name, object? isolateKey = null)
    {
        lock (_sync)
        {
            if (!Untrack(name, isolateKey))
                return false;
        }
        _container.Unregister<ServiceEntry>(serviceKey: (name, isolateKey));
        return true;
    }

    public IServiceScope OpenScope(string name) => new DryIocScope(this, _container.OpenScope(name));

    public object? Resolve(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        List<(string Name, object? Isolate)>? entries;
        lock (_sync)
        {
            if (!_types.TryGetValue(serviceType, out entries))
                return null;
            entries = [.. entries];
        }
        foreach (var (name, isolate) in entries)
        {
            var instance = Resolve(name, isolate);
            if (instance is not null)
                return instance;
        }
        return null;
    }

    internal IReadOnlyList<object?> ResolveManyIn(string name, IServiceResolver resolver)
    {
        List<object?> isolates;
        lock (_sync)
        {
            if (!_isolates.TryGetValue(name, out var registered) || registered.Count == 0)
                return [];
            isolates = [.. registered];
        }
        var results = new List<object?>(isolates.Count);
        foreach (var isolate in isolates)
            results.Add(resolver.Resolve(name, isolate));
        return results;
    }

    private bool IsRegistered(string name, object? isolateKey)
    {
        lock (_sync)
            return _isolates.TryGetValue(name, out var isolates) && isolates.Contains(isolateKey);
    }

    private void Track(string name, object? isolateKey, object? instance)
    {
        if (!_isolates.TryGetValue(name, out var isolates))
            _isolates[name] = isolates = [];
        if (!isolates.Contains(isolateKey))
            isolates.Add(isolateKey);
        if (instance is not null)
        {
            var type = instance.GetType();
            _entryTypes[(name, isolateKey)] = type;
            if (!_types.TryGetValue(type, out var entries))
                _types[type] = entries = [];
            if (!entries.Contains((name, isolateKey)))
                entries.Add((name, isolateKey));
        }
    }

    private bool Untrack(string name, object? isolateKey)
    {
        if (!_isolates.TryGetValue(name, out var isolates) || !isolates.Remove(isolateKey))
            return false;
        if (isolates.Count == 0)
            _isolates.Remove(name);
        if (_entryTypes.Remove((name, isolateKey), out var type) && _types.TryGetValue(type, out var entries))
        {
            entries.Remove((name, isolateKey));
            if (entries.Count == 0)
                _types.Remove(type);
        }
        return true;
    }

    private static IReuse ToReuse(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.Singleton => Reuse.Singleton,
        ServiceLifetime.Scoped => Reuse.Scoped,
        _ => Reuse.Transient,
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _container.Dispose();
    }

    private sealed class DryIocResolver(DryIocServiceRegistry registry, IResolverContext context) : IServiceResolver
    {
        public object? Resolve(string name, object? isolateKey = null)
        {
            if (!registry.IsRegistered(name, isolateKey))
                return null;
            var entry = context.Resolve<ServiceEntry>(serviceKey: (name, isolateKey), ifUnresolved: IfUnresolved.ReturnDefault);
            return entry?.Instance;
        }

        public IReadOnlyList<object?> ResolveMany(string name) => registry.ResolveManyIn(name, this);
    }

    private sealed class DryIocScope : IServiceScope
    {
        private readonly IResolverContext _context;
        private readonly DryIocResolver _resolver;

        public DryIocScope(DryIocServiceRegistry registry, IResolverContext context)
        {
            _context = context;
            _resolver = new DryIocResolver(registry, context);
        }

        public object? Resolve(string name, object? isolateKey = null) => _resolver.Resolve(name, isolateKey);

        public IReadOnlyList<object?> ResolveMany(string name) => _resolver.ResolveMany(name);

        public void Dispose() => _context.Dispose();
    }
}
