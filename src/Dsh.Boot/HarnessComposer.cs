using Dsh.Runtime;
using Dsh.Runtime.Composition;

namespace Dsh.Boot;

public sealed record HarnessOptions(
    HarnessHome Home,
    string? Cwd = null,
    string? Provider = null,
    string? Model = null,
    string? BaseUrl = null,
    string? ApiKeyEnv = null,
    string? ApiKey = null,
    string? ReasoningEffort = null,
    bool IsTui = false,
    string? EntrypointPlugin = null);

public sealed class HarnessApp : IDisposable
{
    public required Context Ctx { get; init; }
    public required HarnessHome Home { get; init; }
    public required ICredentials Credentials { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string? ReasoningEffort { get; init; }
    public Composition? Composition { get; set; }

    private readonly List<IDisposable> _disposables = [];

    internal void Track(IDisposable disposable) => _disposables.Add(disposable);

    public void Dispose()
    {
        List<Exception>? errors = null;
        foreach (var disposable in ((IEnumerable<IDisposable>)_disposables).Reverse())
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception error)
            {
                // 任何一个关闭失败都不能阻断其余清理(例如日志文件句柄必须释放)。
                (errors ??= []).Add(error);
            }
        }
        if (errors is not null)
            throw new AggregateException("harness dispose failed", errors);
    }
}

public static class HarnessComposer
{
    public const string DefaultProvider = "deepseek-official";
    public const string DefaultModel = "deepseek-v4-flash";
    public const string DefaultBaseUrl = "https://api.deepseek.com";
    public const string DefaultApiKeyEnv = "DEEPSEEK_API_KEY";

    public static async Task<HarnessApp> Compose(HarnessOptions options)
        => await ConfigBoot.Compose(options);
}

public static class AnonymousUserId
{
    public static string Resolve(HarnessHome home)
    {
        var path = Path.Combine(home.Root, ".anonymous-user-id");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Guid.TryParse(existing, out _))
                return existing;
        }
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(path, id + '\n');
        return id;
    }
}
