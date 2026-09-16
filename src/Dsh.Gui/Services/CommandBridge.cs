using Dsh.Core;
using Dsh.Interaction;
using Dsh.Runtime;

namespace Dsh.Gui.Services;

/** 命令通道: 按钮与 `/` 命令都走这里, 复用 CommandsService 的校验与副作用, 不复制命令实现。 */
public sealed class CommandBridge(Context ctx)
{
    public event Action? Executed;

    public async Task<CommandExecution?> ExecuteAsync(IAgent agent, string line)
    {
        var commands = ctx.Get<CommandsService>(CommandsService.ServiceName, false);
        if (commands is null)
            return null;
        var execution = await commands.Execute(agent, line);
        Executed?.Invoke();
        return execution;
    }

    public async Task<string> RunAsync(IAgent agent, string line)
    {
        var execution = await ExecuteAsync(agent, line);
        return execution?.Result switch
        {
            CommandResult.Success { Text: { } text } => text,
            CommandResult.Error error => error.Text,
            _ => $"未知命令: {line}",
        };
    }

    public IReadOnlyList<CommandDescriptor> List(IAgent agent)
        => ctx.Get<CommandsService>(CommandsService.ServiceName, false)?.List(agent) ?? [];
}
