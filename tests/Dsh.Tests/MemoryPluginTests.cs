using System.Text.Json;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Memory;
using Dsh.Llm;
using Dsh.Runtime;

namespace Dsh.Tests;

public sealed class MemoryPluginTests
{
    [Fact]
    public void Factory_UsesFileBackendByDefault()
    {
        var store = MemoryStoreFactory.Create(new MemorySettings(), Path.GetTempPath());

        var file = Assert.IsType<FileMemoryStore>(store);
        Assert.EndsWith(".dsh-memory.md", file.Description);
    }

    [Fact]
    public void Factory_UsesMongoBackendWhenConfigured()
    {
        var store = MemoryStoreFactory.Create(new MemorySettings
        {
            Backend = "mongo",
            Mongo = new MemoryMongoSettings { Database = "dsh", Collection = "memory", Key = "k1" },
        }, Path.GetTempPath());

        var mongo = Assert.IsType<MongoTextStore>(store);
        Assert.Equal("dsh.memory#k1", mongo.Description);
        mongo.Dispose();
    }

    [Fact]
    public async Task FileStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dsh-memory-{Guid.NewGuid():N}", "memory.md");
        try
        {
            var store = new FileMemoryStore(path);
            Assert.Null(await store.GetAsync());

            await store.SetAsync("# Memory\n- fact");

            Assert.Equal("# Memory\n- fact", await store.GetAsync());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public async Task WriteTool_ReplacesMemoryWhenEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dsh-memory-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var memoryPath = Path.Combine(root, "memory.md");
            File.WriteAllText(Path.Combine(root, "settings.yaml"), $"""
                memory:
                  enabled: true
                  file: {memoryPath}
                """);
            var home = HarnessHome.Resolve(root);
            var options = new HarnessOptions(home, root);
            var ctx = new Context();
            _ = new SystemPrompt(ctx, new SystemPromptConfig());
            var tools = new ToolRuntime(ctx);
            var store = new FileMemoryStore(memoryPath);
            using var tool = MemoryWriteTool.Register(ctx, options, store);

            var result = await tools.Execute(new ToolExecutionInput
            {
                CallId = ToolCallId.Create("call-1"),
                Name = "memory_write",
                Arguments = JsonDocument.Parse("""{"content":"# Memory\n- decision"}""").RootElement,
                Signal = default,
            });

            Assert.IsType<ToolExecutionResult.Success>(result);
            Assert.Equal("# Memory\n- decision", File.ReadAllText(memoryPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteTool_FailsWhenMemoryDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dsh-memory-off-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var memoryPath = Path.Combine(root, "memory.md");
            File.WriteAllText(Path.Combine(root, "settings.yaml"), $"""
                memory:
                  enabled: false
                  file: {memoryPath}
                """);
            var home = HarnessHome.Resolve(root);
            var options = new HarnessOptions(home, root);
            var ctx = new Context();
            _ = new SystemPrompt(ctx, new SystemPromptConfig());
            var tools = new ToolRuntime(ctx);
            using var tool = MemoryWriteTool.Register(ctx, options, new FileMemoryStore(memoryPath));

            var result = await tools.Execute(new ToolExecutionInput
            {
                CallId = ToolCallId.Create("call-1"),
                Name = "memory_write",
                Arguments = JsonDocument.Parse("""{"content":"x"}""").RootElement,
                Signal = default,
            });

            Assert.IsType<ToolExecutionResult.Failure>(result);
            Assert.False(File.Exists(memoryPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
