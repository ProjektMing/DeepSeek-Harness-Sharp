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
            _ => MemoryPolicyText(ctx, options)));
        systemPrompt.Context(new PromptContext(
            MemoryContextName,
            MemoryContextOrder,
            _ => MemoryContextText(ctx, options)));

        return commands.Register(new CommandDefinition
        {
            Name = "memory",
            Description = "Enable, disable, or show project memory",
            Input = new CommandInputDescriptor("on|off|show"),
            Handler = async invocation =>
            {
                var raw = invocation.RawInput.Trim();
                switch (raw)
                {
                    case "on":
                    case "off":
                        return Toggle(options, raw == "on");
                    case "show":
                        if (!IsEnabled(options))
                            return new CommandResult.Error("project memory is disabled; run /memory on first");
                        if (ResolveMemory(ctx) is not { } memoryToShow)
                            return new CommandResult.Error("project memory is unavailable; the memory plugin is not loaded");
                        return new CommandResult.Success(await memoryToShow.ShowAsync(invocation.Signal));
                    default:
                        return new CommandResult.Error("usage: /memory on | /memory off | /memory show");
                }
            },
        });
    }

    private static CommandResult Toggle(HarnessOptions options, bool enabled)
    {
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
                Capture = settings.Memory?.Capture,
                Mongo = settings.Memory?.Mongo,
            },
        };
        updated.Save(options.Home);
        return new CommandResult.Success($"project memory {(enabled ? "on" : "off")}");
    }

    private static string MemoryPolicyText(Context ctx, HarnessOptions options)
    {
        if (!IsEnabled(options))
            return "";
        if (ResolveMemory(ctx) is not { } memory)
            return "";
        return $"""
            Project memory is enabled (store: {memory.Description}).
            Maintain it with the memory_save tool: remember (upsert a record), correct (record a correction), forget (remove by key), skip (out-of-scope content).
            Records are timestamped automatically; the injected index is capped at 8192 bytes, so read the memory file directly when you need full content.
            """;
    }

    private static string MemoryContextText(Context ctx, HarnessOptions options)
    {
        if (!IsEnabled(options))
            return "";
        if (ResolveMemory(ctx) is not { } memory)
            return "";
        try
        {
            return memory.BuildIndexAsync().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            return $"Project memory ({memory.Description}) is unavailable: {error.Message}";
        }
    }

    private static ProjectMemory? ResolveMemory(Context ctx)
        => ctx.Get<ProjectMemory>(MemoryServices.ProjectMemory, false);

    private static bool IsEnabled(HarnessOptions options)
        => HarnessSettings.Load(options.Home).Memory?.Enabled == true;
}
