using System.Net.Http.Json;
using System.Text.Json;
using Dsh.Llm;

namespace Dsh.E2b;

public sealed record E2bCommandResult(string Stdout, string Stderr, int ExitCode);

public sealed record E2bCreateSandboxRequest(string Template);

public sealed record E2bExecuteCommandRequest(string Command);

public sealed class E2bClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public E2bClient(HttpClient http, string baseUrl = "https://api.e2b.dev")
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<string> CreateSandboxAsync(string template, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(new E2bCreateSandboxRequest(template), DshJson.Info<E2bCreateSandboxRequest>());
        using var response = await _http.PostAsync($"{_baseUrl}/sandboxes", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("sandboxId").GetString()
            ?? throw new JsonException("E2B create response missing sandboxId");
    }

    public async Task<E2bCommandResult> ExecuteCommandAsync(string sandboxId, string command, CancellationToken cancellationToken = default)
    {
        using var content = JsonContent.Create(new E2bExecuteCommandRequest(command), DshJson.Info<E2bExecuteCommandRequest>());
        using var response = await _http.PostAsync($"{_baseUrl}/sandboxes/{sandboxId}/commands", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new E2bCommandResult(
            root.TryGetProperty("stdout", out var stdout) ? stdout.GetString() ?? "" : "",
            root.TryGetProperty("stderr", out var stderr) ? stderr.GetString() ?? "" : "",
            root.TryGetProperty("exitCode", out var exitCode) ? exitCode.GetInt32() : 0);
    }

    public async Task KillSandboxAsync(string sandboxId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"{_baseUrl}/sandboxes/{sandboxId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
