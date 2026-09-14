using Dsh.Core;
using Dsh.Runtime.Events;

namespace Dsh.Goal;

public sealed record GoalChangedNotification(GoalChanged Change, IAgent Agent) : INotification
{
    public static string EventName => "goal/changed";
}
