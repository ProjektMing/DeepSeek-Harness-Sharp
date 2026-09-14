using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Dsh.Runtime.Composition;

public static class YamlConfig
{
    private static readonly Regex IntPattern = new("^[-+]?[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex FloatPattern = new(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.Compiled);

    public static Dictionary<string, object?> LoadMapping(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        if (stream.Documents.Count == 0)
            return [];
        return ConvertYamlNode(stream.Documents[0].RootNode) as Dictionary<string, object?> ?? [];
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
