using Dsh.Runtime;
using Dsh.Runtime.Composition;
using Dsh.Plugins;

namespace Dsh.Boot;

public sealed record HarnessOptions(
    HarnessHome Home,
    string? Cwd = null,
    string? Provider = null,
    string? Model = null,
    string? BaseUrl = null,
    string? ApiKeyEnv = null,
    string? ApiKey = null,
    string? ReasoningEffort = null,
    bool IsTui = false,
    string? EntrypointPlugin = null);

public sealed class HarnessApp : IDisposable
{
    public required Context Ctx { get; init; }
    public required HarnessHome Home { get; init; }
    public required ICredentials Credentials { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string? ReasoningEffort { get; init; }
    public Composition? Composition { get; set; }

    private readonly List<IDisposable> _disposables = [];

    internal void Track(IDisposable disposable) => _disposables.Add(disposable);

    /** 运行入口插件:按描述符 Entry 找到登记项,工厂实例须实现 IDshEntrypoint。 */
    public async Task<int> RunEntrypointAsync(
        string name,
        PluginEntrypointOptions options,
        CancellationToken cancellationToken = default)
    {
        var catalog = Ctx.GetProp("pluginCatalog") as PluginCatalog
            ?? throw new RuntimeException("PLUGIN_CATALOG_MISSING", "the plugin catalog is not available");
        var descriptor = catalog.Descriptors.FirstOrDefault(entry => entry.Entry == name)
            ?? throw new RuntimeException("UNKNOWN_ENTRYPOINT", $"unknown plugin entrypoint '{name}'");
        if (!catalog.TryGet(descriptor.Package, out var create) || create() is not IDshEntrypoint entrypoint)
        {
            throw new RuntimeException("ENTRYPOINT_NOT_RUNNABLE",
                $"plugin package '{descriptor.Package}' does not implement the entrypoint contract");
        }
        return await entrypoint.RunAsync(this, options, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var disposable in ((IEnumerable<IDisposable>)_disposables).Reverse())
            disposable.Dispose();
    }
}

public static class HarnessComposer
{
    public static async Task<HarnessApp> Compose(HarnessOptions options)
        => await ConfigBoot.Compose(options);
}

