using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dsh.Boot;
using Dsh.Core;
using Dsh.Gui.Services;
using Dsh.Interaction;
using Dsh.Llm;
using Dsh.Runtime;
using Dsh.Runtime.Events;
using LlmTextBlock = Dsh.Llm.TextBlock;

namespace Dsh.Gui.ViewModels;

public enum AppPage
{
    Chat,
    Settings,
    Market,
}

public enum ChatTab
{
    Conversation,
    Trace,
}

/** 主窗口状态: 会话目录、会话流、轨迹、输入胶囊与页面切换。 */
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const int TracePreviewChars = 160;

    private readonly Context _ctx;
    private readonly AgentRegistry _agents;
    private readonly SettingsFacade _settings;
    private readonly SessionCatalog _catalog;
    private readonly DispatcherBridge<SessionEvent> _events;
    private readonly List<Func<bool>> _unsubscribers = [];

    private AgentLoopAgent _agent;
    private long _renderedSeq;
    private MessageViewModel? _openAssistant;
    private MessageViewModel? _openReasoning;
    private bool _disposed;

    public MainViewModel(HarnessApp app, AgentLoopAgent agent)
    {
        _ctx = app.Ctx;
        _agent = agent;
        Home = app.Home;
        _agents = _ctx.Get<AgentRegistry>(AgentRegistry.ServiceName)!;
        _settings = new SettingsFacade(_ctx, app.Home);
        _catalog = new SessionCatalog(_ctx);
        _events = new DispatcherBridge<SessionEvent>(ApplyEvents);
        _unsubscribers.Add(_ctx.On<SessionEventNotification>(notification => OnSessionEvent(notification)));
        _unsubscribers.Add(_ctx.OnWaterfall<ApprovalRequestNotification>(
            (notification, next) => OnApprovalRequest(notification, next),
            new EventOptions { Global = true }));
        _settings.Changed += OnSettingsChanged;
        RefreshSessions();
        ShowAgent(agent);
    }

    public HarnessHome Home { get; }

    public Context Context => _ctx;

    public ObservableCollection<WorkspaceGroupViewModel> Workspaces { get; } = [];

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public ObservableCollection<TraceItemViewModel> TraceItems { get; } = [];

    public ComposerViewModel Composer { get; } = new();

    public SettingsFacade Settings => _settings;

    public AgentLoopAgent CurrentAgent => _agent;

    /** 视图响应此事件把某条消息滚入视野。 */
    public event Action<MessageViewModel>? ScrollRequested;

    /** 会话流内容发生变化, 视图据此保持贴底滚动。 */
    public event Action? MessagesChanged;

    /** 当前会话切换时触发, 供视图把依赖项(如设置面板)重新指向新 agent。 */
    public event Action<AgentLoopAgent>? AgentChanged;

    /** 审批弹窗由视图实现, ViewModel 只负责把请求转达出去。 */
    public event Func<ApprovalRequest, Task<ApprovalOutcome>>? ApprovalRequested;

    [ObservableProperty]
    private SessionNodeViewModel? _selectedSession;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "准备就绪";

    [ObservableProperty]
    private string _sessionTitle = "";

    [ObservableProperty]
    private string _sessionSubtitle = "";

    [ObservableProperty]
    private AppPage _page = AppPage.Chat;

    [ObservableProperty]
    private ChatTab _tab = ChatTab.Conversation;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private string _pluginText = "";

    [ObservableProperty]
    private bool _isChatPage = true;

    [ObservableProperty]
    private bool _isSettingsPage;

    [ObservableProperty]
    private bool _isMarketPage;

    [ObservableProperty]
    private bool _isTraceTab;

    partial void OnIsBusyChanged(bool value) => Composer.IsBusy = value;

    partial void OnPageChanged(AppPage value)
    {
        IsChatPage = value == AppPage.Chat;
        IsSettingsPage = value == AppPage.Settings;
        IsMarketPage = value == AppPage.Market;
    }

    partial void OnTabChanged(ChatTab value) => IsTraceTab = value == ChatTab.Trace;

    partial void OnSelectedSessionChanged(SessionNodeViewModel? value)
    {
        foreach (var node in AllSessions())
            node.IsSelected = ReferenceEquals(node, value);
        if (value is null || _disposed)
            return;
        if (value.Agent is { } live && ReferenceEquals(live, _agent))
            return;
        _ = SwitchToAsync(value);
    }

    [RelayCommand]
    private void RefreshSessions()
    {
        foreach (var node in AllSessions().ToList())
            node.IsSelected = false;
        Workspaces.Clear();
        foreach (var group in GroupByWorkspace(_catalog.Load()))
            Workspaces.Add(group);
        SelectedSession = FindSession(_agent.Id);
    }

    [RelayCommand]
    private async Task RefreshPluginsAsync() => PluginText = await _settings.RunCommandAsync(_agent, "/plugins");

    [RelayCommand]
    private async Task NewSessionAsync()
    {
        var handle = await _agents.Create(new CreateAgentOptions(
            SessionId.Create($"session-{Guid.NewGuid()}"),
            _agent.Session.Header.Cwd ?? Environment.CurrentDirectory,
            CurrentOptions()));
        var created = (AgentLoopAgent)handle.Agent;
        await created.WhenIdle();
        RefreshSessions();
        ShowAgent(created);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var text = Composer.Input.Trim();
        if (text.Length == 0)
            return;
        Composer.Input = "";
        if (text.StartsWith('/'))
        {
            await RunCommandAsync(text);
            return;
        }
        // 用户消息只由会话事件渲染: 本地回显会在恢复会话/重放事件时变成第二条。
        _agent.Followup(MessageFactory.CreateUserText(text));
    }

    [RelayCommand]
    private void CancelTask() => _agent.Cancel(new AgentCancelCause.User());

    [RelayCommand]
    private void ShowChat() => Page = AppPage.Chat;

    [RelayCommand]
    private void ShowSettings() => Page = AppPage.Settings;

    [RelayCommand]
    private void ShowMarket()
    {
        Page = AppPage.Market;
        _ = RefreshPluginsAsync();
    }

    [RelayCommand]
    private void ShowConversation() => Tab = ChatTab.Conversation;

    [RelayCommand]
    private void ShowTrace() => Tab = ChatTab.Trace;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void OpenSession(SessionNodeViewModel? node)
    {
        if (node is not null)
            SelectedSession = node;
    }

    [RelayCommand]
    private void SelectTraceItem(TraceItemViewModel? item)
    {
        foreach (var trace in TraceItems)
            trace.IsSelected = ReferenceEquals(trace, item);
        if (item?.Message is { } message)
            ScrollRequested?.Invoke(message);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        foreach (var unsubscribe in _unsubscribers)
            unsubscribe();
        _unsubscribers.Clear();
    }

    private AgentOptions CurrentOptions()
        => new(_agent.Options.Provider, _agent.Options.Model, _agent.Options.ReasoningEffort, _agent.Options.MaxTokens);

    private IReadOnlyList<SessionNodeViewModel> AllSessions()
        => [.. Workspaces.SelectMany(workspace => workspace.Sessions)];

    private SessionNodeViewModel? FindSession(SessionId id)
        => AllSessions().FirstOrDefault(node => node.SessionId == id);

    private static IReadOnlyList<WorkspaceGroupViewModel> GroupByWorkspace(IReadOnlyList<SessionNodeViewModel> nodes)
        => [.. nodes
            .GroupBy(node => node.Workspace, StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(node => node.CreatedAt))
            .Select(group =>
            {
                var workspace = new WorkspaceGroupViewModel { Name = group.Key };
                foreach (var node in group.OrderByDescending(node => node.CreatedAt))
                    workspace.Sessions.Add(node);
                return workspace;
            })];

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(RefreshSessions);

    private void OnSessionEvent(SessionEventNotification notification)
    {
        if (ReferenceEquals(notification.Session, _agent.Session))
            _events.Enqueue(notification.Event);
    }

    private ValueTask<object?> OnApprovalRequest(ApprovalRequestNotification notification, Func<ValueTask<object?>> next)
    {
        var handler = ApprovalRequested;
        if (handler is null)
            return next();
        var answer = new TaskCompletionSource<ApprovalOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() => _ = AskApprovalAsync(handler, notification.Request, answer));
        return new ValueTask<object?>(answer.Task);
    }

    private static async Task AskApprovalAsync(
        Func<ApprovalRequest, Task<ApprovalOutcome>> handler,
        ApprovalRequest request,
        TaskCompletionSource<ApprovalOutcome> answer)
    {
        try
        {
            answer.TrySetResult(await handler(request));
        }
        catch (Exception error)
        {
            answer.TrySetResult(ApprovalOutcome.Cancelled);
            _ = error;
        }
    }

    private async Task SwitchToAsync(SessionNodeViewModel node)
    {
        var agent = node.Agent ?? _agents.Get(node.SessionId) as AgentLoopAgent ?? await ResumeAsync(node);
        if (agent is null)
            return;
        node.Agent = agent;
        ShowAgent(agent);
    }

    private async Task<AgentLoopAgent?> ResumeAsync(SessionNodeViewModel node)
    {
        try
        {
            var handle = await _agents.Resume(new ResumeAgentOptions(node.SessionId, CurrentOptions()));
            var resumed = (AgentLoopAgent)handle.Agent;
            await resumed.WhenIdle();
            return resumed;
        }
        catch (Exception error)
        {
            StatusText = $"会话无法加载: {error.Message}";
            return null;
        }
    }

    private async Task RunCommandAsync(string text)
    {
        var output = await _settings.RunCommandAsync(_agent, text);
        if (output.Length > 0)
            AppendMessage(new MessageViewModel("系统", output, MessageKind.System, false));
    }

    private void ShowAgent(AgentLoopAgent agent)
    {
        _agent = agent;
        _renderedSeq = 0;
        _openAssistant = null;
        _openReasoning = null;
        Messages.Clear();
        TraceItems.Clear();
        SessionTitle = agent.Session.Header.Title ?? agent.Id.Value;
        SessionSubtitle = ModelLabel(agent);
        ApplyEvents(agent.Session.SnapshotEvents());
        IsBusy = agent.Status == AgentStatus.Running;
        SelectedSession = FindSession(agent.Id);
        AgentChanged?.Invoke(agent);
    }

    private void ApplyEvents(IReadOnlyList<SessionEvent> batch)
    {
        if (_disposed)
            return;
        foreach (var sessionEvent in batch)
        {
            if (sessionEvent.Seq < _renderedSeq)
                continue;
            _renderedSeq = sessionEvent.Seq + 1;
            ApplyEvent(sessionEvent);
        }
    }

    private void ApplyEvent(SessionEvent sessionEvent)
    {
        switch (sessionEvent.Data)
        {
            case TurnStartPayload turn:
                IsBusy = true;
                AddTrace(sessionEvent.Seq, TraceKind.Turn, $"第 {turn.Turn} 轮");
                break;
            case TurnEndPayload turn:
                IsBusy = false;
                AddTrace(sessionEvent.Seq, TraceKind.Turn, $"第 {turn.Turn} 轮结束", TurnEndText(turn.Reason));
                ApplyTurnEnd(turn.Reason);
                break;
            case UserMessagePayload user:
                ApplyUserMessage(sessionEvent.Seq, user);
                break;
            case AssistantChunkPayload chunk:
                ApplyChunk(sessionEvent.Seq, chunk.Chunk);
                break;
            case AssistantMessagePayload assistant:
                ApplyAssistant(AssistantText(assistant.Message.Content));
                break;
            case ToolCallPayload call:
                ApplyToolCall(sessionEvent.Seq, call);
                break;
            case ToolResultPayload result:
                ApplyToolResult(sessionEvent.Seq, result);
                break;
            case RequestContextPayload context:
                // 请求元数据(provider/model)只在轨迹里出现, 进正文会和运行时上下文注入行重复。
                AddTrace(sessionEvent.Seq, TraceKind.Context, $"请求上下文 · {context.Provider}/{context.Model}", context.SystemPromptUpdate ?? "");
                break;
            case CommandRunPayload run:
                AddTrace(sessionEvent.Seq, TraceKind.Command, $"命令 /{run.Name}", run.Args ?? "");
                break;
            case ApprovalAskedPayload asked:
                AddTrace(sessionEvent.Seq, TraceKind.Approval, $"审批 {asked.ToolName}", asked.Reason ?? "");
                break;
        }
    }

    private void ApplyUserMessage(long seq, UserMessagePayload payload)
    {
        var text = ContentText(payload.Message.Content);
        switch (payload.Message.Source)
        {
            case UserMessageSource:
                AppendMessage(new MessageViewModel("你", text, MessageKind.User, false));
                break;
            case PluginMessageSource plugin:
                var label = $"上下文注入 · {plugin.Plugin}";
                AppendMessage(new MessageViewModel("", label, MessageKind.Context, false));
                AddTrace(seq, TraceKind.Context, label, plugin.Summary ?? text);
                break;
            default:
                AppendMessage(new MessageViewModel(payload.Message.Source.Kind, text, MessageKind.System, false));
                break;
        }
    }

    private void ApplyTurnEnd(TurnEndReason reason)
    {
        _openAssistant = null;
        _openReasoning = null;
        if (reason is TurnEndReason.Error error)
            AppendMessage(new MessageViewModel("错误", $"{error.Failure.Code}: {error.Failure.Message}", MessageKind.System, false));
    }

    private void ApplyChunk(long seq, StreamChunk chunk)
    {
        switch (chunk)
        {
            case StreamChunk.BlockStart { BlockType: "reasoning" }:
                _openReasoning = null;
                AddTrace(seq, TraceKind.Reasoning, "思考");
                break;
            case StreamChunk.ReasoningDelta delta when delta.Text.Length > 0:
                AppendReasoning(delta.Text);
                break;
            case StreamChunk.BlockStart { BlockType: "text" }:
                _openAssistant = null;
                break;
            case StreamChunk.TextDelta delta when delta.Text.Length > 0:
                AppendAssistant(delta.Text);
                break;
            case StreamChunk.BlockEnd:
                // 保留 _openAssistant: 紧随其后的 assistant/message 会用最终文本覆盖同一条记录, 避免出现重复消息。
                _openReasoning = null;
                break;
        }
    }

    private void ApplyAssistant(string text)
    {
        if (_openAssistant is not null)
        {
            _openAssistant.SetText(text);
            _openAssistant.IsStreaming = false;
            _openAssistant = null;
            return;
        }
        if (text.Length > 0)
            AppendMessage(new MessageViewModel("助手", text, MessageKind.Assistant, false));
    }

    private void ApplyToolCall(long seq, ToolCallPayload call)
    {
        _openAssistant = null;
        _openReasoning = null;
        AppendMessage(new MessageViewModel("工具", $"{call.Name} {call.Arguments}", MessageKind.Tool, false));
        AddTrace(seq, TraceKind.Tool, $"工具 {call.Name}", call.Arguments);
    }

    private void ApplyToolResult(long seq, ToolResultPayload result)
    {
        var text = ContentText(result.Message.Content);
        var label = result.Error is null ? "结果" : $"错误 {result.Error.Code}";
        AppendMessage(new MessageViewModel(label, text, MessageKind.Result, false));
        AddTrace(seq, TraceKind.Tool, result.Error is null ? "工具结果" : $"工具失败 {result.Error.Code}", text);
    }


    private void AppendAssistant(string delta)
    {
        _openAssistant ??= AppendMessage(new MessageViewModel("助手", "", MessageKind.Assistant, true));
        _openAssistant.Append(delta);
    }

    private void AppendReasoning(string delta)
    {
        _openReasoning ??= AppendMessage(new MessageViewModel("思考", "", MessageKind.Reasoning, true));
        _openReasoning.Append(delta);
    }

    private MessageViewModel AppendMessage(MessageViewModel message)
    {
        message.Updated += _ => MessagesChanged?.Invoke();
        Messages.Add(message);
        MessagesChanged?.Invoke();
        return message;
    }

    private void AddTrace(long seq, TraceKind kind, string title, string detail = "")
    {
        TraceItems.Add(new TraceItemViewModel
        {
            Seq = seq,
            Kind = kind,
            Title = title,
            Detail = Preview(detail),
            Time = DateTimeOffset.Now.ToString("HH:mm:ss"),
            Message = Messages.Count > 0 ? Messages[^1] : null,
        });
    }

    private static string Preview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= TracePreviewChars ? flat : flat[..TracePreviewChars] + "…";
    }

    private static string ModelLabel(AgentLoopAgent agent)
        => $"{agent.Options.Provider}/{agent.Options.Model}";

    private static string TurnEndText(TurnEndReason reason) => reason switch
    {
        TurnEndReason.Completed => "完成",
        TurnEndReason.Aborted aborted => $"中止 · {aborted.Reason.Kind}",
        TurnEndReason.Blocked => "被阻止",
        TurnEndReason.MaxTokens => "达到 max tokens",
        TurnEndReason.Interrupted => "被打断",
        TurnEndReason.Error error => $"失败 · {error.Failure.Code}",
        _ => reason.Kind,
    };

    private static string ContentText(IReadOnlyList<ContentBlock> blocks)
        => string.Join('\n', blocks.Select(block => block switch
        {
            LlmTextBlock text => text.Text,
            ReasoningBlock reasoning => $"[reasoning] {reasoning.Text}",
            ToolCallBlock call => $"[tool: {call.Name}] {call.Arguments}",
            ToolResultBlock result => ContentText(result.Content),
            ImageBlock => "[image]",
            _ => $"[{block.Type}]",
        }));

    /** 思考内容已由流式增量渲染, 最终消息只取正文, 避免重复。 */
    private static string AssistantText(IReadOnlyList<ContentBlock> blocks)
        => string.Join('\n', blocks.OfType<LlmTextBlock>().Select(block => block.Text)).Trim();
}
