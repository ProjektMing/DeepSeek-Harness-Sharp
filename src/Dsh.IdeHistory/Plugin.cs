using Dsh.Core;
using Dsh.Plugins;
using Dsh.Runtime;

[assembly: DshPlugin(Dsh.IdeHistory.Plugin.IdeHistory)]

namespace Dsh.IdeHistory;

public sealed class Plugin(string packageName) : IDshPlugin
{
    internal const string IdeHistory = "@deepseek-ai/dsh-ide-history";

    public string[] Inject => packageName switch
    {
        IdeHistory => [ToolRuntime.ServiceName],
        _ => throw new InvalidOperationException($"Unknown DSH package '{packageName}'."),
    };

    public IDisposable Apply(Context ctx, object? config)
    {
        var registry = IdeHistoryRegistry.CreateDefault();
        return IdeHistoryTool.Register(ctx, registry);
    }
}
