using Dsh.Runtime;
using Dsh.Boot;
using Dsh.Core;

namespace Dsh.Interaction;

public static class MemoryCommand
{
    private const string MemorySectionName = "memory:policy";
    private const string MemoryContextName = "memory:project";
    private const int MemoryPolicyOrder = 550;
    private const int MemoryContextOrder = 105;

    public static IDisposable Register(Context ctx, HarnessOptions options)
    {
        var commands = ctx.Get<CommandsService>(CommandsService.ServiceName, false)!;
        var systemPrompt = ctx.Get<SystemPrompt>(SystemPrompt.ServiceName)!;
        systemPrompt.Section(new PromptSection(
            MemorySectionName,
            MemoryPolicyOrder,
            _ => MemoryPolicyText(options)));
        systemPrompt.Context(new PromptContext(
            MemoryContextName,
            MemoryContextOrder,
            _ => MemoryContextText(ctx, options)));

        return commands.Register(new CommandDefinition
        {
            Name = "memory",
            Description = "Enable or disable project memory",
            Input = new CommandInputDescriptor("on|off"),
            Handler = invocation =>
            {
                var raw = invocation.RawInput.Trim();
                if (raw != "on" && raw != "off")
                    return Task.FromResult<CommandResult>(new CommandResult.Error("usage: /memory on | /memory off"));
                var enabled = raw == "on";
                var settings = HarnessSettings.Load(options.Home);
                var updated = new HarnessSettings
                {
                    GlobalDefaultModel = settings.GlobalDefaultModel,
                    CompactionModel = settings.CompactionModel,
                    Subagent = settings.Subagent,
                    Providers = settings.Providers,
                    Skills = settings.Skills,
                    Rules = settings.Rules,
                    McpServers = settings.McpServers,
                    Compaction = settings.Compaction,
                    Safety = settings.Safety,
                    Memory = new MemorySettings
                    {
                        Enabled = enabled,
                        File = settings.Memory?.File,
                        Backend = settings.Memory?.Backend,
                        Mongo = settings.Memory?.Mongo,
                    },
                };
                updated.Save(options.Home);
                return Task.FromResult<CommandResult>(new CommandResult.Success($"project memory {raw}"));
            },
        });
    }

    private static string MemoryPolicyText(HarnessOptions options)
    {
        if (!IsEnabled(options))
            return "";
        if (IsMongoBackend(options))
        {
            return $"""
                Project memory is enabled (store: {DescribeStore(options)}).
                Maintain it with the memory_write tool: replace the whole markdown content when project facts, decisions, conventions, or corrections change.
                Keep entries concise, actionable, and grouped by topic.
                """;
        }
        var path = ResolveMemoryPath(options);
        return $"""
            Project memory is enabled.
            Maintain {path} as the project Markdown memory file.
            When you learn stable project facts, decisions, conventions, or corrections, update this file with your file tools.
            Keep entries concise, actionable, and grouped by topic.
            """;
    }

    private static string MemoryContextText(Context ctx, HarnessOptions options)
    {
        if (!IsEnabled(options))
            return "";
        if (ctx.Get<IMemoryStore>(MemoryServices.Store, false) is { } store)
        {
            string? text;
            try
            {
                text = store.GetAsync().GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                return $"Project memory ({store.Description}) is unavailable: {error.Message}";
            }
            return text is null
                ? $"Project memory ({store.Description}) is empty; record project knowledge when you learn it."
                : $"Project memory ({store.Description}):\n\n{text}";
        }
        var path = ResolveMemoryPath(options);
        if (!File.Exists(path))
            return $"Project memory ({path}) does not exist yet. Create it when you record project knowledge.";
        return $"Project memory ({path}):\n\n{File.ReadAllText(path)}";
    }

    private static bool IsMongoBackend(HarnessOptions options)
        => string.Equals(HarnessSettings.Load(options.Home).Memory?.Backend, "mongo", StringComparison.OrdinalIgnoreCase);

    private static string DescribeStore(HarnessOptions options)
    {
        var memory = HarnessSettings.Load(options.Home).Memory;
        var mongo = memory?.Mongo;
        return mongo is null ? "mongo" : $"{mongo.Database}.{mongo.Collection}#{mongo.Key ?? "project"}";
    }

    private static bool IsEnabled(HarnessOptions options)
        => HarnessSettings.Load(options.Home).Memory?.Enabled == true;

    private static string ResolveMemoryPath(HarnessOptions options)
    {
        var configured = HarnessSettings.Load(options.Home).Memory?.File;
        var cwd = options.Cwd ?? Environment.CurrentDirectory;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(cwd, ".dsh-memory.md")
            : Path.GetFullPath(configured, cwd);
    }
}
