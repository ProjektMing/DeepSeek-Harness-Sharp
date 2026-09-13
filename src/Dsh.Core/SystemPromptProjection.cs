using Dsh.Llm;

namespace Dsh.Core;

public sealed class SystemPromptProjection
{
    public const string Source = "@deepseek-ai/dsh-system-prompt";

    private readonly Session _session;

    public SystemPromptProjection(Session session)
    {
        _session = session;
    }

    public sealed record Commit(Message Message, SurfaceOp Op, IReadOnlyList<long>? SourceSeqs);

    public IReadOnlyList<Commit> Project(string rendered, bool inHistory, bool startsSeries)
    {
        var nodes = SystemNodes();
        if (nodes.Count == 0)
            return [new Commit(CreateSystemMessage(rendered), new SurfaceOp.Append(), null)];
        var head = nodes[0];
        var latest = nodes.LastOrDefault(node => node.Text.Length > 0, head);
        if (!inHistory || startsSeries || rendered.Length == 0)
        {
            var updates = new List<Commit>();
            foreach (var node in nodes.Skip(1).Where(node => node.Text.Length > 0))
                updates.Add(Replace(node.Seq, ""));
            if (head.Text != rendered)
                updates.Add(Replace(head.Seq, rendered));
            return updates;
        }
        if (latest.Text == rendered)
            return [];
        return [new Commit(CreateSystemMessage(rendered), new SurfaceOp.Append(), null)];
    }

    private List<(long Seq, string Text)> SystemNodes()
    {
        var nodes = new List<(long Seq, string Text)>();
        foreach (var seq in _session.SurfaceManager.Nodes)
        {
            if (_session.EventAt(seq) is not { Data: SystemMessagePayload payload } sessionEvent)
                continue;
            nodes.Add((sessionEvent.Seq, TextOf(payload.Message)));
        }
        return nodes;
    }

    private static string TextOf(Message message)
        => message.Content.Count == 0
            ? ""
            : string.Concat(message.Content.OfType<TextBlock>().Select(block => block.Text));

    private static Message CreateSystemMessage(string text)
        => MessageFactory.CreateSystemMessage(
            text.Length == 0 ? [] : [new TextBlock(text)],
            new PluginMessageSource(Source));

    private static Commit Replace(long seq, string text)
        => new(CreateSystemMessage(text), new SurfaceOp.Replace(seq, seq), [seq]);
}
