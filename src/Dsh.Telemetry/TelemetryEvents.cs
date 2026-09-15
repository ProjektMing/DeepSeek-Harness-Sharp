namespace Dsh.Telemetry;

/** 遥测 payload 必须是可源生成序列化的具名类型(AOT 无反射回退),且不承载会话内容。 */
public sealed record TelemetryAgentSession(string SessionId, string Source);

public sealed record TelemetryAgentDisposed(string SessionId);

public sealed record TelemetryAgentFailure(string SessionId, int Turn, int Step, string ErrorType, string ErrorMessage);
