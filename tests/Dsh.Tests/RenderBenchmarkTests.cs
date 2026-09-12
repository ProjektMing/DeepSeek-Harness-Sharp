using System.Diagnostics;
using System.Text;
using Dsh.Tui;

namespace Dsh.Tests;

public class RenderBenchmarkTests
{
    private const int GridWidth = 200;
    private const int GridHeight = 50;
    private const int FrameCount = 500;

    [Fact]
    public void Gpu_Vs_Cpu_Render_Benchmark()
    {
        var corpus = BuildCorpus();
        var lines = corpus.Split('\n');
        var grid = new CellGrid(GridWidth, GridHeight);

        var cpuFrameNanos = new long[FrameCount];
        var gpuFrameNanos = new long[FrameCount];
        long cpuBytes = 0;
        long gpuBytes = 0;

        var renderer = new AnsiRenderer();
        var cpuAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < FrameCount; frame++)
        {
            FillFrame(lines, grid, frame);
            var started = Stopwatch.GetTimestamp();
            var output = renderer.Render(grid, 0, 0, forceFull: frame == 0);
            cpuFrameNanos[frame] = (long)Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            cpuBytes += output.Length;
        }
        var cpuAlloc = GC.GetAllocatedBytesForCurrentThread() - cpuAllocBefore;

        var backgroundInstances = new float[CellQuadBuilder.InstanceFloatCount(GridWidth * GridHeight)];
        var glyphInstances = new float[CellQuadBuilder.InstanceFloatCount(GridWidth * GridHeight)];
        FillFrame(lines, grid, 0);
        CellQuadBuilder.FillInstances(grid, backgroundInstances, glyphInstances);
        var gpuAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < FrameCount; frame++)
        {
            FillFrame(lines, grid, frame);
            var started = Stopwatch.GetTimestamp();
            var (backgroundCount, glyphCount) = CellQuadBuilder.FillInstances(grid, backgroundInstances, glyphInstances);
            gpuFrameNanos[frame] = (long)Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            gpuBytes += (backgroundCount + glyphCount) * CellQuadBuilder.FloatsPerInstance * sizeof(float);
        }
        var gpuAlloc = GC.GetAllocatedBytesForCurrentThread() - gpuAllocBefore;

        var report = new StringBuilder();
        report.AppendLine("# GPU vs CPU 渲染压测");
        report.AppendLine();
        report.AppendLine($"网格: {GridWidth}x{GridHeight}，帧数: {FrameCount}，每帧内容整体滚动一行（模拟大量文本输出）；网格对象复用、每帧重写单元格（与真实 TUI 一致）。");
        report.AppendLine("CPU 路径: AnsiRenderer.Render（行级 diff，StringBuilder 与 prev 网格复用，首帧全量），不含终端 I/O。");
        report.AppendLine("GPU 路径: CellQuadBuilder.FillInstances（单趟扫描、实例化实例数据 9 floats/quad、动态字形图集、float[] 复用），不含 GL 上传与交换。");
        report.AppendLine();
        report.AppendLine("| 指标 | CPU (AnsiRenderer) | GPU (顶点构建) |");
        report.AppendLine("| --- | --- | --- |");
        report.AppendLine($"| 平均帧耗时 | {Avg(cpuFrameNanos) / 1e6:F3} ms | {Avg(gpuFrameNanos) / 1e6:F3} ms |");
        report.AppendLine($"| P95 帧耗时 | {Pctl(cpuFrameNanos, 0.95) / 1e6:F3} ms | {Pctl(gpuFrameNanos, 0.95) / 1e6:F3} ms |");
        report.AppendLine($"| 最大帧耗时 | {cpuFrameNanos.Max() / 1e6:F3} ms | {gpuFrameNanos.Max() / 1e6:F3} ms |");
        report.AppendLine($"| 总耗时 | {cpuFrameNanos.Sum() / 1e6:F1} ms | {gpuFrameNanos.Sum() / 1e6:F1} ms |");
        report.AppendLine($"| GC 分配 | {cpuAlloc / 1e6:F1} MB | {gpuAlloc / 1e6:F1} MB |");
        report.AppendLine($"| 每帧产物大小 | {cpuBytes / 1024.0 / FrameCount:F1} KB ANSI 文本 | {gpuBytes / 1024.0 / FrameCount:F1} KB 顶点数据（显存占用估算） |");

        var directory = Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/bench");
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory, "render-benchmark.md"));
        File.WriteAllText(path, report.ToString());
        Assert.True(File.Exists(path));
    }

    private static double Avg(long[] values) => values.Average();

    private static double Pctl(long[] values, double p)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
    }

    private static string BuildCorpus()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < 400; index++)
            builder.AppendLine($"[{index:D4}] 你好，DeepSeek Harness！GPU 与 CPU 渲染对比压测 0123456789 — 混合 CJK 与 ASCII 文本，用于制造大量字符单元格。");
        return builder.ToString();
    }

    private static void FillFrame(string[] lines, CellGrid grid, int frame)
    {
        grid.Clear();
        for (var row = 0; row < GridHeight; row++)
        {
            var line = lines[(frame + row) % lines.Length];
            var x = 0;
            foreach (var character in line)
            {
                if (x >= GridWidth)
                    break;
                grid[x, row] = new Cell(character);
                x += TerminalTextWidth.IsWide(character) ? 2 : 1;
            }
        }
    }
}
