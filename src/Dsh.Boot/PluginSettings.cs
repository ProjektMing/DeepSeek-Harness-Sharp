using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Boot;

/** settings.yaml 的 plugins 条目:布尔开关或 {enabled, ...参数} 形式。 */
public sealed record PluginSetting
{
    public const string EnabledKey = "enabled";

    public bool Enabled { get; init; } = true;
    public Dictionary<string, object?> Parameters { get; init; } = [];

    public static PluginSetting FromRaw(object? raw) => raw switch
    {
        null => new PluginSetting(),
        bool enabled => new PluginSetting { Enabled = enabled },
        IDictionary<string, object?> map => FromMap(map),
        _ => throw new RuntimeException("INVALID_PLUGIN_SETTING",
            $"plugin setting must be a boolean or a mapping, got {raw.GetType().Name}"),
    };

    private static PluginSetting FromMap(IDictionary<string, object?> map)
    {
        var enabled = true;
        if (map.TryGetValue(EnabledKey, out var value) && value is not null)
        {
            enabled = value is bool flag
                ? flag
                : throw new RuntimeException("INVALID_PLUGIN_SETTING", $"plugins.<name>.{EnabledKey} must be a boolean");
        }
        var parameters = new Dictionary<string, object?>(map, StringComparer.Ordinal);
        parameters.Remove(EnabledKey);
        return new PluginSetting { Enabled = enabled, Parameters = parameters };
    }
}

/** settings.yaml 的 plugins 段:用户在此禁用插件或保存参数;未列出的插件发现即启用。 */
public static class PluginSettingsSection
{
    public const string PluginsKey = "plugins";

    public static Dictionary<string, PluginSetting> ParseDocument(string yaml)
    {
        var root = YamlConfig.LoadMapping(yaml);
        return root.TryGetValue(PluginsKey, out var raw) ? Parse(raw) : new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
    }

    private static Dictionary<string, PluginSetting> Parse(object? raw)
    {
        if (raw is not IDictionary<string, object?> map)
            return new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
        var plugins = new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
        foreach (var (name, value) in map)
            plugins[name] = PluginSetting.FromRaw(value);
        return plugins;
    }
}
