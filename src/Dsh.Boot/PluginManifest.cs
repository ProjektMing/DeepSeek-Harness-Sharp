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

/** 插件清单:settings.yaml 的 plugins 段与 Profiles/Templates/plugins.yaml 默认清单。 */
public static class PluginManifest
{
    public const string PluginsKey = "plugins";
    private const string DefaultTemplateFile = "plugins.yaml";

    public static Dictionary<string, PluginSetting> Parse(object? raw)
    {
        if (raw is not IDictionary<string, object?> map)
            return new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
        var plugins = new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
        foreach (var (name, value) in map)
            plugins[name] = PluginSetting.FromRaw(value);
        return plugins;
    }

    public static Dictionary<string, PluginSetting> ParseYaml(string content)
    {
        var root = YamlConfig.LoadMapping(content);
        return root.TryGetValue(PluginsKey, out var raw) ? Parse(raw) : new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
    }

    public static Dictionary<string, PluginSetting> LoadDefaults(string? baseDirectory = null)
    {
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Profiles", "Templates", DefaultTemplateFile);
        return File.Exists(path) ? ParseYaml(File.ReadAllText(path)) : new Dictionary<string, PluginSetting>(StringComparer.Ordinal);
    }

    /** 默认清单为底,用户 settings.yaml 的值优先。 */
    public static Dictionary<string, PluginSetting> Merge(
        IReadOnlyDictionary<string, PluginSetting> defaults,
        IReadOnlyDictionary<string, PluginSetting> user)
    {
        var merged = new Dictionary<string, PluginSetting>(defaults, StringComparer.Ordinal);
        foreach (var (name, setting) in user)
            merged[name] = setting;
        return merged;
    }

    /** 目录扫描发现、清单未列出的插件默认启用;用户已显式配置的保持不变。 */
    public static void IncludeDiscovered(
        Dictionary<string, PluginSetting> plugins,
        IReadOnlyList<string> discovered)
    {
        foreach (var package in discovered)
            plugins.TryAdd(package, new PluginSetting());
    }
}
