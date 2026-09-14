using Dsh.Runtime;

namespace Dsh.Tests.Runtime;

public class RuntimeEventTests
{
    [Fact]
    public void Emit_InvokesListenersInOrder()
    {
        var ctx = new Context();
        var calls = new List<string>();
        ctx.On("test", (_, _) =>
        {
            calls.Add("a");
            return new ValueTask<object?>();
        });
        ctx.On("test", (_, _) =>
        {
            calls.Add("b");
            return new ValueTask<object?>();
        }, new EventOptions { Prepend = true });
        ctx.Emit("test");
        Assert.Equal(["b", "a"], calls);
    }

    [Fact]
    public async Task Serial_AwaitsEachListener()
    {
        var ctx = new Context();
        var calls = new List<string>();
        ctx.On("test", async (_, _) =>
        {
            await Task.Delay(10);
            calls.Add("a");
            return null;
        });
        ctx.On("test", (_, _) =>
        {
            calls.Add("b");
            return new ValueTask<object?>((object?)null);
        });
        await ctx.Serial("test");
        Assert.Equal(["a", "b"], calls);
    }

    [Fact]
    public async Task Serial_ShortCircuitsOnBailValue()
    {
        var ctx = new Context();
        ctx.On("test", (_, _) => new ValueTask<object?>());
        ctx.On("test", (_, _) => new ValueTask<object?>(false));
        ctx.On("test", (_, _) => new ValueTask<object?>("hit"));
        ctx.On("test", (_, _) => throw new InvalidOperationException("must not run"));
        Assert.Equal("hit", await ctx.Serial("test"));
    }

    [Fact]
    public async Task Waterfall_ChainsNext()
    {
        var ctx = new Context();
        ctx.On("test", async (_, args) =>
        {
            var next = (Func<ValueTask<object?>>)args[1]!;
            var value = await next();
            return $"a{(string?)value}";
        });
        ctx.On("test", async (_, args) =>
        {
            var next = (Func<ValueTask<object?>>)args[1]!;
            var value = await next();
            return $"b{(string?)value}";
        });
        var result = await ctx.Events.Waterfall(null, "test", ["x"], () => new ValueTask<object?>("!"));
        Assert.Equal("ab!", result);
    }

    [Fact]
    public async Task Parallel_ThrowsAggregateException()
    {
        var ctx = new Context();
        ctx.On("test", (_, _) => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<AggregateException>(() => ctx.Events.Parallel(null, "test"));
    }

    [Fact]
    public void Dispose_StopsListener()
    {
        var ctx = new Context();
        var count = 0;
        var dispose = ctx.On("test", (_, _) =>
        {
            count++;
            return new ValueTask<object?>();
        });
        ctx.Emit("test");
        dispose();
        ctx.Emit("test");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Emit_IsolatesListenerExceptions()
    {
        var ctx = new Context();
        var reached = false;
        ctx.On("test", (_, _) => throw new InvalidOperationException("boom"));
        ctx.On("test", (_, _) =>
        {
            reached = true;
            return new ValueTask<object?>();
        });
        ctx.Emit("test");
        Assert.True(reached);
        Assert.Contains(ctx.Root.Logger.Buffer, message => message.Type == LoggerType.Error);
    }

    [Fact]
    public void Filter_DropsHooksOutsideScope()
    {
        var ctx = new Context();
        var scopeA = new object();
        var scopeB = new object();
        var received = new List<string>();
        var ctxA = ctx.Extend(("scope", scopeA));
        var ctxB = ctx.Extend(("scope", scopeB));
        ctxA.On("scoped", (_, _) =>
        {
            received.Add("a");
            return new ValueTask<object?>();
        });
        ctxB.On("scoped", (_, _) =>
        {
            received.Add("b");
            return new ValueTask<object?>();
        });

        var emitterA = ctxA.WithFilter(candidate => ReferenceEquals(candidate.GetProp("scope"), scopeA));
        emitterA.Events.Emit(emitterA, "scoped");
        Assert.Equal(["a"], received);

        received.Clear();
        ctx.Emit("scoped");
        Assert.Equal(["a", "b"], received);
    }

    [Fact]
    public void Filter_GlobalHooksAlwaysRun()
    {
        var ctx = new Context();
        var received = new List<string>();
        var scoped = ctx.WithFilter(_ => false);
        scoped.On("scoped", (_, _) =>        {
            received.Add("scoped");
            return new ValueTask<object?>();
        });
        ctx.On("scoped", (_, _) =>
        {
            received.Add("global");
            return new ValueTask<object?>();
        }, new EventOptions { Global = true });

        scoped.Events.Emit(scoped, "scoped");
        Assert.Equal(["global"], received);
    }
}
