using Dsh.Runtime;

namespace Dsh.Plugins;

public interface IDshPlugin
{
    string[] Inject { get; }
    IDisposable Apply(Context ctx, object? config);
}

public interface IPluginManager
{
    IReadOnlyList<string> PackageNames { get; }

    bool SupportsDynamicLoad { get; }

    string Describe(string package);

    Task<string> AddAsync(string packageOrPath);

    Task<string> RemoveAsync(string package, bool force = false);

    Task<string> DisableAsync(string package);

    Task<string> EnableAsync(string package);
}
