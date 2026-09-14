using Dsh.Runtime;
using Dsh.Runtime.Logging;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Interaction;

namespace Dsh.Tests;

public sealed class ProviderRegistrationTests
{
    [Fact]
    public void Register_SkipsProviderWithoutCredential()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        var llm = new LlmRuntime(ctx);
        ctx.SetOwn("credentials", new StubCredentials(null));
        var dir = CreateHome();
        try
        {
            var registration = ProviderBootstrapper.Register(ctx, new HarnessOptions(new HarnessHome(dir), dir));

            Assert.Null(registration);
            Assert.Empty(llm.ListProviders());
            Assert.Contains(ctx.Root.Logger.Buffer,
                message => message.Text.Contains("skipped: API key is not configured"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Register_RegistersProviderWhenCredentialIsConfigured()
    {
        using var setup = LoggingSetup.Create();
        var ctx = new Context(setup);
        var llm = new LlmRuntime(ctx);
        ctx.SetOwn("credentials", new StubCredentials("sk-test"));
        var dir = CreateHome();
        try
        {
            using var registration = ProviderBootstrapper.Register(ctx, new HarnessOptions(new HarnessHome(dir), dir));

            Assert.NotNull(registration);
            var provider = Assert.Single(llm.ListProviders());
            Assert.Equal("deepseek-official", provider.Id);
            Assert.DoesNotContain(ctx.Root.Logger.Buffer,
                message => message.Text.Contains("skipped: API key is not configured"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CreateHome()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dsh-provider-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.yaml"), """
            global_default_model: deepseek-official/deepseek-v4-flash
            providers:
              deepseek-official:
                type: openai-compatible
                options:
                  baseUrl: https://example.invalid
                  apiKeyEnv: DSH_TEST_MISSING_KEY
                models:
                  deepseek-v4-flash:
                    name: Test Flash
            """);
        return dir;
    }

    private sealed class StubCredentials(string? value) : ICredentials
    {
        public string? Get(string reference) => value;
    }
}
