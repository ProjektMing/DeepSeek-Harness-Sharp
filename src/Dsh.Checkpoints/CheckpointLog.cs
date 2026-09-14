using System.Text.Json;
using System.Text.Json.Serialization;
using Dsh.Llm;

namespace Dsh.Checkpoints;

public sealed record CheckpointPoint
{
    [JsonPropertyName("ts")]
    public required long Timestamp { get; init; }

    [JsonPropertyName("commit")]
    public required string Commit { get; init; }

    [JsonPropertyName("seq")]
    public required long Seq { get; init; }

    [JsonPropertyName("session")]
    public required string Session { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    public DateTimeOffset Time => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
}

public sealed class CheckpointLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly List<CheckpointPoint> _points = [];
    private readonly Lock _sync = new();

    public CheckpointLog(string path)
    {
        _path = path;
        Load();
    }

    public string Path => _path;

    public IReadOnlyList<CheckpointPoint> Points
    {
        get
        {
            lock (_sync)
                return [.. _points];
        }
    }

    public void Append(CheckpointPoint point)
    {
        lock (_sync)
        {
            _points.Add(point);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, JsonSerializer.Serialize(point, Options) + "\n");
        }
    }

    public IReadOnlyList<CheckpointPoint> Prune(int maxPoints, int keepDays, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-keepDays).ToUnixTimeMilliseconds();
        List<CheckpointPoint> removed;
        lock (_sync)
        {
            var keep = _points
                .Where(point => point.Timestamp >= cutoff)
                .ToList();
            if (keep.Count > maxPoints)
                keep = keep[^maxPoints..];
            removed = _points.Where(point => !keep.Contains(point)).ToList();
            if (removed.Count == 0)
                return [];
            _points.Clear();
            _points.AddRange(keep);
            Rewrite();
        }
        return removed;
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        foreach (var line in File.ReadLines(_path))
        {
            if (line.Trim().Length == 0)
                continue;
            try
            {
                if (JsonSerializer.Deserialize<CheckpointPoint>(line, Options) is { } point)
                    _points.Add(point);
            }
            catch (JsonException)
            {
            }
        }
    }

    private void Rewrite()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var text = string.Join('\n', _points.Select(point => JsonSerializer.Serialize(point, Options)));
        File.WriteAllText(_path, text.Length == 0 ? "" : text + "\n");
    }

    public static string FormatPoint(int index, CheckpointPoint point)
        => $"[{index}] {point.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss} seq={point.Seq} ({point.Reason}) {Shorten(point.Commit)} {point.Session}";

    public static string Shorten(string commit) => commit.Length <= 8 ? commit : commit[..8];

    public static string SessionLabel(SessionId id) => id.Value;
}
