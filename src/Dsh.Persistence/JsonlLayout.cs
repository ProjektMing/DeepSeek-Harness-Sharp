using System.Text;
using Dsh.Core;
using Dsh.Llm;

namespace Dsh.Persistence;

public enum JsonlCompression
{
    Zstd,
    None,
}

public static class JsonlLayout
{
    public static string LogSuffix(JsonlCompression compression)
        => compression == JsonlCompression.Zstd ? ".jsonl.zstd" : ".jsonl";

    public static string EncodeSegment(string raw)
    {
        if (raw.Length == 0) throw new ArgumentException("cannot encode an empty path segment", nameof(raw));
        if (raw == ".") return "~002E";
        if (raw == "..") return "~002E~002E";
        var output = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch != '~' && ProjectStorageKey.IsSafeSegmentChar(ch)) output.Append(ch);
            else output.Append($"~{(int)ch:X4}");
        }
        return output.ToString();
    }

    public static string ProjectDir(string root, string? cwd)
        => Path.Join(root, cwd is null ? "_no-cwd" : ProjectStorageKey.Of(cwd));

    public static string SessionDir(string root, string? cwd, SessionId id)
        => Path.Join(ProjectDir(root, cwd), EncodeSegment(id.Value));

    public static string LogPath(string root, string? cwd, SessionId id, JsonlCompression compression)
        => Path.Join(SessionDir(root, cwd, id), $"session{LogSuffix(compression)}");
}
