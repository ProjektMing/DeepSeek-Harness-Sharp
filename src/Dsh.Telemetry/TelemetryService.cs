using System.Text.Json;
using Dsh.Llm;

namespace Dsh.Telemetry;

public sealed class TelemetryService : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public TelemetryService(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        _writer = new StreamWriter(outputPath, append: true) { AutoFlush = true };
    }

    public void Record(string eventName, object? payload = null)
    {
        var line = DshJson.Serialize(new TelemetryRecord(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            eventName,
            payload is null ? default : DshJson.ToElementRuntime(payload)));
        _writer.WriteLine(line);
    }

    internal sealed record TelemetryRecord(long Timestamp, string Event, JsonElement Payload);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _writer.Dispose();
    }
}
