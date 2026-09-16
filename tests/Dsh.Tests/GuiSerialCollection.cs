namespace Dsh.Tests;

/**
 * 使用 Avalonia headless 的测试共用同一个 UI 线程/dispatcher, 彼此并行会互相干扰,
 * 也会和真实 LLM 冒烟测试抢资源; 因此这类测试串行执行, 不与其它集合并行。
 */
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class GuiSerialCollection
{
    public const string CollectionName = "gui-serial";
}
