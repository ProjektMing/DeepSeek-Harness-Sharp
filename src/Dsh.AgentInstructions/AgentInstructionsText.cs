using Dsh.Boot;

namespace Dsh.AgentInstructions;

public static class AgentInstructionsText
{
    private const string InstructionsFileName = "AGENTS.md";

    private static readonly IReadOnlySet<string> GlobalAgentDirs = new HashSet<string>(StringComparer.Ordinal)
    {
        ".config", ".kilocode", ".kilo", ".opencode",
    };

    public static IReadOnlyList<string> Discover(HarnessHome home, string? cwd)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var homeRoot = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var dir = Path.GetFullPath(string.IsNullOrEmpty(cwd) ? Directory.GetCurrentDirectory() : cwd);
        while (true)
        {
            var candidate = Path.Combine(dir, InstructionsFileName);
            if (File.Exists(candidate) && seen.Add(candidate))
                found.Add(candidate);
            if (string.Equals(dir, homeRoot, StringComparison.Ordinal))
                break;
            var parent = Directory.GetParent(dir);
            if (parent is null || GlobalAgentDirs.Contains(parent.Name))
                break;
            dir = parent.FullName;
        }
        found.Reverse();
        var harnessGlobal = Path.Combine(home.Root, InstructionsFileName);
        if (File.Exists(harnessGlobal) && seen.Add(harnessGlobal))
            found.Add(harnessGlobal);
        return found;
    }

    public static string Render(HarnessHome home, string? cwd)
    {
        var parts = new List<string>();
        foreach (var path in Discover(home, cwd))
        {
            var content = File.ReadAllText(path).Trim();
            if (content.Length > 0)
                parts.Add($"## {path}\n\n{content}");
        }
        var rules = HarnessSettings.Load(home).Rules;
        if (rules.Count > 0)
            parts.Add($"## Rules\n\n{string.Join("\n", rules.Select(rule => $"- {rule}"))}");
        return parts.Count == 0 ? "" : $"# Agent Instructions\n\n{string.Join("\n\n", parts)}";
    }
}
