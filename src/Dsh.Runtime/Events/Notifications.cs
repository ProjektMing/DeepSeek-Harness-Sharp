namespace Dsh.Runtime.Events;

/** 通知类型即事件身份;EventName 仅用于日志/审计可读名。 */
public interface INotification
{
    static abstract string EventName { get; }
}

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    ValueTask Handle(TNotification notification);
}

public interface IBailHandler<in TNotification> where TNotification : INotification
{
    ValueTask<object?> Handle(TNotification notification);
}

public interface IWaterfallHandler<in TNotification> where TNotification : INotification
{
    ValueTask<object?> Handle(TNotification notification, Func<ValueTask<object?>> next);
}

internal sealed class DelegateNotificationHandler<TNotification>(Action<TNotification> handler) : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public ValueTask Handle(TNotification notification)
    {
        handler(notification);
        return ValueTask.CompletedTask;
    }
}

internal sealed class AsyncNotificationHandler<TNotification>(Func<TNotification, ValueTask> handler) : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public ValueTask Handle(TNotification notification) => handler(notification);
}

internal sealed class DelegateBailHandler<TNotification>(Func<TNotification, ValueTask<object?>> handler) : IBailHandler<TNotification>
    where TNotification : INotification
{
    public ValueTask<object?> Handle(TNotification notification) => handler(notification);
}

internal sealed class DelegateWaterfallHandler<TNotification>(Func<TNotification, Func<ValueTask<object?>>, ValueTask<object?>> handler) : IWaterfallHandler<TNotification>
    where TNotification : INotification
{
    public ValueTask<object?> Handle(TNotification notification, Func<ValueTask<object?>> next) => handler(notification, next);
}

public static class EventContextExtensions
{
    public static Func<bool> On<TNotification>(this Context ctx, Action<TNotification> handler, EventOptions? options = null)
        where TNotification : INotification
        => ctx.Events.On(new DelegateNotificationHandler<TNotification>(handler), options);

    public static Func<bool> OnAsync<TNotification>(this Context ctx, Func<TNotification, ValueTask> handler, EventOptions? options = null)
        where TNotification : INotification
        => ctx.Events.On(new AsyncNotificationHandler<TNotification>(handler), options);

    public static Func<bool> OnBail<TNotification>(this Context ctx, Func<TNotification, ValueTask<object?>> handler, EventOptions? options = null)
        where TNotification : INotification
        => ctx.Events.OnBail(new DelegateBailHandler<TNotification>(handler), options);

    public static Func<bool> OnWaterfall<TNotification>(this Context ctx, Func<TNotification, Func<ValueTask<object?>>, ValueTask<object?>> handler, EventOptions? options = null)
        where TNotification : INotification
        => ctx.Events.OnWaterfall(new DelegateWaterfallHandler<TNotification>(handler), options);
}
