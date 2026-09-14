using Dsh.Runtime;
using Dsh.Boot;
using Dsh.Core;

namespace Dsh.Interaction;

public static class ProviderBootstrapper
{
    public static IDisposable? Register(Context ctx, HarnessOptions options)
    {
        var settings = HarnessSettings.Load(options.Home);
        var defaultModel = settings.ResolveDefaultModel();
        var defaultProvider = options.Provider ?? defaultModel?.Provider ?? HarnessComposer.DefaultProvider;
        var credentials = ctx.GetProp("credentials") as ICredentials
            ?? throw new InvalidOperationException("credentials is required for provider registration");
        var llm = ctx.Get<LlmRuntime>(LlmRuntime.ServiceName)
            ?? throw new InvalidOperationException("llm is required for provider registration");

        var handles = new List<IDisposable>();
        try
        {
            foreach (var (name, providerSettings) in settings.Providers)
            {
                var isDefault = string.Equals(name, defaultProvider, StringComparison.OrdinalIgnoreCase);
                if (RegisterOne(ctx, options, credentials, llm, name, providerSettings, isDefault) is { } handle)
                    handles.Add(handle);
            }
            if (settings.ResolveProvider(defaultProvider) is null
                && RegisterOne(ctx, options, credentials, llm, defaultProvider, null, true) is { } fallback)
            {
                handles.Add(fallback);
            }
        }
        catch
        {
            foreach (var handle in handles)
                handle.Dispose();
            throw;
        }
        return handles.Count == 0 ? null : new DisposableBundle(handles);
    }

    private static IDisposable? RegisterOne(
        Context ctx,
        HarnessOptions options,
        ICredentials credentials,
        LlmRuntime llm,
        string provider,
        ProviderSettings? providerSettings,
        bool isDefault)
    {
        var baseUrl = (isDefault ? options.BaseUrl : null)
            ?? providerSettings?.Options?.BaseUrl
            ?? HarnessComposer.DefaultBaseUrl;
        var apiKeyEnv = (isDefault ? options.ApiKeyEnv : null)
            ?? providerSettings?.Options?.ApiKeyEnv
            ?? HarnessComposer.DefaultApiKeyEnv;
        var apiKey = (isDefault ? options.ApiKey : null)
            ?? providerSettings?.Options?.ApiKey;
        return ProviderAdapterRegistrar.RegisterProviderAdapter(
            ctx,
            provider,
            providerSettings,
            baseUrl,
            apiKeyEnv,
            apiKey,
            options,
            credentials,
            llm);
    }

    private sealed class DisposableBundle(IReadOnlyList<IDisposable> disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
                disposable.Dispose();
        }
    }
}