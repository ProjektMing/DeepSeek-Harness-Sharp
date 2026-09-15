using Dsh.Llm;
using Dsh.Runtime.Events;

namespace Dsh.Core;

public sealed record LlmStreamNotification(GenerateOptions Options) : INotification
{
    public static string EventName => "llm/stream";
}

public sealed record LlmAdaptersUpdatedNotification : INotification
{
    public static string EventName => "llm/adapters-updated";
}

public sealed record LlmAdapterFactoriesUpdatedNotification : INotification
{
    public static string EventName => "llm/adapter-factories-updated";
}
