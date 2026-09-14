namespace Dsh.Runtime.Logging;

public sealed record LoggingOptions
{
    public const int DefaultBufferSize = 1000;
    public const int DefaultFileMaxBytes = 32 * 1024 * 1024;
    public const int DefaultKeepDays = 15;

    public string? Level { get; init; }
    public int BufferSize { get; init; } = DefaultBufferSize;
    public bool Console { get; init; }
    public bool File { get; init; } = true;
    public int FileMaxBytes { get; init; } = DefaultFileMaxBytes;
    public int KeepDays { get; init; } = DefaultKeepDays;
}
