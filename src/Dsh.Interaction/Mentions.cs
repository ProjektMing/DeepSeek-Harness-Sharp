using System.Text;
using System.Text.RegularExpressions;
using Dsh.Core;

namespace Dsh.Interaction;

public sealed record MentionSessionInfo(string Id, string? Title, string? Summary)
{
    public static MentionSessionInfo FromSnapshot(SessionPersistenceSnapshot snapshot)
        => new(snapshot.Header.Id.Value, snapshot.Header.Title, null);
}

/** `@` 引用的解析: 会话 id/标题优先, 否则按 cwd 匹配文件系统条目; 选中后展开为带边界的文本块。 */
public static partial class MentionResolver
{
    public const int FileContentMaxChars = 2000;
    public const int DirectoryMaxEntries = 20;

    [GeneratedRegex(@"@([^\s@]+)", RegexOptions.Compiled)]
    private static partial Regex MentionPattern();

    public static IReadOnlyList<string> ResolveCandidates(string token, string cwd, IReadOnlyList<MentionSessionInfo> sessions)
    {
        if (string.IsNullOrEmpty(token))
            return [];
        var sessionMatches = sessions
            .Where(session => session.Id.StartsWith(token, StringComparison.OrdinalIgnoreCase)
                || (session.Title?.StartsWith(token, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(session => session.Id)
            .ToList();
        return sessionMatches.Count > 0 ? sessionMatches : ResolveFileSystemCandidates(token, cwd);
    }

    public static string ExpandMentions(string text, string cwd, IReadOnlyList<MentionSessionInfo> sessions)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(cwd))
            return text;
        return MentionPattern().Replace(text, match =>
        {
            var token = match.Groups[1].Value;
            return ExpandToken(token, cwd, sessions) ?? match.Value;
        });
    }

    private static IReadOnlyList<string> ResolveFileSystemCandidates(string token, string cwd)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(cwd, token));
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return [];
            var prefix = Path.GetFileName(fullPath);
            return [.. Directory.EnumerateFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .Where(name => name is not null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => Slash(Path.GetRelativePath(cwd, Path.Combine(directory, name!))))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? ExpandToken(string token, string cwd, IReadOnlyList<MentionSessionInfo> sessions)
    {
        var session = sessions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Title, token, StringComparison.OrdinalIgnoreCase));
        if (session is not null)
            return FormatSession(session);
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(cwd, token));
            if (File.Exists(fullPath))
                return FormatFile(fullPath, cwd);
            if (Directory.Exists(fullPath))
                return FormatDirectory(fullPath, cwd);
        }
        catch (Exception)
        {
            return null;
        }
        return null;
    }

    private static string FormatSession(MentionSessionInfo session)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine($"[session {session.Id}]");
        if (!string.IsNullOrWhiteSpace(session.Title))
            builder.AppendLine($"Title: {session.Title}");
        if (!string.IsNullOrWhiteSpace(session.Summary))
            builder.AppendLine($"Summary: {session.Summary}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatFile(string fullPath, string cwd)
    {
        var relative = Slash(Path.GetRelativePath(cwd, fullPath));
        var content = File.ReadAllText(fullPath);
        if (content.Length > FileContentMaxChars)
            content = content[..FileContentMaxChars];
        return $"\n[file: {relative}]\n{content}\n[/file]";
    }

    private static string FormatDirectory(string fullPath, string cwd)
    {
        var relative = Slash(Path.GetRelativePath(cwd, fullPath));
        var entries = Directory.EnumerateFileSystemEntries(fullPath)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .Take(DirectoryMaxEntries)
            .Select(entry => Slash(Path.GetRelativePath(cwd, entry)));
        return $"\n[directory: {relative}]\n{string.Join('\n', entries)}\n[/directory]";
    }

    private static string Slash(string path)
        => Path.DirectorySeparatorChar == '/'
            ? path
            : path.Replace(Path.DirectorySeparatorChar, '/');
}
