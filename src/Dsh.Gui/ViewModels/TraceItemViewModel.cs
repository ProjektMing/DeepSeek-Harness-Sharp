using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

/** 轨迹 Tab 的一条事件: 点击可回跳到会话流中的对应位置, 详情可展开。 */
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

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasDetail => Detail.Length > 0;

    public string ExpandLabel => IsExpanded ? "▾" : "▸";

    public string KindLabel => Kind switch
    {
        TraceKind.Turn => "轮次",
        TraceKind.Reasoning => "思考",
        TraceKind.Tool => "工具",
        TraceKind.Approval => "审批",
        TraceKind.Context => "上下文",
        TraceKind.Command => "命令",
        _ => "其它",
    };

    public static string Wire(TraceKind kind) => kind switch
    {
        TraceKind.Turn => "turn",
        TraceKind.Reasoning => "reasoning",
        TraceKind.Tool => "tool",
        TraceKind.Approval => "approval",
        TraceKind.Context => "context",
        TraceKind.Command => "command",
        _ => "other",
    };

    public static TraceKind? Parse(string wire) => wire switch
    {
        "turn" => TraceKind.Turn,
        "reasoning" => TraceKind.Reasoning,
        "tool" => TraceKind.Tool,
        "approval" => TraceKind.Approval,
        "context" => TraceKind.Context,
        "command" => TraceKind.Command,
        "other" => TraceKind.Other,
        _ => null,
    };

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandLabel));

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
