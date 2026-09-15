using Dsh.Runtime;
using Dsh.Plugins;

[assembly: DshPlugin(Dsh.Core.Plugin.Core)]

namespace Dsh.Core;

public sealed class Plugin(string packageName) : IDshPlugin
{
    internal const string Core = "@deepseek-ai/dsh-core";

    public string[] Inject => packageName switch
    {
        Core => [],
        _ => throw new InvalidOperationException($"Unknown DSH package '{packageName}'."),
    };

    public IDisposable Apply(Context ctx, object? config) => packageName switch
    {
        Core => RegisterCore(ctx, config),
        _ => throw new InvalidOperationException($"Unknown DSH package '{packageName}'."),
    };

    private static IDisposable RegisterCore(Context ctx, object? config)
    {
        _ = new SessionStore(ctx);
        _ = new SessionProjectionRegistry(ctx);
        _ = new SystemPrompt(ctx, SystemPromptConfigFrom(config));
        _ = new ToolRuntime(ctx);
        _ = new LlmRuntime(ctx);
        _ = new AgentRegistry(ctx);
        // 持久化插件声明依赖 core 的服务, 因此它总是晚于 core 加载; 这里必须按需解析而不是取值快照。
        _ = new AgentLoop(ctx, AgentLoopConfigFrom(config), _ => PersistenceOf(ctx));
        return new NoopDisposable();
    }

    private static ISessionPersistence PersistenceOf(Context ctx)
        => ctx.Get<ISessionPersistence>(PersistenceServiceName, false)
            ?? throw new InvalidOperationException("no session persistence backend configured for resume");

    private const string PersistenceServiceName = "sessionPersistence";

    private static SystemPromptConfig SystemPromptConfigFrom(object? config)
    {
        var dict = config as IReadOnlyDictionary<string, object?>;
        return new SystemPromptConfig
        {
            IncludeHarnessIdentity = BoolOf(dict, "includeHarnessIdentity") ?? new SystemPromptConfig().IncludeHarnessIdentity,
            IncludeRuntimeContext = BoolOf(dict, "includeRuntimeContext") ?? new SystemPromptConfig().IncludeRuntimeContext,
            Persona = dict?.GetValueOrDefault("persona") as string ?? "",
        };
    }

    private static AgentLoopConfig AgentLoopConfigFrom(object? config)
    {
        var dict = config as IReadOnlyDictionary<string, object?>;
        return new AgentLoopConfig
        {
            MaxParallelToolCalls = IntOf(dict, "maxParallelToolCalls") ?? AgentLoopConfig.DefaultMaxParallelToolCalls,
        };
    }

    private static bool? BoolOf(IReadOnlyDictionary<string, object?>? dict, string key)
        => dict?.GetValueOrDefault(key) as bool?;

    private static int? IntOf(IReadOnlyDictionary<string, object?>? dict, string key)
        => dict?.GetValueOrDefault(key) switch
        {
            long value => (int)value,
            int value => value,
            _ => null,
        };

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
