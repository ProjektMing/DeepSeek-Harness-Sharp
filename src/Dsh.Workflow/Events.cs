using Dsh.Runtime.Events;

namespace Dsh.Workflow;

public sealed record WorkflowPhaseNotification(WorkflowRunInfo Info, string Title) : INotification
{
    public static string EventName => "workflow/phase";
}

public sealed record WorkflowLogNotification(WorkflowRunInfo Info, string Message) : INotification
{
    public static string EventName => "workflow/log";
}

public sealed record WorkflowAgentStartNotification(WorkflowRunInfo Info, WorkflowAgentInfo Agent) : INotification
{
    public static string EventName => "workflow/agent-start";
}

public sealed record WorkflowAgentEndNotification(WorkflowRunInfo Info, WorkflowAgentEndInfo Agent) : INotification
{
    public static string EventName => "workflow/agent-end";
}
