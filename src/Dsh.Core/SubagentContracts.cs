using System.Text.Json;
using System.Text.Json.Nodes;
using Dsh.Llm;

namespace Dsh.Core;

public enum SubagentStopReason
{
    Completed,
    Aborted,
    Error,
    MaxTokens,
    Refusal,
}

public static class SubagentStopReasonWire
{
    public static string Of(SubagentStopReason reason) => reason switch
    {
        SubagentStopReason.Completed => "completed",
        SubagentStopReason.Aborted => "aborted",
        SubagentStopReason.Error => "error",
        SubagentStopReason.MaxTokens => "max-tokens",
        SubagentStopReason.Refusal => "refusal",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}

public sealed record SubagentCapabilities(
    bool AgentOptions = false,
    bool OutputSchema = false,
    bool DepthLimit = false,
    bool ToolFilter = false,
    bool Persona = false);

public sealed record SubagentResult
{
    public required IReadOnlyList<ContentBlock> Output { get; init; }
    public required SubagentStopReason StopReason { get; init; }
    public JsonElement? Structured { get; init; }
    public string? Diagnostic { get; init; }
}

public interface ISubagentRun
{
    SessionId Id { get; }
    IAgent? LocalAgent { get; }
    Task<SubagentResult> Result { get; }
    Task DisposeAsync();
}

public sealed record SubagentStartRequest
{
    public string? Label { get; init; }
    public required IReadOnlyList<ContentBlock> Prompt { get; init; }
    public required IAgent Parent { get; init; }
    public required CancellationToken Signal { get; init; }
    public AgentOptions? AgentOptions { get; init; }
    public JsonObject? OutputSchema { get; init; }
    public int? MaxDepth { get; init; }
    public ToolRestriction? ToolFilter { get; init; }
    public string? Persona { get; init; }
}

/** 子代理 provider 的能力视图:供消费方在不接触 provider SPI 的情况下探测能力。 */
public sealed record SubagentProviderInfo(SubagentCapabilities Capabilities, bool InheritsParentContext);

/** 子代理启动服务的消费侧契约:实现由 subagent 插件的 SubagentRuntime 提供,工作流等模块只面向本接口。 */
public interface ISubagentService
{
    public const string ServiceName = "subagents";

    Task<ISubagentRun> StartAsync(string name, SubagentStartRequest request);

    SubagentProviderInfo? GetProviderInfo(string name);
}
