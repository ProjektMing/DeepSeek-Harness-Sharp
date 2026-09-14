using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Dsh.Runtime.Composition;

public static class YamlConfig
{
    private static readonly Regex IntPattern = new("^[-+]?[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex FloatPattern = new(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.Compiled);

    public static List<object?> Load(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        if (stream.Documents.Count == 0)
            return [];
        return ConvertYamlNode(stream.Documents[0].RootNode) as List<object?> ?? [];
    }

    private static object? ConvertYamlNode(YamlNode node)
    {
        switch (node)
        {
            case null:
                return null;
            case YamlScalarNode scalar:
                return ParseScalar(scalar.Value ?? "");
            case YamlMappingNode mapping:
                var mapped = new Dictionary<string, object?>();
                foreach (var (key, value) in mapping.Children)
                {
                    mapped[ConvertScalarKey(key)] = ConvertYamlNode(value);
                }
                return mapped;
            case YamlSequenceNode sequence:
                return sequence.Children.Select(ConvertYamlNode).ToList();
            default:
                return node.ToString();
        }
    }

    private static string ConvertScalarKey(YamlNode key)
        => key is YamlScalarNode scalar ? scalar.Value ?? "" : key.ToString();

    private static object? ParseScalar(string value)
    {
        if (value is "null" or "~" or "") return null;
        if (value == "true") return true;
        if (value == "false") return false;
        if (IntPattern.IsMatch(value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }
        if (FloatPattern.IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            return floating;
        }
        return value;
    }
}

public sealed class ConfigEntry
{
    public string? Id { get; set; }
    public required string Name { get; set; }
    public object? Config { get; set; }

    public static List<ConfigEntry> Parse(List<object?> data)
    {
        var entries = new List<ConfigEntry>();
        foreach (var item in data)
        {
            if (item is not IDictionary<string, object?> dict)
                continue;
            if (dict.GetOrNull("name") is not string name || name.Length == 0)
                continue;
            if (dict.GetOrNull("disabled") is true)
                continue;
            entries.Add(new ConfigEntry
            {
                Id = dict.GetOrNull("id") as string,
                Name = name,
                Config = dict.GetOrNull("config"),
            });
        }
        return entries;
    }

    public static List<ConfigEntry> Parse(IEnumerable<Dictionary<string, object?>> data)
        => Parse(data.Cast<object?>().ToList());
}

internal static class DictionaryExtensions
{
    public static object? GetOrNull(this IDictionary<string, object?> dict, string key)
        => dict.TryGetValue(key, out var value) ? value : null;
}

public static class ConfigPatches
{
    public static List<ConfigEntry> Apply(
        List<ConfigEntry> data,
        IReadOnlyList<Dictionary<string, object?>>? patches,
        Action<string, object?[]> warn)
    {
        if (patches is null || patches.Count == 0)
            return data;

        var entryMap = new Dictionary<string, ConfigEntry>();
        foreach (var entry in data)
        {
            if (entry.Id is not null)
                entryMap[entry.Id] = entry;
        }

        foreach (var patch in patches)
        {
            var id = patch.GetOrNull("id") as string;
            var insert = (patch.GetOrNull("insert") as List<object?>) is { } list
                ? ConfigEntry.Parse(list)
                : null;
            var name = patch.GetOrNull("name") as string;

            if (insert is not null)
            {
                if (id is not null)
                {
                    if (!entryMap.ContainsKey(id))
                    {
                        warn("patch insert: entry %s not found", [id]);
                        continue;
                    }
                    warn("patch insert: nested groups are not supported, entries appended at top level for %s", [id]);
                }
                data.AddRange(insert);
                continue;
            }

            if (id is null)
            {
                warn("patch: id is required for non-insert patches", []);
                continue;
            }

            if (!entryMap.TryGetValue(id, out var entry))
            {
                warn("patch: entry %s not found", [id]);
                continue;
            }

            if (name is not null && name != entry.Name)
            {
                warn("patch: name mismatch for %s (expected %s, got %s), skipping", [id, entry.Name, name]);
                continue;
            }

            foreach (var (key, value) in patch)
            {
                if (key is "id" or "insert" or "name")
                    continue;
                if (key == "config")
                    entry.Config = value;
            }
        }

        return data;
    }
}
