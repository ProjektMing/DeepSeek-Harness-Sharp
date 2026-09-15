using CommunityToolkit.Mvvm.ComponentModel;

namespace Dsh.Gui.ViewModels;

public enum TraceKind
{
    Turn,
    Reasoning,
    Tool,
    Approval,
    Context,
    Command,
    Other,
}

/** 轨迹 Tab 的一条事件: 点击可回跳到会话流中的对应位置。 */
public sealed partial class TraceItemViewModel : ObservableObject
{
    public required long Seq { get; init; }

    public required TraceKind Kind { get; init; }

    public required string Title { get; init; }

    public string Detail { get; init; } = "";

    public string Time { get; init; } = "";

    /** 对应的会话流记录, 用于点击跳转。 */
    public MessageViewModel? Message { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
