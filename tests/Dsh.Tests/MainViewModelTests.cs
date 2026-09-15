using Avalonia.Threading;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Gui.ViewModels;
using Dsh.Llm;
using Dsh.Persistence;

namespace Dsh.Tests;

/** 主窗口视图模型的行为: 页面/标签切换、会话分组、命令通道、轨迹选中。 */
public sealed class MainViewModelTests
{
    [Fact]
    public async Task PageTabAndSidebarCommands_SwitchState()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        Assert.True(viewModel.IsChatPage);
        viewModel.ShowSettingsCommand.Execute(null);
        Assert.True(viewModel.IsSettingsPage);
        viewModel.ShowMarketCommand.Execute(null);
        Assert.True(viewModel.IsMarketPage);
        viewModel.ShowChatCommand.Execute(null);
        Assert.True(viewModel.IsChatPage);

        Assert.False(viewModel.IsTraceTab);
        viewModel.ShowTraceCommand.Execute(null);
        Assert.True(viewModel.IsTraceTab);
        viewModel.ShowConversationCommand.Execute(null);
        Assert.False(viewModel.IsTraceTab);

        Assert.True(viewModel.IsSidebarVisible);
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.False(viewModel.IsSidebarVisible);
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.True(viewModel.IsSidebarVisible);
    }

    [Fact]
    public async Task Sessions_GroupedByWorkspace_AndMarkCurrentSessionSelected()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        var workspace = Assert.Single(viewModel.Workspaces);
        Assert.NotEmpty(workspace.Name);
        var node = Assert.Single(workspace.Sessions);
        Assert.Equal(environment.Agent.Id, node.SessionId);
        Assert.Same(node, viewModel.SelectedSession);
        Assert.True(node.IsSelected);
        Assert.True(node.IsLive);
    }

    [Fact]
    public async Task SnapshotReplay_MapsEventsToMessagesAndTrace()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        var session = environment.Agent.Session;
        session.Append(new UserMessagePayload(MessageFactory.CreateUserText("你好")), new SurfaceOp.Append());
        session.Append(new TurnStartPayload(1));
        session.Append(new RequestContextPayload("test", "test-model"));
        session.Append(new ToolCallPayload(1, 1, ToolCallId.Create("call-1"), "bash", """{"command":"ls"}"""));
        session.Append(new TurnEndPayload(1, new TurnEndReason.Completed()));

        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        Assert.Collection(
            viewModel.Messages,
            message => Assert.Equal(MessageKind.User, message.Kind),
            message => Assert.Equal(MessageKind.Tool, message.Kind));
        Assert.Contains(viewModel.TraceItems, item => item.Kind == TraceKind.Turn);
        Assert.Contains(viewModel.TraceItems, item => item.Kind == TraceKind.Context);
        Assert.Contains(viewModel.TraceItems, item => item.Kind == TraceKind.Tool);
        Assert.False(viewModel.IsBusy);
    }

    /** 运行时上下文注入只应出现一行: 请求元数据(request/context)不重复进正文, 插件名也不该再补一个 @。 */
    [Fact]
    public async Task PluginInjectedContext_RendersSingleRow()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        var session = environment.Agent.Session;
        session.Append(
            new UserMessagePayload(MessageFactory.CreateUserMessage(
                [new TextBlock("runtime context")],
                new PluginMessageSource("@deepseek-ai/dsh-system-prompt"))),
            new SurfaceOp.Append());
        session.Append(new RequestContextPayload("openai-compatible", "deepseek-v4-flash"));

        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        var message = Assert.Single(viewModel.Messages);
        Assert.Equal(MessageKind.Context, message.Kind);
        Assert.Equal("上下文注入 · @deepseek-ai/dsh-system-prompt", message.Text);
        Assert.False(message.HasRole);
        Assert.Contains(viewModel.TraceItems, item => item.Title.StartsWith("请求上下文", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectTraceItem_MarksSelectionAndRequestsScroll()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        var session = environment.Agent.Session;
        session.Append(new UserMessagePayload(MessageFactory.CreateUserText("你好")), new SurfaceOp.Append());
        session.Append(new TurnStartPayload(1));
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        MessageViewModel? scrolled = null;
        viewModel.ScrollRequested += message => scrolled = message;
        var trace = viewModel.TraceItems.First(item => item.Kind == TraceKind.Turn);
        viewModel.SelectTraceItemCommand.Execute(trace);

        Assert.True(trace.IsSelected);
        Assert.Same(viewModel.Messages[^1], scrolled);
    }

    [Fact]
    public async Task Submit_SlashCommand_RunsThroughCommandsService()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        viewModel.Composer.Input = "/plugins";
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.Composer.Input);
        var message = Assert.Single(viewModel.Messages);
        Assert.Equal(MessageKind.System, message.Kind);
        Assert.Contains("dsh-", message.Text);
    }

    [Fact]
    public async Task Submit_TextMessage_IsRenderedOnceFromSessionEvent()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        viewModel.Composer.Input = "你好";
        await viewModel.SubmitCommand.ExecuteAsync(null);

        // 提交本身不回显, 消息要等会话事件到达才出现。
        Assert.Empty(viewModel.Messages);
        for (var attempt = 0; attempt < 200 && viewModel.Messages.Count == 0; attempt += 1)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Single(viewModel.Messages, message => message.Kind == MessageKind.User);
        Assert.Equal("你好", viewModel.Messages[0].Text);
    }

    [Fact]
    public async Task Submit_EmptyInput_DoesNothing()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        viewModel.Composer.Input = "   ";
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Messages);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SettingsFacade_WritesSafetySettingsThroughSingleEntry()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        using var viewModel = new MainViewModel(environment.App, environment.Agent);

        viewModel.Settings.UpdateSafety(autoApprove: true, ["bash: rm -rf /*"]);

        var reloaded = viewModel.Settings.Load();
        var safety = Assert.IsType<SafetySettings>(reloaded.Safety);
        Assert.True(safety.AutoApprove);
        Assert.Contains("bash: rm -rf /*", safety.Blacklist);
        Assert.True(File.Exists(viewModel.Settings.SettingsPath));
    }

    [Fact]
    public async Task RestoredSession_KeepsListedAndReplayable()
    {
        using var environment = await GuiTestEnvironment.CreateAsync();
        var sessionId = environment.Agent.Id;
        environment.Agent.Session.Append(new UserMessagePayload(MessageFactory.CreateUserText("第一条")), new SurfaceOp.Append());
        await environment.App.Ctx.Get<SessionStore>(SessionStore.ServiceName)!.Flush(environment.Agent.Session);

        using var restarted = await environment.RestartAsync();
        var agents = restarted.App.Ctx.Get<AgentRegistry>(AgentRegistry.ServiceName)!;
        var handle = await agents.Resume(new ResumeAgentOptions(sessionId, new AgentOptions("test", "test-model")));
        var resumed = (AgentLoopAgent)handle.Agent;
        using var viewModel = new MainViewModel(restarted.App, resumed);

        Assert.Collection(viewModel.Messages, message => Assert.Equal("第一条", message.Text));
        var nodes = viewModel.Workspaces.SelectMany(workspace => workspace.Sessions).ToList();
        var node = Assert.Single(nodes, candidate => candidate.SessionId == sessionId);
        Assert.Same(resumed, node.Agent);
        Assert.True(node.IsLive);
    }
}
