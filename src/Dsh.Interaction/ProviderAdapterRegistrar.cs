using Dsh.Runtime;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Llm;
using Dsh.Llm.Anthropic;
using Dsh.Llm.DeepSeek;
using Dsh.Llm.OpenAi;

namespace Dsh.Interaction;

internal static class ProviderAdapterRegistrar
{
    private static readonly IReadOnlyList<DeepSeekCatalogModel> DeepSeekCatalog =
    [
        new("deepseek-v4-flash", "DeepSeek-V4-Flash",
            "Fast, efficient, and economical; suited to focused, routine, or parallel tasks.",
            DeepSeekConnectionOptions.DefaultContextWindowValue),
        new("deepseek-v4-pro", "DeepSeek-V4-Pro",
            "Stronger agentic coding, knowledge, and difficult reasoning; suited to complex or quality-critical tasks at higher cost.",
            DeepSeekConnectionOptions.DefaultContextWindowValue),
        new("deepseek-v4-flash-vision-exp", "DeepSeek-V4-Flash-Vision-Exp",
            null,
            DeepSeekConnectionOptions.DefaultContextWindowValue,
            null,
            ["text", "image"]),
    ];

    public static IDisposable? RegisterProviderAdapter(
        Context ctx,
        string providerId,
        ProviderSettings? provider,
        string baseUrl,
        string? apiKeyEnv,
        string? apiKey,
        HarnessOptions options,
        ICredentials credentials,
        LlmRuntime llm)
    {
        if (string.Equals(provider?.Type, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedApiKey = ResolveKey(ctx, providerId, apiKeyEnv ?? "ANTHROPIC_API_KEY", apiKey, credentials);
            if (resolvedApiKey is null)
                return null;
            var adapter = new AnthropicAdapter(providerId, baseUrl, resolvedApiKey, provider?.Models.Keys.ToList());
            return llm.RegisterAdapter([providerId], adapter);
        }
        if (provider?.Type is "openai-compatible" or "openai-responses")
        {
            var resolvedApiKey = ResolveKey(ctx, providerId, apiKeyEnv ?? "OPENAI_API_KEY", apiKey, credentials);
            if (resolvedApiKey is null)
                return null;
            var useResponses = string.Equals(provider.Type, "openai-responses", StringComparison.OrdinalIgnoreCase);
            var adapter = new OpenAiCompatibleAdapter(
                providerId,
                Endpoint.NormalizeBaseUrl(baseUrl),
                resolvedApiKey,
                provider.Models.Keys.ToList(),
                useResponses: useResponses);
            return llm.RegisterAdapter([providerId], adapter);
        }
        if (ResolveKey(ctx, providerId, apiKeyEnv ?? HarnessComposer.DefaultApiKeyEnv, apiKey, credentials) is null)
            return null;
        return RegisterDeepSeekAdapter(providerId, provider, baseUrl, apiKeyEnv, apiKey, options, credentials, llm);
    }

    private static string? ResolveKey(
        Context ctx,
        string providerId,
        string apiKeyEnv,
        string? apiKey,
        ICredentials credentials)
    {
        var resolved = string.IsNullOrWhiteSpace(apiKey) ? credentials.Get(apiKeyEnv) : apiKey;
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;
        ctx.Logger.Warn("%s", $"provider \"{providerId}\" skipped: API key is not configured (set providers.{providerId}.options.apiKey or environment variable {apiKeyEnv}); requests using it will fail with NO_ADAPTER");
        return null;
    }

    private static IReadOnlyList<DeepSeekCatalogModel> MergeCatalog(ProviderSettings? provider)
    {
        if (provider?.Models is not { Count: > 0 } models)
            return DeepSeekCatalog;
        var merged = DeepSeekCatalog.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var (id, model) in models)
        {
            merged.TryGetValue(id, out var existing);
            merged[id] = new DeepSeekCatalogModel(
                id,
                model.Name ?? existing?.Name,
                existing?.Description,
                existing?.ContextWindow,
                existing?.MaxTokens,
                existing?.InputModalities,
                model.SystemPromptUpdate ?? existing?.SystemPromptUpdate);
        }
        return [.. merged.Values];
    }

    private static AdapterRegistrationHandle RegisterDeepSeekAdapter(
        string providerId,
        ProviderSettings? provider,
        string baseUrl,
        string? apiKeyEnv,
        string? apiKey,
        HarnessOptions options,
        ICredentials credentials,
        LlmRuntime llm)
    {
        var resolvedBaseUrl = Endpoint.NormalizeBaseUrl(baseUrl);
        var resolvedApiKeyEnv = apiKeyEnv ?? HarnessComposer.DefaultApiKeyEnv;
        var connection = new DeepSeekConnectionOptions(
            resolvedBaseUrl,
            resolvedApiKeyEnv,
            new RequestDefaults(),
            DeepSeekConnectionOptions.DefaultMaxTokens,
            DeepSeekConnectionOptions.DefaultContextWindowValue,
            MergeCatalog(provider),
            DeepSeekConnectionOptions.DefaultStreamIdleTimeoutMs,
            ResolvedRetryPolicy.Resolve(null, "llm-deepseek"));
        var adapter = new DeepSeekAdapter(providerId, new DeepSeekAdapterOptions
        {
            Options = () => connection,
            ResolveApiKey = (conn, _) =>
            {
                var raw = apiKey ?? credentials.Get(conn.ApiKeyEnv)
                    ?? throw new LlmException(new LlmFailure(
                        $"provider \"{providerId}\" credential \"{conn.ApiKeyEnv}\" is not configured",
                        LlmFailureCodes.MissingCredential));
                if (!ApiKey.Normalize(raw, out var key, out var rejection))
                {
                    throw new LlmException(new LlmFailure(
                        $"provider \"{providerId}\" credential \"{conn.ApiKeyEnv}\" is unusable: {rejection}",
                        LlmFailureCodes.InvalidCredential));
                }
                return Task.FromResult(key);
            },
            ResolveUserId = () => AnonymousUserId.Resolve(options.Home),
        });
        return llm.RegisterAdapter([providerId], adapter);
    }
}
