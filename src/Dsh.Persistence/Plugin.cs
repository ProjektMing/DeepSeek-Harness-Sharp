using Dsh.Runtime;
using Dsh.Runtime.Events;
using Dsh.Core;
using Dsh.Interaction;
using Dsh.Llm;
using Dsh.Plugins;

[assembly: DshPlugin(Dsh.Persistence.Plugin.Persistence)]

namespace Dsh.Persistence;

public sealed class Plugin(string packageName) : IDshPlugin
{
    internal const string Persistence = "@deepseek-ai/dsh-persistence";

    public string[] Inject => packageName switch
    {
        Persistence => [SessionStore.ServiceName, CommandsService.ServiceName],
        _ => throw new InvalidOperationException($"Unknown DSH package '{packageName}'."),
    };

    public IDisposable Apply(Context ctx, object? config) => packageName switch
    {
        Persistence => RegisterPersistence(ctx, config),
        _ => throw new InvalidOperationException($"Unknown DSH package '{packageName}'."),
    };

    private static IDisposable RegisterPersistence(Context ctx, object? _)
    {
        var homePath = ctx.GetProp("dshHomePath") as string
            ?? throw new InvalidOperationException("dshHomePath is required for the persistence plugin");
        var persistence = new JsonlSessionPersistence(Path.Combine(homePath, "sessions"));
        ctx.Provide(ServiceName, persistence);
        var command = SessionCommand.Register(ctx, persistence);
        var wiring = WirePersistence(ctx, persistence);
        return new DisposableBundle(persistence, command, wiring);
    }

    public const string ServiceName = "sessionPersistence";

    internal static IDisposable WirePersistence(Context ctx, ISessionPersistence persistence)
    {
        var handles = new Dictionary<SessionId, ISessionHandle>();
        var created = ctx.On<SessionCreatedNotification>(notification =>
        {
            var session = notification.Session;
            if (handles.ContainsKey(session.Id))
                return;
            var handle = persistence.Create(session.Header, session.InheritedEventCount);
            handles[session.Id] = handle;
            var seed = session.SnapshotEvents();
            if (seed.Count > 0)
                handle.Append(seed);
        });
        var eventHandler = ctx.On<SessionEventNotification>(notification =>
        {
            if (handles.TryGetValue(notification.Session.Id, out var handle))
                handle.Append([notification.Event]);
        });
        var flush = ctx.On<SessionFlushNotification>(notification =>
        {
            if (handles.TryGetValue(notification.Session.Id, out var handle))
                handle.Flush();
        });
        var disposed = ctx.On<SessionDisposedNotification>(notification =>
        {
            if (handles.Remove(notification.Session.Id, out var handle))
            {
                handle.Flush();
                handle.Close();
            }
        });
        return new DisposableBundle(
            new CallbackDisposable(() => created()),
            new CallbackDisposable(() => eventHandler()),
            new CallbackDisposable(() => flush()),
            new CallbackDisposable(() => disposed()));
    }

    private sealed class DisposableBundle(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
                disposable.Dispose();
        }
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
