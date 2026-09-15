namespace Dsh.Plugins;

/** 生成目录在模块初始化时自注册的落点;宿主从这里取得镜像内插件的登记项。 */
public static class DshPluginCatalogRegistry
{
    private static readonly List<DshPluginRegistration> Entries = [];
    private static readonly Lock Gate = new();

    public static void Register(DshPluginRegistration registration)
    {
        lock (Gate)
        {
            if (Entries.Any(entry => entry.Package == registration.Package))
                return;
            Entries.Add(registration);
        }
    }

    public static IReadOnlyList<DshPluginRegistration> Snapshot()
    {
        lock (Gate)
            return [.. Entries];
    }
}
