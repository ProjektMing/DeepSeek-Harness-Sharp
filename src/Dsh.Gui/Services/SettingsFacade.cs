using Dsh.Boot;
using Dsh.Core;
using Dsh.Interaction;
using Dsh.Runtime;

namespace Dsh.Gui.Services;

/** settings.yaml 的唯一读写入口: 有对应命令时复用命令的校验与副作用, 无命令时才直接读写。 */
public sealed class SettingsFacade(Context ctx, HarnessHome home)
{
    public event Action? Changed;

    public string SettingsPath => Path.Combine(home.Root, "settings.yaml");

    public HarnessSettings Load() => HarnessSettings.Load(home);

    public void UpdateSafety(bool autoApprove, IReadOnlyList<string> blacklist)
    {
        var settings = Load();
        settings.Safety = new SafetySettings
        {
            AutoApprove = autoApprove,
            Blacklist = [.. blacklist],
        };
        settings.Save(home);
        Changed?.Invoke();
    }

    public async Task<string> RunCommandAsync(IAgent agent, string line)
    {
        var commands = ctx.Get<CommandsService>(CommandsService.ServiceName);
        if (commands is null)
            return $"未知命令: {line}";
        try
        {
            var execution = await commands.Execute(agent, line);
            var text = execution?.Result switch
            {
                CommandResult.Success { Text: { } success } => success,
                CommandResult.Error error => error.Text,
                _ => $"未知命令: {line}",
            };
            Changed?.Invoke();
            return text;
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }
}
