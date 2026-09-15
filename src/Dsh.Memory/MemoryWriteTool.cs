using System.Text.Json;
using System.Text.Json.Nodes;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Llm;
using Dsh.Runtime;

namespace Dsh.Memory;

public sealed record MemoryWriteResult(string Store, int Bytes);

/** memory_write:整体替换项目记忆(markdown 文本);后端由 memory.backend 决定。 */
public static class MemoryWriteTool
{
    public const string ToolName = "memory_write";

    private const string SectionText =
        "Project memory holds stable project facts, decisions, conventions and corrections. "
        + "Use memory_write to replace its entire content with the updated markdown; "
        + "keep entries concise, actionable and grouped by topic.";

    public static IDisposable Register(Context ctx, HarnessOptions options, IMemoryStore store)
    {
        var tools = ctx.Get<ToolRuntime>(ToolRuntime.ServiceName)!;
        var systemPrompt = ctx.Get<SystemPrompt>(SystemPrompt.ServiceName)!;
        var section = systemPrompt.Section(PromptSection.Literal("tool:memory_write", PromptOrders.ToolMemoryWrite, SectionText));
        var registration = tools.Register(new ToolDefinition
        {
            Name = ToolName,
            Description = "Replace the entire project memory with the given markdown content.",
            Parameters = JsonNode.Parse("""
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["content"],
                  "properties": {
                    "content": { "type": "string", "description": "Full markdown content of the project memory (replaces the previous content)." }
                  }
                }
                """)!.AsObject(),
            Output = new ToolOutputDefinition(
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["store", "bytes"],
                      "properties": {
                        "store": { "type": "string" },
                        "bytes": { "type": "integer" }
                      }
                    }
                    """)!.AsObject(),
                (_, value) =>
                [
                    new TextBlock($"Project memory updated ({value.GetProperty("bytes").GetInt32()} bytes, {value.GetProperty("store").GetString()})."),
                ]),
            Execute = (args, exec) => Execute(args, exec, options, store),
        });
        return new Registration(registration, section);
    }

    private static async Task<object?> Execute(
        JsonElement args,
        ToolRunContext exec,
        HarnessOptions options,
        IMemoryStore store)
    {
        if (HarnessSettings.Load(options.Home).Memory?.Enabled != true)
            throw new InvalidOperationException("project memory is disabled; run /memory on first");
        var content = args.TryGetProperty("content", out var contentElement) ? contentElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("memory_write requires non-empty content");
        await store.SetAsync(content, exec.Signal);
        return new MemoryWriteResult(store.Description, content.Length);
    }

    private sealed class Registration(IDisposable registration, IDisposable section) : IDisposable
    {
        public void Dispose()
        {
            section.Dispose();
            registration.Dispose();
        }
    }
}
