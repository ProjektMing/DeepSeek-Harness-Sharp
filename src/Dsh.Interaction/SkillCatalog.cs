using Dsh.Runtime.Events;

namespace Dsh.Interaction;

/** 技能目录的消费侧契约:由 skills 插件的 SkillRegistry 实现;未装 skills 插件时 @ 引用技能不可用,其余提及照常。 */
public interface ISkillCatalog
{
    public const string ServiceName = "skills";

    Task<IReadOnlyList<string>> ListNames(CancellationToken cancellationToken = default);
}

public sealed record SkillsChangedNotification : INotification
{
    public static string EventName => "skills/change";
}
