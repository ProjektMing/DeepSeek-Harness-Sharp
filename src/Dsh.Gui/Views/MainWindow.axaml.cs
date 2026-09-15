using Avalonia.Controls;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Gui.ViewModels;

namespace Dsh.Gui.Views;

public sealed partial class MainWindow : Window
{
    private const double ScreenMargin = 60;

    /** XAML 编译器要求存在无参构造, 实际启动路径走下面的带参构造。 */
    public MainWindow() => InitializeComponent();

    public MainWindow(HarnessApp app, AgentLoopAgent agent) : this()
    {
        ViewModel = new MainViewModel(app, agent);
        DataContext = ViewModel;
        ViewModel.ApprovalRequested += ShowApprovalAsync;
    }

    public MainViewModel? ViewModel { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToScreen();
    }

    /** XAML 里的尺寸是逻辑单位, 高 DPI 下可能超过屏幕工作区; 超出部分会被系统裁掉且不可见。 */
    private void ClampToScreen()
    {
        if (Screens.Primary is not { } screen)
            return;
        var availableWidth = screen.WorkingArea.Width / screen.Scaling;
        var availableHeight = screen.WorkingArea.Height / screen.Scaling;
        Width = Math.Min(Width, Math.Max(MinWidth, availableWidth - ScreenMargin));
        Height = Math.Min(Height, Math.Max(MinHeight, availableHeight - ScreenMargin));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.ApprovalRequested -= ShowApprovalAsync;
            viewModel.Dispose();
        }
        base.OnClosed(e);
    }

    private Task<ApprovalOutcome> ShowApprovalAsync(ApprovalRequest request)
    {
        var answer = new TaskCompletionSource<ApprovalOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new ApprovalDialog(request, outcome => answer.TrySetResult(outcome));
        dialog.ShowDialog(this);
        return answer.Task;
    }
}
