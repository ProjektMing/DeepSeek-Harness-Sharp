namespace Dsh.Runtime.Ioc;

/** IServiceRegistry 的只读 IServiceProvider 视图,供第三方库/生态互操作按类型查找已注册实例。 */
public sealed class ServiceProviderView(IServiceRegistry registry) : IServiceProvider
{
    public object? GetService(Type serviceType) => registry.Resolve(serviceType);
}
