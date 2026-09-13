using Dsh.Core;
using Dsh.Llm;

namespace Dsh.Tests;

public sealed class SystemPromptProjectionTests
{
    private static Session NewSession() => Session.Create(SessionId.Create($"session-projection-{Guid.NewGuid():N}"));

    private static SessionEvent AppendSystem(Session session, string text)
        => session.Append(
            new SystemMessagePayload(0, 0, MessageFactory.CreateSystemMessage(
                text.Length == 0 ? [] : [new TextBlock(text)],
                new PluginMessageSource(SystemPromptProjection.Source))),
            new SurfaceOp.Append());

    [Fact]
    public void AppendsHeadNodeWhenNoSystemNodeExists()
    {
        var session = NewSession();
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("prompt v1", inHistory: false, startsSeries: true);

        var commit = Assert.Single(commits);
        Assert.IsType<SurfaceOp.Append>(commit.Op);
        Assert.Equal("prompt v1", Assert.IsType<TextBlock>(commit.Message.Content[0]).Text);
        Assert.Equal(MessageRole.System, commit.Message.Role);
        Assert.IsType<PluginMessageSource>(commit.Message.Source);
    }

    [Fact]
    public void AppendsEmptyHeadNodeWhenPromptIsEmpty()
    {
        var session = NewSession();
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("", inHistory: false, startsSeries: true);

        var commit = Assert.Single(commits);
        Assert.IsType<SurfaceOp.Append>(commit.Op);
        Assert.Empty(commit.Message.Content);
    }

    [Fact]
    public void ReplaceRouteRewritesHeadOnChange()
    {
        var session = NewSession();
        var head = AppendSystem(session, "prompt v1");
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("prompt v2", inHistory: false, startsSeries: false);

        var commit = Assert.Single(commits);
        var replace = Assert.IsType<SurfaceOp.Replace>(commit.Op);
        Assert.Equal(head.Seq, replace.Start);
        Assert.Equal(head.Seq, replace.End);
        Assert.Equal([head.Seq], commit.SourceSeqs);
        Assert.Equal("prompt v2", Assert.IsType<TextBlock>(commit.Message.Content[0]).Text);
    }

    [Fact]
    public void ReplaceRouteIsNoOpWhenUnchanged()
    {
        var session = NewSession();
        AppendSystem(session, "prompt v1");
        var projection = new SystemPromptProjection(session);

        Assert.Empty(projection.Project("prompt v1", inHistory: false, startsSeries: false));
    }

    [Fact]
    public void ReplaceRouteClearsNonHeadNodesAndRewritesHead()
    {
        var session = NewSession();
        var head = AppendSystem(session, "prompt v1");
        var tail = AppendSystem(session, "prompt v2");
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("prompt v3", inHistory: false, startsSeries: false);

        Assert.Equal(2, commits.Count);
        var clearTail = commits[0];
        Assert.Equal(new SurfaceOp.Replace(tail.Seq, tail.Seq), clearTail.Op);
        Assert.Empty(clearTail.Message.Content);
        var rewriteHead = commits[1];
        Assert.Equal(new SurfaceOp.Replace(head.Seq, head.Seq), rewriteHead.Op);
        Assert.Equal("prompt v3", Assert.IsType<TextBlock>(rewriteHead.Message.Content[0]).Text);
    }

    [Fact]
    public void InHistoryAppendsOnChangeWithinSeries()
    {
        var session = NewSession();
        AppendSystem(session, "prompt v1");
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("prompt v2", inHistory: true, startsSeries: false);

        var commit = Assert.Single(commits);
        Assert.IsType<SurfaceOp.Append>(commit.Op);
        Assert.Equal("prompt v2", Assert.IsType<TextBlock>(commit.Message.Content[0]).Text);
    }

    [Fact]
    public void InHistoryIsNoOpWhenUnchanged()
    {
        var session = NewSession();
        AppendSystem(session, "prompt v1");
        var projection = new SystemPromptProjection(session);

        Assert.Empty(projection.Project("prompt v1", inHistory: true, startsSeries: false));
    }

    [Fact]
    public void InHistoryRebaselinesOnNewSeries()
    {
        var session = NewSession();
        var head = AppendSystem(session, "prompt v1");
        var projection = new SystemPromptProjection(session);

        var commits = projection.Project("prompt v1", inHistory: true, startsSeries: true);

        Assert.Empty(commits);
        var changeCommits = projection.Project("prompt v2", inHistory: true, startsSeries: true);
        var commit = Assert.Single(changeCommits);
        Assert.Equal(new SurfaceOp.Replace(head.Seq, head.Seq), commit.Op);
    }

    [Fact]
    public void InHistoryComparesAgainstLatestNonEmptyNode()
    {
        var session = NewSession();
        var head = AppendSystem(session, "prompt v1");
        var tail = AppendSystem(session, "prompt v2");
        var projection = new SystemPromptProjection(session);

        Assert.Empty(projection.Project("prompt v2", inHistory: true, startsSeries: false));
        var commits = projection.Project("prompt v1", inHistory: true, startsSeries: false);
        var commit = Assert.Single(commits);
        Assert.IsType<SurfaceOp.Append>(commit.Op);
    }
}
