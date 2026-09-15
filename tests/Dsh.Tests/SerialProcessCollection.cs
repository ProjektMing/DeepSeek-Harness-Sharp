namespace Dsh.Tests;

/**
 * 需要独占跑的一组用例:这些用例启动真实进程并等待输出/超时,与其它集合并行时会因机器负载抖动
 * (单独跑稳定通过,整包并行时偶发 5s 等待超时或输出未及刷出)。
 */
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialProcessCollection
{
    public const string Name = "serial-process";
}
