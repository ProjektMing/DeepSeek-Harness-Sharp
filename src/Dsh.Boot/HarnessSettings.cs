using YamlDotNet.Serialization;

namespace Dsh.Boot;

/** 配置文件位置：%USERPROFILE%\.dsh\settings.yaml（即 $DSH_HOME/settings.yaml，默认 ~/.dsh/settings.yaml） */
public sealed class HarnessSettings
{
    [YamlMember(Alias = "global_default_model")]
    public string? GlobalDefaultModel { get; set; }

    [YamlMember(Alias = "compaction_model")]
    public string? CompactionModel { get; set; }

    [YamlMember(Alias = "subagent")]
    public SubagentSettings? Subagent { get; set; }

    [YamlMember(Alias = "providers")]
    public Dictionary<string, ProviderSettings> Providers { get; set; } = [];

    [YamlMember(Alias = "skills")]
    public SkillsSettings? Skills { get; set; }

    [YamlMember(Alias = "rules")]
    public List<string> Rules { get; set; } = [];

    [YamlMember(Alias = "mcp")]
    public Dictionary<string, McpServerSettings> McpServers { get; set; } = [];

    [YamlMember(Alias = "compaction")]
    public CompactionSettings? Compaction { get; set; }

    [YamlMember(Alias = "safety")]
    public SafetySettings? Safety { get; set; }

    [YamlMember(Alias = "memory")]
    public MemorySettings? Memory { get; set; }

    private const string MinimalSettingsTemplate = """
        global_default_model: deepseek-official/deepseek-v4-flash
        compaction_model: deepseek-official/deepseek-v4-flash
        """;

    public static string LoadTemplate(string? baseDirectory = null)
    {
        var templatePath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Profiles", "Templates", "settings.yaml");
        return File.Exists(templatePath) ? File.ReadAllText(templatePath) : MinimalSettingsTemplate;
    }

    public static HarnessSettings Load(HarnessHome home)
    {
        var path = Path.Combine(home.Root, "settings.yaml");
        Directory.CreateDirectory(home.Root);
        if (!File.Exists(path))
            File.WriteAllText(path, LoadTemplate());
        var deserializer = new StaticDeserializerBuilder(new DshYamlStaticContext())
            .WithAttemptingUnquotedStringTypeDeserialization()
            .Build();
        return deserializer.Deserialize<HarnessSettings>(File.ReadAllText(path));
    }

    public void Save(HarnessHome home)
    {
        var path = Path.Combine(home.Root, "settings.yaml");
        Directory.CreateDirectory(home.Root);
        var serializer = new StaticSerializerBuilder(new DshYamlStaticContext()).Build();
        File.WriteAllText(path, serializer.Serialize(this));
    }

    public (string Provider, string Model)? ResolveDefaultModel()
    {
        if (GlobalDefaultModel is not { Length: > 0 } value)
            return null;
        var parts = value.Split('/', 2);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;
        return (parts[0], parts[1]);
    }

    public ProviderSettings? ResolveProvider(string providerName)
        => Providers.GetValueOrDefault(providerName);
}

public sealed class ProviderSettings
{
    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "options")]
    public ProviderOptions? Options { get; set; }

    [YamlMember(Alias = "models")]
    public Dictionary<string, ProviderModelSettings> Models { get; set; } = [];
}

public sealed class ProviderOptions
{
    [YamlMember(Alias = "baseUrl")]
    public string? BaseUrl { get; set; }

    [YamlMember(Alias = "apiKey")]
    public string? ApiKey { get; set; }

    [YamlMember(Alias = "apiKeyEnv")]
    public string? ApiKeyEnv { get; set; }
}

public sealed class ProviderModelSettings
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "reasoning")]
    public bool? Reasoning { get; set; }

    [YamlMember(Alias = "tool_call")]
    public bool? ToolCall { get; set; }

    [YamlMember(Alias = "system_prompt_update")]
    public string? SystemPromptUpdate { get; set; }
}

public sealed class SubagentSettings
{
    [YamlMember(Alias = "default_model")]
    public string? DefaultModel { get; set; }
}

public sealed class SkillsSettings
{
    [YamlMember(Alias = "paths")]
    public List<string> Paths { get; set; } = [];

    [YamlMember(Alias = "urls")]
    public List<string> Urls { get; set; } = [];
}

public sealed class CompactionSettings
{
    [YamlMember(Alias = "auto")]
    public bool Auto { get; set; } = true;

    [YamlMember(Alias = "prune")]
    public bool Prune { get; set; } = true;
}

public sealed class SafetySettings
{
    [YamlMember(Alias = "autoApprove")]
    public bool AutoApprove { get; set; }

    [YamlMember(Alias = "blacklist")]
    public List<string> Blacklist { get; set; } = [];
}

public sealed class MemorySettings
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }

    [YamlMember(Alias = "file")]
    public string? File { get; set; }
}

public sealed class McpServerSettings
{
    [YamlMember(Alias = "transport")]
    public string? Transport { get; set; }

    [YamlMember(Alias = "command")]
    public List<string>? Command { get; set; }

    [YamlMember(Alias = "args")]
    public List<string>? Args { get; set; }

    [YamlMember(Alias = "url")]
    public string? Url { get; set; }

    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;
}
