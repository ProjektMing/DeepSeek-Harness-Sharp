using Dsh.Boot;
using Dsh.Core;
using Dsh.Plugins;
using Dsh.Runtime;

[assembly: DshPlugin("@deepseek-ai/dsh-memory")]

namespace Dsh.Memory;

/** 项目记忆插件:按 settings.yaml 的 memory.backend 提供 IMemoryStore,并注册 memory_write 工具。 */
public sealed class Plugin : IDshPlugin
{
    public string[] Inject => [ToolRuntime.ServiceName, SystemPrompt.ServiceName];

    public IDisposable Apply(Context ctx, object? config)
    {
        var options = ctx.GetProp("harnessOptions") as HarnessOptions
            ?? throw new InvalidOperationException("harnessOptions is required for the memory plugin");
        var settings = HarnessSettings.Load(options.Home);
        var store = MemoryStoreFactory.Create(settings.Memory, options.Cwd ?? Environment.CurrentDirectory);
        ctx.Provide(MemoryServices.Store, store);
        var tool = MemoryWriteTool.Register(ctx, options, store);
        return new Bundle(store as IDisposable, tool);
    }

    private sealed class Bundle(IDisposable? store, IDisposable tool) : IDisposable
    {
        public void Dispose()
        {
            tool.Dispose();
            store?.Dispose();
        }
    }
}
