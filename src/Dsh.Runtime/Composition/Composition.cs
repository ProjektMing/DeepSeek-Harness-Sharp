using Dsh.Runtime.Events;

namespace Dsh.Runtime.Composition;

public sealed record PluginEntry(PluginDefinition Definition, object? Config);

public sealed class Composition
{
    private readonly Context _root;

    private Composition(Context root)
    {
        _root = root;
    }

    public IReadOnlyList<PluginActivation> Activations => _root.Scheduler.Snapshot();

    public Context Root => _root;

    public PluginActivation? Find(string packageName)
        => Activations.FirstOrDefault(activation => string.Equals(activation.Name, packageName, StringComparison.Ordinal));

    public static async Task<Composition> StartAsync(Context root, IReadOnlyList<PluginEntry> entries)
    {
        foreach (var entry in entries)
            root.Scheduler.Register(entry.Definition, entry.Config);

        await root.Scheduler.SettleAsync();
        var failures = CollectFailures(root);
        if (failures.Count > 0)
        {
            throw new RuntimeException("BOOT_FAILED",
                $"boot failed with {failures.Count} activation error(s):\n{string.Join('\n', failures)}");
        }
        root.Emit(new CompositionReadyNotification());
        return new Composition(root);
    }

    public async Task<PluginActivation> AddAsync(PluginDefinition definition, object? config = null)
        => await _root.Scheduler.AddAsync(definition, config);

    /** 关闭时释放所有插件的效果:编译进镜像的插件不经过协作式卸载,文件句柄/订阅这类资源必须在这里放掉。 */
    public void DeactivateAll()
    {
        foreach (var activation in Activations)
            activation.DisposeEffectsAsync().GetAwaiter().GetResult();
    }

    private static List<string> CollectFailures(Context root)
    {
        var failures = new List<string>();
        foreach (var activation in root.Scheduler.Snapshot())
        {
            if (activation.State != ActivationState.Failed)
                continue;
            failures.Add($"  - plugin <{activation.Name}>: {activation.Error}");
        }
        foreach (var message in root.Logger.Buffer)
        {
            if (message.Type == LoggerType.Error)
                failures.Add($"  - log <{message.Name}>: {message.Text}");
        }
        return failures;
    }
}
