using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Dsh.Gui.Views;

/** 关闭主窗口时询问最小化到托盘还是直接退出; null 表示用户没有做出选择。 */
public sealed class ClosePromptDialog : Window
{
    private readonly CheckBox _remember = new() { Content = "记住我的选择（可在设置中修改）" };

    public ClosePromptDialog()
    {
        Title = "关闭 DeepSeek Harness";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ApplyTheme();

        var tray = new Button { Content = "最小化到托盘", MinWidth = 120, IsDefault = true, Classes = { "Primary" } };
        tray.Click += (_, _) => Complete(false);
        var quit = new Button { Content = "直接退出", MinWidth = 120, Classes = { "Ghost" } };
        quit.Click += (_, _) => Complete(true);

        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "最小化到托盘还是直接退出？", TextWrapping = TextWrapping.Wrap },
                _remember,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { tray, quit },
                },
            },
        };
    }

    public static async Task<(bool Quit, bool Remember)?> ShowAsync(Window owner)
    {
        var dialog = new ClosePromptDialog();
        return await dialog.ShowDialog<(bool Quit, bool Remember)?>(owner);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }
        base.OnKeyDown(e);
    }

    private void Complete(bool quit)
    {
        (bool Quit, bool Remember)? result = (quit, _remember.IsChecked == true);
        Close(result);
    }

    private void ApplyTheme()
    {
        if (Application.Current is not { } app)
            return;
        if (app.TryFindResource("Brush.Bg.Card", out var background) && background is IBrush brush)
            Background = brush;
        if (app.TryFindResource("Font.Ui", out var font) && font is FontFamily family)
            FontFamily = family;
    }
}
