namespace Dsh.IdeHistory;

public sealed record IdeHistoryEntry
{
    public required string Provider { get; init; }
    public required string Path { get; init; }
    public required long Timestamp { get; init; }
    public required string Kind { get; init; }
    public string? ContentId { get; init; }
    public string? EntryFile { get; init; }
    public bool HasContent => ContentId is not null || EntryFile is not null;

    public DateTimeOffset Time => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
}

public sealed record IdeHistoryStoreInfo
{
    public required string Provider { get; init; }
    public required string Location { get; init; }
    public required string Retention { get; init; }
}

public sealed record IdeHistoryContent
{
    public required byte[] Bytes { get; init; }
    public required string Origin { get; init; }

    public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
}

public interface IIdeHistoryProvider
{
    string Name { get; }

    bool SupportsContent { get; }

    string? LastReadError { get; }

    IReadOnlyList<IdeHistoryStoreInfo> Discover();

    IReadOnlyList<IdeHistoryEntry> List(IdeHistoryStoreInfo store, string? pathFilter, int limit);

    IdeHistoryContent? Read(IdeHistoryStoreInfo store, IdeHistoryEntry entry);
}
