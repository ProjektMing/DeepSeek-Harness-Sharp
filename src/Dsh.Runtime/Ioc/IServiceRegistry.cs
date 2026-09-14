namespace Dsh.Runtime.Ioc;

public enum ServiceLifetime
{
    Singleton,
    Transient,
    Scoped,
}

public interface IServiceResolver
{
    /** 未注册或当前作用域不可用时返回 null。 */
    object? Resolve(string name, object? isolateKey = null);

    /** 返回同一服务名在各 isolate 下注册的全部实例。 */
    IReadOnlyList<object?> ResolveMany(string name);
}

public interface IServiceScope : IServiceResolver, IDisposable
{
}

/**
 * L0 服务仓库端口:插件与调用方只见此接口,不见具体容器。
 * 约定(AOT/裁剪约束):实现以实例与委托注册为主,不做类型注册——NativeAOT 全量裁剪会裁掉构造函数,
 * 类型注册在 AOT 发布下会因无法选择构造函数而失败;委托注册与实例注册在所有发布形态下均可用。
 */
public interface IServiceRegistry : IServiceResolver
{
    /** 注册实例;同名同 isolate 重复注册时覆盖。isolateKey 为 null 表示根 isolate。 */
    void Register(string name, object? instance, object? isolateKey = null);

    void RegisterFactory<T>(string name, Func<IServiceResolver, T> factory, ServiceLifetime lifetime, object? isolateKey = null);

    bool Unregister(string name, object? isolateKey = null);

    IServiceScope OpenScope(string name);

    /** 供 System.IServiceProvider 只读视图使用:按实例的精确类型查找,找不到返回 null。 */
    object? Resolve(Type serviceType);
}
