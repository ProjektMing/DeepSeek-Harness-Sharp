using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Gui.Services;
using Dsh.Gui.ViewModels;

namespace Dsh.Gui.Views;

public sealed partial class MainWindow : Window
{
    private const double ScreenMargin = 60;
    private const double TitleBarHeight = 34;

    private readonly GuiSettings? _guiSettings;
    private CloseActionKind _closeAction = CloseActionKind.Tray;
    private TrayIcon? _tray;
    private bool _forceQuit;
    private ClosePromptDialog? _closePrompt;

    /** XAML 编译器要求存在无参构造, 实际启动路径走下面的带参构造。 */
    public MainWindow() => InitializeComponent();

    public MainWindow(HarnessApp app, AgentLoopAgent agent) : this()
    {
        _guiSettings = new GuiSettings(app.Home);
        _closeAction = ClosePolicy.Parse(_guiSettings.Load().CloseAction);
        ViewModel = new MainViewModel(app, agent);
        DataContext = ViewModel;
        ViewModel.DecisionRequested += ShowDecisionAsync;
        ViewModel.CopyRequested += CopyToClipboard;
        ViewModel.FilePicker = PickAsync;
        ViewModel.Preferences.Applied += ApplyAppearance;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        SetUpTitleBar();
        SetUpTray();
    }

    public MainViewModel? ViewModel { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestoreBounds();
        ClampToScreen();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_forceQuit && _closeAction != CloseActionKind.Quit)
        {
            e.Cancel = true;
            try
            {
                if (_closeAction == CloseActionKind.Ask)
                {
                    await AskCloseActionAsync();
                    return;
                }
            }
            catch (Exception error)
            {
                // 询问窗口自身出错时不能让 async void 把进程带走: 退回最小化到托盘。
                ViewModel?.SetStatus($"关闭询问失败, 已最小化到托盘: {error.Message}");
            }
            HideToTray();
            return;
        }
        ViewModel?.PersistWindowBounds(Width, Height, Position.X, Position.Y, WindowState == WindowState.Maximized);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.DecisionRequested -= ShowDecisionAsync;
            viewModel.CopyRequested -= CopyToClipboard;
            viewModel.Preferences.Applied -= ApplyAppearance;
            viewModel.Dispose();
        }
        _tray?.Dispose();
        base.OnClosed(e);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void SetUpTitleBar()
    {
        if (!OperatingSystem.IsWindows())
            return;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        TitleBar.IsVisible = true;
    }

    private void SetUpTray()
    {
        if (OperatingSystem.IsLinux() && !ClosePolicy.TraySupported)
            return;
        try
        {
            var menu = new NativeMenu();
            menu.Add(new NativeMenuItem("显示主窗口") { Command = new RelayCommand(ShowFromTray) });
            menu.Add(new NativeMenuItem("新会话") { Command = new RelayCommand(() => ViewModel?.NewSessionCommand.Execute(null)) });
            menu.Add(new NativeMenuItem("取消当前任务") { Command = new RelayCommand(() => ViewModel?.CancelTaskCommand.Execute(null)) });
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem("设置") { Command = new RelayCommand(() => ViewModel?.ShowSettingsCommand.Execute(null)) });
            menu.Add(new NativeMenuItem("退出") { Command = new RelayCommand(Quit) });
            _tray = new TrayIcon
            {
                Icon = LoadTrayIcon(),
                ToolTipText = "DeepSeek Harness",
                Menu = menu,
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => ShowFromTray();
        }
        catch (Exception)
        {
            // 托盘属于锦上添花: 没有通知区域时静默降级为普通窗口。
            _tray = null;
        }
    }

    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Dsh.Gui/Assets/icon.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task AskCloseActionAsync()
    {
        if (_closePrompt is not null)
            return;
        _closePrompt = new ClosePromptDialog();
        var result = await _closePrompt.ShowDialog<(bool Quit, bool Remember)?>(this);
        _closePrompt = null;
        if (result is not { } choice)
            return;
        if (choice.Remember && _guiSettings is not null)
        {
            _closeAction = choice.Quit ? CloseActionKind.Quit : CloseActionKind.Tray;
            _guiSettings.Save(_guiSettings.Load() with { CloseAction = ClosePolicy.Wire(_closeAction) });
        }
        if (choice.Quit)
            Quit();
        else
            HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ViewModel?.SetStatus("已最小化到托盘，托盘菜单可退出");
    }

    private void Quit()
    {
        _forceQuit = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }
        Close();
    }

    private void RestoreBounds()
    {
        if (_guiSettings?.Load() is not { RememberBounds: true } snapshot)
            return;
        if (snapshot.WindowWidth > 0)
            Width = snapshot.WindowWidth;
        if (snapshot.WindowHeight > 0)
            Height = snapshot.WindowHeight;
        if (snapshot.WindowX is { } x && snapshot.WindowY is { } y)
            Position = new PixelPoint((int)x, (int)y);
        if (snapshot.WindowMaximized)
            WindowState = WindowState.Maximized;
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

    private void ApplyAppearance()
    {
        if (Application.Current is { } app && _guiSettings is not null)
            ThemeService.Apply(app, _guiSettings.Load());
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        if (e.Key == Key.W)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Q)
        {
            e.Handled = true;
            Quit();
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async Task<object?> ShowDecisionAsync(DecisionViewModel decision)
    {
        var dialog = new DecisionDialog(decision);
        return await dialog.ShowDialog<object?>(this);
    }

    private void CopyToClipboard(string text) => _ = CopyAsync(text);

    private async Task CopyAsync(string text)
    {
        try
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
            ViewModel?.SetStatus("已复制到剪贴板");
        }
        catch (Exception error)
        {
            ViewModel?.SetStatus($"复制失败: {error.Message}");
        }
    }

    private async Task<IReadOnlyList<string>> PickAsync(bool allowMultiple, bool folders)
    {
        if (StorageProvider is not { } storage)
            return [];
        if (folders)
        {
            var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "选择文件夹" });
            return [.. picked.Select(folder => folder.TryGetLocalPath()).Where(path => path is not null).Select(path => path!)];
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = allowMultiple, Title = "选择文件" });
        return [.. files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Select(path => path!)];
    }
}
