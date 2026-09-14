using Dsh.Runtime.Events;

namespace Dsh.Skills;

public sealed record SkillsChangedNotification : INotification
{
    public static string EventName => "skills/change";
}
