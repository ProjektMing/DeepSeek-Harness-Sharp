using Cordis;
using Dsh.Boot;
using Dsh.Interaction;
using Dsh.Plugins;
using Dsh.Sdk;

[assembly: DshPlugin("@deepseek-ai/dsh-acp")]

namespace Dsh.Acp;

public sealed class Plugin : IDshPlugin
{
    private const string EndpointKey = "acpEndpoint";

    public string[] Inject => [];

    public IDisposable Apply(Context ctx, object? config)
    {
        PluginEntrypointRegistry.Register("acp",
            static (app, _, cancellationToken) => RunAsync(app, cancellationToken));
        if (Console.IsInputRedirected)
            return new CallbackDisposable();
        var (provider, model) = ResolveDefaults(ctx);
        var home = (ctx.GetProp("harnessOptions") as HarnessOptions)?.Home ?? HarnessHome.Resolve();
        var listener = new AcpSocketServer(ctx, home, provider, model, ApprovalAnswerers.AutoApproveScoped);
        listener.Start();
        ctx.Root.SetOwn(EndpointKey, listener.Endpoint);
        return new CallbackDisposable(listener.Dispose);
    }

    private static async Task<int> RunAsync(HarnessApp app, CancellationToken cancellationToken)
    {
        if (!Console.IsInputRedirected)
            return await RunSocketModeAsync(app, cancellationToken);
        var transport = new JsonRpcLineTransport(Console.In, Console.Out);
        var server = new AcpServer(app.Ctx, transport, app.Provider, app.Model, ApprovalAnswerers.AutoApproveScoped);
        transport.RequestHandler = server.HandleRequestAsync;
        transport.NotificationHandler = (method, parameters) =>
        {
            if (method == AcpMethods.Cancel)
                server.Cancel(parameters);
        };
        transport.Start();
        await transport.WhenClosedAsync().WaitAsync(cancellationToken);
        await server.CloseAllAsync();
        await transport.DisposeAsync();
        return 0;
    }

    private static async Task<int> RunSocketModeAsync(HarnessApp app, CancellationToken cancellationToken)
    {
        var endpoint = app.Ctx.GetProp(EndpointKey) as string
            ?? throw new InvalidOperationException("the ACP socket listener was not started");
        await Console.Error.WriteLineAsync($"dsh: ACP listening on {endpoint}");
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await shutdown.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
        return 0;
    }

    private static (string? Provider, string? Model) ResolveDefaults(Context ctx)
    {
        if (ctx.GetProp("harnessOptions") is not HarnessOptions options)
            return (null, null);
        var fallback = HarnessSettings.Load(options.Home).ResolveDefaultModel();
        return (options.Provider ?? fallback?.Provider ?? HarnessComposer.DefaultProvider,
            options.Model ?? fallback?.Model ?? HarnessComposer.DefaultModel);
    }

    private sealed class CallbackDisposable(Action? dispose = null) : IDisposable
    {
        public void Dispose() => dispose?.Invoke();
    }
}
