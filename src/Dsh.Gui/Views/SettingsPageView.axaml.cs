using Avalonia.Controls;
using Dsh.Gui.ViewModels;

namespace Dsh.Gui.Views;

public sealed partial class SettingsPageView : UserControl
{
    private SettingsPanel? _legacyPanel;

    public SettingsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_legacyPanel is not null || DataContext is not MainViewModel viewModel)
            return;
        _legacyPanel = new SettingsPanel(viewModel.Context, viewModel.CurrentAgent, viewModel.Home)
        {
            Agent = viewModel.CurrentAgent,
        };
        viewModel.AgentChanged += agent => _legacyPanel.Agent = agent;
        LegacyHost.Content = _legacyPanel;
    }
}
