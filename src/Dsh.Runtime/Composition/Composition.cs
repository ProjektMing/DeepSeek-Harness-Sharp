namespace Dsh.Runtime.Composition;

public sealed class Composition
{
    private readonly Context _root;

    private Composition(Context root)
    {
        _root = root;
    }

    public IReadOnlyList<PluginActivation> Activations => _root.ActivationsSnapshot();

    public Context Root => _root;

    public PluginActivation? Find(string packageName)
        => Activations.FirstOrDefault(activation => string.Equals(activation.Name, packageName, StringComparison.Ordinal));

    public static Composition Load(
        Context root,
        string configPath,
        IReadOnlyDictionary<string, PluginDefinition>? builtins,
        Func<string, PluginDefinition?> resolver,
        IReadOnlyList<Dictionary<string, object?>>? patches = null)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new RuntimeException("CONFIG_NOT_FOUND", $"config file not found: {fullPath}");
        var data = YamlConfig.Load(File.ReadAllText(fullPath));
        var entries = ConfigPatches.Apply(
            ConfigEntry.Parse(data),
            patches,
            (message, args) => root.LoggerFor("loader").Warn(message, args));

        foreach (var entry in entries)
        {
            var definition = builtins?.GetValueOrDefault(entry.Name) ?? resolver(entry.Name);
            if (definition is null)
            {
                root.LoggerFor("loader").Error("%s", $"plugin not found: {entry.Name}");
                continue;
            }
            root.Plugin(definition, entry.Config);
        }

        Settle(root);
        var failures = CollectFailures(root);
        if (failures.Count > 0)
        {
            throw new RuntimeException("BOOT_FAILED",
                $"config boot failed with {failures.Count} activation error(s):\n{string.Join('\n', failures)}");
        }
        return new Composition(root);
    }

    public async Task<PluginActivation> AddAsync(PluginDefinition definition, object? config = null)
    {
        var activation = _root.Plugin(definition, config);
        await activation.WaitAsync();
        return activation;
    }

    private static void Settle(Context root)
    {
        for (var pass = 0; pass < 16; pass++)
        {
            var before = root.ActivationsSnapshot().Count(a => a.State != ActivationState.Pending);
            foreach (var activation in root.ActivationsSnapshot())
                activation.TryStart();
            var after = root.ActivationsSnapshot().Count(a => a.State != ActivationState.Pending);
            if (after == before)
                break;
        }
    }

    private static List<string> CollectFailures(Context root)
    {
        var failures = new List<string>();
        foreach (var activation in root.ActivationsSnapshot())
        {
            if (activation.State != ActivationState.Failed)
                continue;
            var message = activation.Error is Exception error ? DeepestMessage(error) : activation.Error?.ToString();
            failures.Add($"  - plugin <{activation.Name}>: {message}");
        }
        foreach (var message in root.Logger.Buffer)
        {
            if (message.Type == LoggerType.Error)
                failures.Add($"  - log <{message.Name}>: {string.Join(' ', message.Args.Select(arg => arg?.ToString()))}");
        }
        return failures;
    }

    private static string DeepestMessage(Exception error)
    {
        while (error.InnerException is not null)
            error = error.InnerException;
        return error.Message;
    }
}
