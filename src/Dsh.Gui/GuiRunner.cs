using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Gui.Views;
using Dsh.Llm;

namespace Dsh.Gui;

public static class GuiRunner
{
    public static async Task<int> Run(
        HarnessApp app,
        string cwd,
        string? resumeSessionId = null)
    {
        var agents = app.Ctx.Get<AgentRegistry>(AgentRegistry.ServiceName)!;
        var agent = await OpenAgentAsync(app, agents, cwd, resumeSessionId);

        var (exitCode, lastSession) = await RunAvaloniaAsync(app, agent);
        var sessions = app.Ctx.Get<SessionStore>(SessionStore.ServiceName)!;
        await sessions.Flush(lastSession);
        return exitCode;
    }

    /** --session 指定时直接打开历史会话; 恢复失败则退回新建, 不让 GUI 起不来。 */
    private static async Task<AgentLoopAgent> OpenAgentAsync(
        HarnessApp app,
        AgentRegistry agents,
        string cwd,
        string? resumeSessionId)
    {
        var options = new AgentOptions(app.Provider, app.Model, app.ReasoningEffort is null ? null : ReasoningEffortId.Create(app.ReasoningEffort));
        if (resumeSessionId is not null)
        {
            try
            {
                var resumed = (AgentLoopAgent)(await agents.Resume(new ResumeAgentOptions(SessionId.Create(resumeSessionId), options))).Agent;
                await resumed.WhenIdle();
                return resumed;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"dsh: session \"{resumeSessionId}\" cannot be resumed: {error.Message}");
            }
        }
        var created = (AgentLoopAgent)(await agents.Create(new CreateAgentOptions(
            SessionId.Create($"session-{Guid.NewGuid()}"),
            cwd,
            options))).Agent;
        await created.WhenIdle();
        return created;
    }

    private static Task<(int ExitCode, Session Session)> RunAvaloniaAsync(HarnessApp app, AgentLoopAgent agent)
    {
        var done = new TaskCompletionSource<(int, Session)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                MainWindow? window = null;
                App.StartupWindowFactory = () =>
                {
                    window = new MainWindow(app, agent);
                    return window;
                };
                var exitCode = BuildApp().StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
                done.TrySetResult((exitCode, window?.ViewModel?.CurrentAgent.Session ?? agent.Session));
            }
            catch (Exception error)
            {
                done.TrySetException(error);
            }
        });
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static AppBuilder BuildApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
