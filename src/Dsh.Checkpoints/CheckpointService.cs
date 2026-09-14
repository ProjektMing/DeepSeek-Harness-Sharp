using System.Threading.Channels;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Llm;
using Dsh.Persistence;
using Dsh.Runtime;

namespace Dsh.Checkpoints;

public sealed record CheckpointPolicy
{
    public required bool Enabled { get; init; }
    public required int MaxPoints { get; init; }
    public required int KeepDays { get; init; }

    public static CheckpointPolicy Resolve(HarnessSettings? settings, object? config)
    {
        var policy = new CheckpointPolicy
        {
            Enabled = settings?.Checkpoints?.Enabled ?? false,
            MaxPoints = settings?.Checkpoints?.MaxPoints ?? CheckpointsSettings.DefaultMaxPoints,
            KeepDays = settings?.Checkpoints?.KeepDays ?? CheckpointsSettings.DefaultKeepDays,
        };
        if (config is not IReadOnlyDictionary<string, object?> map)
        {
            return new CheckpointPolicy
            {
                Enabled = policy.Enabled,
                MaxPoints = Math.Max(1, policy.MaxPoints),
                KeepDays = Math.Max(1, policy.KeepDays),
            };
        }
        var maxPoints = map.TryGetValue("max_points", out var maxValue) && maxValue is long max ? (int)max : policy.MaxPoints;
        var keepDays = map.TryGetValue("keep_days", out var keepValue) && keepValue is long keep ? (int)keep : policy.KeepDays;
        return new CheckpointPolicy
        {
            Enabled = map.TryGetValue("enabled", out var enabled) && enabled is bool flag ? flag : policy.Enabled,
            MaxPoints = Math.Max(1, maxPoints),
            KeepDays = Math.Max(1, keepDays),
        };
    }
}

public sealed class CheckpointService : Service, IDisposable
{
    public const string ServiceName = "checkpoints";

    private sealed record ProjectRepo(ShadowGit Git, CheckpointLog Log);

    private sealed record SnapshotRequest(SessionId Session, long Seq, string Cwd, string Reason);

    private readonly CheckpointPolicy _policy;
    private readonly string _homeRoot;
    private readonly Dictionary<string, ProjectRepo> _repos = new(StringComparer.Ordinal);
    private readonly Channel<SnapshotRequest> _queue = Channel.CreateBounded<SnapshotRequest>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly Lock _sync = new();

    public CheckpointService(Context ctx, CheckpointPolicy policy, string homeRoot) : base(ctx, ServiceName)
    {
        _policy = policy;
        _homeRoot = homeRoot;
        _ = ctx.Get<SessionStore>(SessionStore.ServiceName)
            ?? throw new InvalidOperationException("checkpoints requires the sessionStore service");
        _worker = Task.Run(ProcessAsync);
        ctx.On(SessionStore.EventEvent, (_, args) =>
        {
            Observe((Session)args[0]!, (SessionEvent)args[1]!);
            return new ValueTask<object?>();
        }, new EventOptions { Global = true });
    }

    public int MaxPoints => _policy.MaxPoints;

    public int KeepDays => _policy.KeepDays;

    public IReadOnlyList<CheckpointPoint> PointsFor(string cwd)
        => RepoForOrNull(cwd)?.Log.Points ?? [];

    public async Task<string> RestoreFilesAsync(string cwd, int index, CancellationToken signal)
    {
        var repo = RepoForOrNull(cwd)
            ?? throw new InvalidOperationException("no checkpoints recorded for this project");
        var points = repo.Log.Points;
        if (index < 0 || index >= points.Count)
            throw new InvalidOperationException($"checkpoint index out of range: {index} (have {points.Count})");
        var point = points[index];
        await repo.Git.RestoreAsync(point.Commit, signal);
        return point.Commit;
    }

    private void Observe(Session session, SessionEvent sessionEvent)
    {
        var reason = sessionEvent.Data switch
        {
            ToolResultPayload => "tool/result",
            TurnEndPayload => "turn/end",
            _ => null,
        };
        if (reason is null)
            return;
        var cwd = session.Header.Cwd;
        if (cwd is not { Length: > 0 })
            return;
        _queue.Writer.TryWrite(new SnapshotRequest(session.Header.Id, sessionEvent.Seq, cwd, reason));
    }

    private async Task ProcessAsync()
    {
        var reader = _queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_stop.Token))
            {
                SnapshotRequest? request = null;
                while (reader.TryRead(out var item))
                    request = item;
                if (request is null)
                    continue;
                try
                {
                    await TakeSnapshotAsync(request, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception error)
                {
                    Ctx.Logger.Error("%s", error);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TakeSnapshotAsync(SnapshotRequest request, CancellationToken signal)
    {
        var repo = RepoFor(request.Cwd);
        await repo.Git.InitAsync(signal);
        var tree = await repo.Git.StageTreeAsync(signal);
        var last = repo.Log.Points.LastOrDefault();
        if (last is not null && await repo.Git.TreeOfAsync(last.Commit, signal) == tree)
            return;
        var message = $"checkpoint session={request.Session.Value} seq={request.Seq} reason={request.Reason} at={DateTimeOffset.Now:O}";
        var commit = await repo.Git.CommitTreeAsync(tree, message, signal);
        repo.Log.Append(new CheckpointPoint
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Commit = commit,
            Seq = request.Seq,
            Session = request.Session.Value,
            Reason = request.Reason,
        });
        var removed = repo.Log.Prune(_policy.MaxPoints, _policy.KeepDays, DateTimeOffset.UtcNow);
        if (removed.Count > 0)
        {
            await repo.Git.DropAsync(removed.Select(point => point.Commit), signal);
            Ctx.Logger.Info("%s", $"checkpoints pruned {removed.Count} point(s) for {request.Cwd}");
        }
    }

    private ProjectRepo RepoFor(string cwd)
    {
        lock (_sync)
        {
            if (_repos.TryGetValue(cwd, out var existing))
                return existing;
            var root = Path.Combine(_homeRoot, "checkpoints", JsonlLayout.ProjectKey(cwd));
            var created = new ProjectRepo(
                new ShadowGit(Path.Combine(root, "repo.git"), cwd),
                new CheckpointLog(Path.Combine(root, "points.jsonl")));
            _repos[cwd] = created;
            return created;
        }
    }

    private ProjectRepo? RepoForOrNull(string cwd)
    {
        var repo = RepoFor(cwd);
        return repo.Log.Points.Count == 0 && !Directory.Exists(repo.Git.GitDir) ? null : repo;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }
}
