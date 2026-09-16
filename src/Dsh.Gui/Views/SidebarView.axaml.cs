using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dsh.Gui.ViewModels;

namespace Dsh.Gui.Views;

public sealed partial class SidebarView : UserControl
{
    public SidebarView() => InitializeComponent();

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: SessionNodeViewModel node } || DataContext is not MainViewModel viewModel)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            viewModel.CommitRenameCommand.Execute(node);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            viewModel.CancelRenameCommand.Execute(node);
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SessionNodeViewModel node } || DataContext is not MainViewModel viewModel)
            return;
        if (node.IsRenaming)
            viewModel.CommitRenameCommand.Execute(node);
    }
}
