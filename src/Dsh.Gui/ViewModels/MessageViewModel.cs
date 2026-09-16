using System.Text;
using Avalonia.Layout;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

public enum MessageFeedback
{
    None,
    Liked,
    Disliked,
}

/** 会话流里的一条记录; 流式内容按 16ms 合并刷新, 长内容可折叠。 */
public sealed partial class MessageViewModel : ObservableObject
{
    private const int CoalesceMilliseconds = 16;
    public const int PreviewChars = 140;

    private readonly StringBuilder _buffer = new();
    private bool _flushScheduled;

    public MessageViewModel(string role, string text, MessageKind kind, bool streaming)
    {
        Role = role;
        Kind = kind;
        IsStreaming = streaming;
        IsExpanded = !IsFoldable;
        _buffer.Append(text);
        Text = text;
    }

    public string Role { get; }

    public MessageKind Kind { get; }

    [ObservableProperty]
    private string _text = "";

    /** 折叠状态下展示的一行摘要; 上下文注入行把注入正文放在 Detail 里。 */
    [ObservableProperty]
    private string _detail = "";

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private MessageFeedback _feedback;

    public bool IsUser => Kind == MessageKind.User;

    public bool IsAssistant => Kind == MessageKind.Assistant;

    public bool IsReasoning => Kind == MessageKind.Reasoning;

    public bool IsTool => Kind is MessageKind.Tool or MessageKind.Result;

    public bool IsContext => Kind == MessageKind.Context;

    public bool IsSystem => Kind is MessageKind.System or MessageKind.Approval;

    public bool ShowBody => !IsFoldable || IsExpanded;

    public bool ShowMarkdown => IsAssistant && ShowBody;

    public bool ShowPlainBody => ShowBody && !IsAssistant;

    public bool ShowPreview => IsFoldable && !IsExpanded;

    public bool ShowDetail => IsContext && IsExpanded && Detail.Length > 0;

    public bool IsLiked => Feedback == MessageFeedback.Liked;

    public bool IsDisliked => Feedback == MessageFeedback.Disliked;

    /** 上下文注入行自带说明文案, 不再重复显示角色标签。 */
    public bool HasRole => Kind != MessageKind.Context;

    public HorizontalAlignment Align => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /** 思考/工具调用/工具结果/上下文注入默认折叠; 正文始终展开。 */
    public bool IsFoldable => Kind is MessageKind.Reasoning or MessageKind.Tool or MessageKind.Result or MessageKind.Context;

    public string FoldLabel => IsExpanded ? "▾ 折叠" : "▸ 展开";

    public bool HasDetail => Detail.Length > 0;

    public string Preview
    {
        get
        {
            var flat = Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= PreviewChars ? flat : flat[..PreviewChars] + "…";
        }
    }

    public bool ShowActions => Kind == MessageKind.Assistant && !IsStreaming;

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
        OnPropertyChanged(nameof(Preview));
        Updated?.Invoke(this);
    }

    partial void OnDetailChanged(string value)
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(ShowDetail));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(FoldLabel));
        OnPropertyChanged(nameof(ShowBody));
        OnPropertyChanged(nameof(ShowMarkdown));
        OnPropertyChanged(nameof(ShowPlainBody));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowDetail));
    }

    partial void OnFeedbackChanged(MessageFeedback value)
    {
        OnPropertyChanged(nameof(IsLiked));
        OnPropertyChanged(nameof(IsDisliked));
    }

    [RelayCommand]
    private void ToggleFold() => IsExpanded = !IsExpanded;
}
