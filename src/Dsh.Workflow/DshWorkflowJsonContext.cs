using System.Text.Json.Serialization;

namespace Dsh.Workflow;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolWorkflowRunStartPayload))]
[JsonSerializable(typeof(ToolWorkflowRunEndPayload))]
[JsonSerializable(typeof(RalphRunResult))]
[JsonSerializable(typeof(WorkflowRunToolResult))]
[JsonSerializable(typeof(ToolWorkflowAgentStartPayload))]
[JsonSerializable(typeof(ToolWorkflowAgentEndPayload))]
internal sealed partial class DshWorkflowJsonContext : JsonSerializerContext
{
}

