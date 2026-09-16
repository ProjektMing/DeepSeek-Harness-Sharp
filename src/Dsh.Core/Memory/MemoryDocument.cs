using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Dsh.Core;

public enum MemoryKind
{
    Fact,
    Decision,
    Constraint,
    Correction,
    Environment,
}

public sealed record MemoryRecord(string Key, string Text, DateTimeOffset? UpdatedAt)
{
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public string Render() => UpdatedAt is { } at
        ? $"- {Key} :: {Text} ({at.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)})"
        : $"- {Key} :: {Text}";
}

/** 项目记忆文件格式:`## 节` 下的 `- key :: text (时间戳)` 记录;非记录行原样保留。 */
public sealed partial class MemoryDocument
{
    public sealed record Entry(string? RawLine, MemoryRecord? Record);

    public sealed class Section(string name)
    {
        public string Name { get; } = name;

        public List<Entry> Entries { get; } = [];

        public IEnumerable<MemoryRecord> Records => Entries.Select(entry => entry.Record).OfType<MemoryRecord>();
    }

    public List<string> Preamble { get; } = [];

    public List<Section> Sections { get; } = [];

    public static MemoryKind KindOf(string sectionName)
    {
        var name = sectionName.ToLowerInvariant();
        if (name.Contains("correction"))
            return MemoryKind.Correction;
        if (name.Contains("decision"))
            return MemoryKind.Decision;
        if (name.Contains("constraint"))
            return MemoryKind.Constraint;
        if (name.Contains("command") || name.Contains("environment"))
            return MemoryKind.Environment;
        return MemoryKind.Fact;
    }

    public Section? FindSection(string name)
        => Sections.FirstOrDefault(section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase));

    public Section EnsureSection(string name)
    {
        var existing = FindSection(name);
        if (existing is not null)
            return existing;
        var created = new Section(name);
        Sections.Add(created);
        return created;
    }

    /** 同节内 key 相同(忽略大小写)的记录就地替换,否则追加到节尾;返回是否发生了替换。 */
    public bool Upsert(string sectionName, MemoryRecord record)
    {
        var section = EnsureSection(sectionName);
        for (var index = 0; index < section.Entries.Count; index++)
        {
            if (section.Entries[index].Record is { } existing
                && string.Equals(existing.Key, record.Key, StringComparison.OrdinalIgnoreCase))
            {
                section.Entries[index] = new Entry(null, record);
                return true;
            }
        }
        section.Entries.Add(new Entry(null, record));
        return false;
    }

    /** 跨全部节按 key(忽略大小写)删除第一条匹配记录;返回被删记录所在节名,未找到返回 null。 */
    public string? Remove(string key)
    {
        foreach (var section in Sections)
        {
            for (var index = 0; index < section.Entries.Count; index++)
            {
                if (section.Entries[index].Record is { } record
                    && string.Equals(record.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    section.Entries.RemoveAt(index);
                    return section.Name;
                }
            }
        }
        return null;
    }

    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var line in Preamble)
            builder.AppendLine(line);
        foreach (var section in Sections)
        {
            if (builder.Length > 0 && !EndsWithBlankLine(builder))
                builder.AppendLine();
            builder.Append("## ").AppendLine(section.Name);
            foreach (var entry in section.Entries)
                builder.AppendLine(entry.Record?.Render() ?? entry.RawLine ?? "");
        }
        return builder.ToString();
    }

    public static MemoryDocument Parse(string? text)
    {
        var doc = new MemoryDocument();
        if (string.IsNullOrWhiteSpace(text))
            return doc;
        Section? current = null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = new Section(line[3..].Trim());
                doc.Sections.Add(current);
                continue;
            }
            if (current is null)
            {
                doc.Preamble.Add(rawLine.TrimEnd());
                continue;
            }
            if (RecordLine().Match(line) is { Success: true } match)
            {
                current.Entries.Add(new Entry(null, new MemoryRecord(
                    match.Groups["key"].Value,
                    match.Groups["text"].Value,
                    ParseTimestamp(match.Groups["ts"]))));
                continue;
            }
            current.Entries.Add(new Entry(rawLine.TrimEnd(), null));
        }
        return doc;
    }

    private static DateTimeOffset? ParseTimestamp(Group group)
        => group.Success
        && DateTimeOffset.TryParseExact(
            group.Value,
            MemoryRecord.TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var at)
            ? at
            : null;

    private static bool EndsWithBlankLine(StringBuilder builder)
    {
        var newlines = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
            newlines++;
        return newlines >= 2;
    }

    [GeneratedRegex(@"^- (?<key>.*?) :: (?<text>.*?)(?: \((?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)\))?$", RegexOptions.Compiled)]
    private static partial Regex RecordLine();
}
