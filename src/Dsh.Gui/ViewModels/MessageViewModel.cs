using System.Text;
using Avalonia.Layout;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dsh.Gui.ViewModels;

public enum MessageKind
{
    User,
    Assistant,
    Reasoning,
    Context,
    Tool,
    Result,
    System,
    Approval,
}

/** 会话流里的一条记录; 流式内容按 16ms 合并刷新, 避免每个 token 触发一次布局。 */
public sealed partial class MessageViewModel : ObservableObject
{
    private const int CoalesceMilliseconds = 16;

    private readonly StringBuilder _buffer = new();
    private bool _flushScheduled;

    public MessageViewModel(string role, string text, MessageKind kind, bool streaming)
    {
        Role = role;
        Kind = kind;
        IsStreaming = streaming;
        _buffer.Append(text);
        Text = text;
    }

    public string Role { get; }

    public MessageKind Kind { get; }

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isStreaming;

    public bool IsUser => Kind == MessageKind.User;

    /** 上下文注入行自带说明文案, 不再重复显示角色标签。 */
    public bool HasRole => Kind != MessageKind.Context;

    public HorizontalAlignment Align => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /** 文本发生变化(流式增量合并后)时触发, 供视图保持滚动到底部。 */
    public event Action<MessageViewModel>? Updated;

    public void Append(string delta)
    {
        _buffer.Append(delta);
        if (_flushScheduled)
            return;
        _flushScheduled = true;
        DispatcherTimer.RunOnce(FlushPending, TimeSpan.FromMilliseconds(CoalesceMilliseconds));
    }

    public void SetText(string text)
    {
        _buffer.Clear();
        _buffer.Append(text);
        FlushPending();
    }

    public void FlushPending()
    {
        _flushScheduled = false;
        var text = _buffer.ToString();
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return;
        Text = text;
        Updated?.Invoke(this);
    }
}
