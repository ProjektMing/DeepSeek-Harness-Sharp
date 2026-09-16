namespace Dsh.Memory;

/** 自动捕获用的两段 LLM 提示词:会话摘要与 typed 整固。 */
public static class CapturePrompts
{
    public static string Digest(string transcript) => $"""
        You are summarizing one coding-assistant session for future recall.
        Reply with exactly two lines and nothing else:
        TOPIC: <short topic, at most 12 characters>
        SUMMARY: <2-3 sentences on what was done and learned>

        Session transcript:
        {transcript}
        """;

    public static string Consolidation(string index, string transcript) => $$"""
        You maintain the project memory of a coding assistant.
        Current memory index:
        {{index}}

        Recent session transcript:
        {{transcript}}

        Decide which durable project facts, decisions, constraints, environment commands, or user corrections deserve to be stored.
        Reply with JSON only, no prose:
        {"operations":[{"op":"upsert","section":"Facts|Decisions|Constraints|Commands|Corrections","key":"slug.key","text":"one line"},{"op":"remove","key":"slug.key"},{"op":"noop","reason":"why nothing is stored"}]}
        At most 16 operations; prefer noop over storing weak information.
        Never store secrets, personal preferences, temporary state, command output dumps, or facts already present in the index.
        """;
}
