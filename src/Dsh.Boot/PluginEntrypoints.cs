namespace Dsh.Boot;

public sealed record PluginEntrypointOptions(
    HarnessHome Home,
    string Cwd);

/** 入口插件契约:插件类实现它,并在类上声明 DshEntrypoint("tui") 之类的入口名。 */
public interface IDshEntrypoint
{
    Task<int> RunAsync(HarnessApp app, PluginEntrypointOptions options, CancellationToken cancellationToken);
}
