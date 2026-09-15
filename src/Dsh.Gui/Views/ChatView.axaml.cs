using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dsh.Gui.ViewModels;
namespace Dsh.Gui.Views;

public sealed partial class ChatView : UserControl
{
    private MainViewModel? _viewModel;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
            return;
        _viewModel.ScrollRequested += ScrollToMessage;
        _viewModel.MessagesChanged += ScrollToEnd;
    }

    private void Detach()
    {
        if (_viewModel is null)
            return;
        _viewModel.ScrollRequested -= ScrollToMessage;
        _viewModel.MessagesChanged -= ScrollToEnd;
        _viewModel = null;
    }

    private void ScrollToMessage(MessageViewModel message) => MessageList.ScrollIntoView(message);

    private void ScrollToEnd()
    {
        if (_viewModel?.Messages.Count > 0)
            MessageList.ScrollIntoView(_viewModel.Messages[^1]);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _viewModel is null)
            return;
        e.Handled = true;
        if (_viewModel.SubmitCommand.CanExecute(null))
            _viewModel.SubmitCommand.Execute(null);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // TextBox 会先消费 Enter(AcceptsReturn), 必须在隧道阶段拦截。
        InputBox.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        InputBox.Focus();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        InputBox.RemoveHandler(InputElement.KeyDownEvent, OnInputKeyDown);
        Detach();
        base.OnUnloaded(e);
    }
}
