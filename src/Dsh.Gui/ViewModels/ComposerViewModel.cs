using CommunityToolkit.Mvvm.ComponentModel;

namespace Dsh.Gui.ViewModels;

/** 输入胶囊的状态: 只保存用户输入与提交条件, 提交动作由 MainViewModel 承担。 */
public sealed partial class ComposerViewModel : ObservableObject
{
    [ObservableProperty]
    private string _input = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _placeholder = "给智能体发消息";

    [ObservableProperty]
    private bool _canSubmit;

    partial void OnInputChanged(string value) => CanSubmit = !IsBusy && value.Trim().Length > 0;

    partial void OnIsBusyChanged(bool value) => CanSubmit = !value && Input.Trim().Length > 0;
}
