namespace Dsh.Boot;

public sealed record PluginEntrypointOptions(
    HarnessHome Home,
    string Cwd,
    /** `--session <id>`: 入口直接打开指定会话(GUI), 未指定时为 null。 */
    string? ResumeSessionId = null);

/** 入口插件契约:插件类实现它,并在类上声明 DshEntrypoint("tui") 之类的入口名。 */
public interface IDshEntrypoint
{
    Task<int> RunAsync(HarnessApp app, PluginEntrypointOptions options, CancellationToken cancellationToken);
}
