using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Dsh.Boot;

/** settings.yaml 的文本级编辑:只替换 plugins 段,保留其余内容与注释。 */
internal static partial class SettingsDocument
{
    private const string PluginsHeader = "plugins:";
    private const string Indent = "  ";

    private static readonly HashSet<string> ReservedScalars =
        new(["true", "false", "null", "~", "yes", "no", "on", "off"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Za-z0-9_./@\-]+$")]
    private static partial Regex PlainScalar();

    public static string? ExtractPluginsBlock(string text)
    {
        var lines = SplitLines(text);
        var start = FindPluginsStart(lines);
        if (start < 0)
            return null;
        return string.Join('\n', lines[start..BlockEnd(lines, start)]);
    }

    public static string ReplacePluginsBlock(string text, string? block)
    {
        if (block is null)
            return text.TrimEnd() + "\n";
        var lines = SplitLines(text);
        var start = FindPluginsStart(lines);
        if (start < 0)
            return text.TrimEnd() + "\n" + block + "\n";
        var end = BlockEnd(lines, start);
        return string.Join('\n', lines[..start].Concat(SplitLines(block)).Concat(lines[end..])).TrimEnd() + "\n";
    }

    public static string RenderPlugins(IReadOnlyDictionary<string, PluginSetting> plugins)
    {
        var builder = new StringBuilder(PluginsHeader);
        foreach (var (name, setting) in plugins)
        {
            if (setting.Parameters.Count == 0)
            {
                builder.Append('\n').Append(Indent).Append(Quote(name)).Append(": ").Append(Bool(setting.Enabled));
                continue;
            }
            builder.Append('\n').Append(Indent).Append(Quote(name)).Append(':');
            builder.Append('\n').Append(Indent).Append(Indent).Append("enabled: ").Append(Bool(setting.Enabled));
            foreach (var (key, value) in setting.Parameters)
                builder.Append('\n').Append(Indent).Append(Indent).Append(key).Append(": ").Append(FormatScalar(value));
        }
        return builder.ToString();
    }

    private static int FindPluginsStart(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            if (line == PluginsHeader || line.StartsWith(PluginsHeader + " ", StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private static int BlockEnd(string[] lines, int start)
    {
        var end = start + 1;
        while (end < lines.Length && IsBlankOrIndented(lines[end]))
            end++;
        return end;
    }

    private static bool IsBlankOrIndented(string line)
        => line.Length == 0 || line[0] is ' ' or '\t';

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n").Split('\n');

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string name) => $"\"{Escape(name)}\"";

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string FormatScalar(object? value) => value switch
    {
        null => "null",
        bool flag => Bool(flag),
        long number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        string text => FormatString(text),
        _ => FormatString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static string FormatString(string text)
    {
        var plain = text.Length > 0
            && PlainScalar().IsMatch(text)
            && !ReservedScalars.Contains(text)
            && !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        return plain ? text : Quote(text);
    }
}
