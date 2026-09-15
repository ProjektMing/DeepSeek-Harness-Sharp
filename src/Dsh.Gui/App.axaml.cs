using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dsh.Gui.Views;

namespace Dsh.Gui;

public sealed partial class App : Application
{
    /** 主窗口由 GuiRunner 在 Avalonia 线程外准备好依赖后注入。 */
    public static Func<MainWindow>? StartupWindowFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && StartupWindowFactory is not null)
            desktop.MainWindow = StartupWindowFactory();
        base.OnFrameworkInitializationCompleted();
    }
}
