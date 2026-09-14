using Dsh.Runtime;
using Dsh.Plugins;

namespace Dsh.Interaction;

public static class PluginCommand
{
    public static IDisposable Register(Context ctx, PluginCatalog catalog)
    {
        var commands = ctx.Get<CommandsService>(CommandsService.ServiceName, false)!;
        return commands.Register(new CommandDefinition
        {
            Name = "plugins",
            Description = "List, add, or remove DSH plugin modules",
            Input = new CommandInputDescriptor("list | add <pkg|path> | remove <pkg>"),
            Handler = invocation =>
            {
                var tokens = invocation.RawInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var manager = ctx.Get<IPluginManager>("pluginManager", false);
                if (tokens.Length == 0 || tokens[0] == "list")
                {
                    var names = (manager?.PackageNames ?? catalog.PackageNames)
                        .Distinct()
                        .OrderBy(name => name, StringComparer.Ordinal);
                    var mode = manager is null
                        ? "dynamic add/remove: unavailable"
                        : $"dynamic add/remove: {(manager.SupportsDynamicLoad ? "available" : "unavailable (NativeAOT)")}";
                    return Task.FromResult<CommandResult>(
                        new CommandResult.Success($"{string.Join('\n', names)}\n{mode}"));
                }
                if (manager is null)
                {
                    return Task.FromResult<CommandResult>(
                        new CommandResult.Error("plugin manager is not available in this profile"));
                }
                if (tokens[0] == "add" && tokens.Length > 1)
                    return RunAsync(manager.AddAsync(string.Join(' ', tokens.Skip(1))));
                if (tokens[0] == "remove" && tokens.Length > 1)
                    return RunAsync(manager.RemoveAsync(tokens[1]));
                return Task.FromResult<CommandResult>(
                    new CommandResult.Error("usage: /plugins list | add <pkg|path> | remove <pkg>"));
            },
        });
    }

    private static async Task<CommandResult> RunAsync(Task<string> operation)
    {
        var message = await operation;
        return new CommandResult.Success(message);
    }
}
