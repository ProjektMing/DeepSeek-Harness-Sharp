using Chickensoft.LogicBlocks;

namespace Dsh.Runtime.Plugins;

/** 每插件一个 LogicBlocks 状态机:状态 Pending/Activating/Active/Deactivating/Failed/Disposed。
 *  Apply 由调度器同步驱动(输入 Activate → ActivateCompleted),效应回收为异步(Async 跟踪)。 */
public sealed class PluginLifecycle : LogicBlock
{
    public PluginLifecycle(PluginActivation activation)
    {
        Set(activation);
        Set(new PluginLifecycleState.Pending());
        Set(new PluginLifecycleState.Activating());
        Set(new PluginLifecycleState.Active());
        Set(new PluginLifecycleState.Deactivating());
        Set(new PluginLifecycleState.Failed());
        Set(new PluginLifecycleState.Disposed());
        Start<PluginLifecycleState.Pending>();
    }

    public PluginLifecycleState Current => (PluginLifecycleState)State!;
}

public abstract record PluginLifecycleState : LogicBlockState
{
    public static class Input
    {
        public readonly record struct Activate;

        public readonly record struct ActivateCompleted(string? Error);

        public readonly record struct DependencyChanged;

        public readonly record struct ConfigChanged;

        public readonly record struct Retry;

        public readonly record struct Deactivate(bool Unload);

        public readonly record struct Deactivated(bool Unload);
    }

    public sealed record Pending : PluginLifecycleState,
        IGet<Input.Activate>,
        IGet<Input.Deactivate>,
        IGet<Input.DependencyChanged>,
        IGet<Input.ConfigChanged>,
        IGet<Input.Retry>
    {
        public Pending() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Pending));

        public Type On(in Input.Activate input) => To<Activating>();

        public Type On(in Input.Deactivate input) => input.Unload ? To<Disposed>() : ToSelf();

        public Type On(in Input.DependencyChanged input) => ToSelf();

        public Type On(in Input.ConfigChanged input) => ToSelf();

        public Type On(in Input.Retry input) => ToSelf();
    }

    public sealed record Activating : PluginLifecycleState, IGet<Input.ActivateCompleted>, IGet<Input.Deactivate>
    {
        public Activating() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Activating));

        public Type On(in Input.ActivateCompleted input) => input.Error is null ? To<Active>() : To<Failed>();

        public Type On(in Input.Deactivate input) => ToSelf();
    }

    public sealed record Active : PluginLifecycleState,
        IGet<Input.DependencyChanged>,
        IGet<Input.ConfigChanged>,
        IGet<Input.Deactivate>
    {
        public Active() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Active));

        public Type On(in Input.DependencyChanged input) => BeginRebuild();

        public Type On(in Input.ConfigChanged input) => BeginRebuild();

        public Type On(in Input.Deactivate input)
        {
            var activation = Get<PluginActivation>();
            activation.PendingUnload = input.Unload;
            StartDisposal(activation);
            return To<Deactivating>();
        }

        private Type BeginRebuild()
        {
            var activation = Get<PluginActivation>();
            activation.PendingUnload = false;
            StartDisposal(activation);
            return To<Deactivating>();
        }

        private void StartDisposal(PluginActivation activation)
        {
            // 不用 Async(...):其续体会捕获调用方的 SynchronizationContext,测试/宿主同步上下文未泵时
            // 会滞留在队列里持住状态机(进而持住插件程序集),导致协作式卸载无法回收 ALC。
            var disposal = activation.DisposeEffectsAsync();
            TrackTask(disposal.ContinueWith(
                _ => activation.SendInput(new Input.Deactivated(activation.PendingUnload)),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default));
        }
    }

    public sealed record Deactivating : PluginLifecycleState, IGet<Input.Deactivated>
    {
        public Deactivating() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Deactivating));

        public Type On(in Input.Deactivated input) => input.Unload ? To<Disposed>() : To<Pending>();
    }

    public sealed record Failed : PluginLifecycleState,
        IGet<Input.Retry>,
        IGet<Input.DependencyChanged>,
        IGet<Input.ConfigChanged>,
        IGet<Input.Deactivate>
    {
        public Failed() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Failed));

        public Type On(in Input.Retry input) => RetryPending();

        public Type On(in Input.DependencyChanged input) => RetryPending();

        public Type On(in Input.ConfigChanged input) => RetryPending();

        public Type On(in Input.Deactivate input) => To<Disposed>();

        private Type RetryPending()
        {
            Get<PluginActivation>().ClearError();
            return To<Pending>();
        }
    }

    public sealed record Disposed : PluginLifecycleState
    {
        public Disposed() => this.OnEnter(() => Get<PluginActivation>().LifecycleEntered(ActivationState.Disposed));
    }
}
