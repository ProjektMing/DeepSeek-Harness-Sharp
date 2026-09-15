using Dsh.Boot;
using Dsh.Plugins;
using Dsh.Runtime;
using Dsh.Sdk;

[assembly: DshPlugin("@deepseek-ai/dsh-lsp")]

namespace Dsh.Lsp;

/** 入口 "lsp":在 stdio 上跑 LSP 服务(initialize/hover/shutdown)。 */
[DshEntrypoint("lsp")]
public sealed class Plugin : IDshPlugin, IDshEntrypoint
{
    public string[] Inject => [];

    public IDisposable Apply(Context ctx, object? config) => new CallbackDisposable();

    public async Task<int> RunAsync(HarnessApp app, PluginEntrypointOptions options, CancellationToken cancellationToken)
    {
        await using var transport = new JsonRpcLineTransport(Console.In, Console.Out);
        transport.RequestHandler = new LspServer().HandleRequestAsync;
        transport.Start();
        await transport.WhenClosedAsync().WaitAsync(cancellationToken);
        return 0;
    }

    private sealed class CallbackDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
