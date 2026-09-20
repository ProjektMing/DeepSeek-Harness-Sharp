namespace Dsh.Core;

/** 非交互 shell 的环境覆盖:关闭颜色与分页器,保证子进程输出是纯文本。 */
public static class ShellEnvironment
{
    public static IReadOnlyDictionary<string, string?> NonInteractiveOverrides { get; } = new Dictionary<string, string?>
    {
        ["NO_COLOR"] = "1",
        ["TERM"] = "dumb",
        ["PAGER"] = "cat",
        ["GIT_PAGER"] = "cat",
    };
}
