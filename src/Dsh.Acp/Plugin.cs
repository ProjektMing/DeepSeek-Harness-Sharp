using Dsh.Runtime;
using Dsh.Boot;
using Dsh.Interaction;
using Dsh.Plugins;

[assembly: DshPlugin("@deepseek-ai/dsh-acp")]

namespace Dsh.Acp;

public sealed class Plugin : IDshPlugin
{
    private const string EndpointKey = "acpEndpoint";

    public string[] Inject => [];

    public IDisposable Apply(Context ctx, object? config)
    {
        if (Console.IsInputRedirected)
            return new CallbackDisposable();
        var (provider, model) = ResolveDefaults(ctx);
        var home = (ctx.GetProp("harnessOptions") as HarnessOptions)?.Home ?? HarnessHome.Resolve();
        var listener = new AcpSocketServer(ctx, home, provider, model, ApprovalAnswerers.AutoApproveScoped);
        listener.Start();
        ctx.Root.SetOwn(EndpointKey, listener.Endpoint);
        return new CallbackDisposable(listener.Dispose);
    }

    private static (string? Provider, string? Model) ResolveDefaults(Context ctx)
    {
        if (ctx.GetProp("harnessOptions") is not HarnessOptions options)
            return (null, null);
        var fallback = HarnessSettings.Load(options.Home).ResolveDefaultModel();
        return (options.Provider ?? fallback?.Provider,
            options.Model ?? fallback?.Model);
    }

    private sealed class CallbackDisposable(Action? dispose = null) : IDisposable
    {
        public void Dispose() => dispose?.Invoke();
    }
}
