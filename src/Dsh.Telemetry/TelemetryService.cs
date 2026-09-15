using System.Text.Json;
using Dsh.Llm;
using Dsh.Runtime;

namespace Dsh.Telemetry;

/** 本地遥测服务:把 harness/插件生命周期与错误事件写成 JSONL(不记录会话内容)。 */
public sealed class TelemetryService : Service, IDisposable
{
    public const string ServiceName = "telemetry";

    private readonly StreamWriter? _writer;
    private bool _failed;

    public TelemetryService(Context ctx, string outputPath) : base(ctx, ServiceName)
    {
        // 遥测是可选能力: 文件建不出来/写不进去只记 WARN 并空转, 不能阻断整个 harness 启动。
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            _writer = new StreamWriter(outputPath, append: true) { AutoFlush = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _failed = true;
            Ctx.Logger.Warn("%s", $"telemetry disabled: cannot write {outputPath}: {error.Message}");
        }
    }

    public void AgentSessionStarted(string sessionId, string source)
        => Record("agent/session-start", new TelemetryAgentSession(sessionId, source));

    public void AgentDisposed(string sessionId)
        => Record("agent/disposed", new TelemetryAgentDisposed(sessionId));

    public void AgentFailed(string sessionId, int turn, int step, Exception error)
        => Record("agent/error", new TelemetryAgentFailure(sessionId, turn, step, error.GetType().Name, error.Message));

    /** 只接收具名 payload 类型:运行期类型必须能由 DshTelemetryJsonContext 提供元数据(AOT 无反射回退)。 */
    public void Record<T>(string eventName, T payload)
    {
        if (_failed || _writer is null)
            return;
        try
        {
            var line = DshJson.Serialize(new TelemetryRecord(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                eventName,
                payload is null ? default : DshJson.ToElementRuntime(payload)));
            _writer.WriteLine(line);
        }
        catch (Exception error)
        {
            _failed = true;
            Ctx.Logger.Warn("%s", $"telemetry recording disabled after write failure: {error.Message}");
        }
    }

    internal sealed record TelemetryRecord(long Timestamp, string Event, JsonElement Payload);

    public void Dispose()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
        }
    }
}
