using Dsh.Runtime.Events;

namespace Dsh.Core;

public sealed record SessionCreatedNotification(Session Session) : INotification
{
    public static string EventName => "session/created";
}

public sealed record SessionDisposedNotification(Session Session) : INotification
{
    public static string EventName => "session/disposed";
}

public sealed record SessionEventNotification(Session Session, SessionEvent Event) : INotification
{
    public static string EventName => "session/event";
}

public sealed record SessionFlushNotification(Session Session) : INotification
{
    public static string EventName => "session/flush";
}
