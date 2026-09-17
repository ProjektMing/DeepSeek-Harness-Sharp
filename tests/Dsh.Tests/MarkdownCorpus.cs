using Dsh.Tui;

namespace Dsh.Tests;

internal static class MarkdownCorpus
{
    private static readonly string[] HeadingTexts =
    [
        "渲染管线性能分析", "实例化渲染与动态图集", "压测结果与热点归因", "第三轮：Release 测量",
        "LRU 淘汰策略的时钟语义", "终端侧解析成本", "后续优化方向", "实验设置与复现方式",
    ];

    private static readonly string[] ProseTexts =
    [
        "本轮 **优化** 将 `FillInstances` 的均帧耗时从 1.22 ms 降到 0.23 ms，GC 分配归零。",
        "把 `ticks` 更新改为 **每帧一次** 时钟读后，GPU 顶点构建的主要成本被消除，详见 `render-benchmark.md`。",
        "在 4K 网格（480x135，64800 格）下，**稀疏更新** 场景的瓶颈是行 diff 的逐格比较，而不是 ANSI 生成。",
        "字形图集改为 8192 槽动态烘焙后，启动耗时从 6-8 s 降为缓存命中 **几十毫秒**，未命中按字形 50-100 µs 摊销。",
        "The benchmark renders **1000 frames** of mixed `ASCII`/`CJK` markdown at 60 fps-equivalent throughput.",
        "Sampling shows `Environment.TickCount64` accounts for **99%** of `GetGlyphIndex` — one clock read per cell.",
    ];

    private static readonly string[] CodeLines =
    [
        "private static void Upload(int vbo, float[] vertices, int floatCount)",
        "var (backgroundCount, glyphCount) = CellQuadBuilder.FillInstances(grid, bg, glyphs);",
        "GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, vertices);",
        "_slotTicks[slot] = Environment.TickCount64;",
        "if (nextBackground != background) break;",
        "return new Phase(nanos, GC.GetAllocatedBytesForCurrentThread() - allocBefore, outputBytes);",
        "public ReadOnlySpan<Cell> Row(int y) => _cells.AsSpan(y * Width, Width);",
        "for (var x = 0; x < width; x++) { var cell = cells[row + x]; }",
    ];

    private static readonly string[] CommentLines =
    [
        "// 背景 run 合并：一行 200 格 = 200 个背景 quad 合并为一个宽 quad",
        "// 上传孤立：BufferData(NULL) 孤立旧存储，消除驱动的隐式同步",
        "// 注意：ticks 只驱动淘汰顺序，不影响画面正确性",
    ];

    private static readonly string[] ListItems =
    [
        "顶点缓冲复用（压测直接暴露）", "transcript 每帧全量重排（最大热点）", "GPU 渲染循环空转",
        "背景 quad 合并", "AnsiRenderer 分配池化", "Cell 瘦身：16B -> 6B，网格内存 -62%",
    ];

    private static readonly string[] JsonLines =
    [
        """{"tool": "apply_patch", "status": "ok", "files": ["src/Dsh.Tui/GpuRenderer.cs"], "elapsed_ms": 42}""",
        """{"tool": "bash", "command": "dotnet test --filter RenderBenchmark", "exit_code": 0, "stdout_lines": 128}""",
        """{"glyph_atlas": {"columns": 128, "rows": 64, "capacity": 8192, "baked": 229, "cache_bytes": 76544}}""",
        """{"frame": 512, "cpu_ms": 1.874, "gpu_ms": 3.193, "gc_bytes": 0, "instances": 48831}""",
    ];

    private static readonly string[] TableCells =
    [
        "平均帧耗时", "P95 帧耗时", "GC 分配", "每帧产物", "GPU 0.044 ms", "CPU 0.083 ms",
        "160.5 MB", "0.0 MB", "1759.8 KB", "59.7 KB", "-81%", "1.9x",
    ];

    private static readonly string[] QuoteLines =
    [
        "注意：Release 构建下 JIT 内联后绝对值只会更低，Debug 数值仅代表相对比较。",
        "在该热点中，调用开销远大于省下的每格一次读取，融合方案实测回退后已还原。",
        "渲染优化方案里的优化手段已经实施过，此处只说明结论，不重复推导。",
    ];

    private static readonly string[] ImageLines =
    [
        "![渲染对比图](artifacts/gpu-screenshots/dsh-gpu-instanced.png)",
        "![图 3：实例化后顶点布局](docs/images/vertex-layout.svg)",
        "![dotTrace 调用树](artifacts/bench/gpu-profile-5.png)",
    ];

    public static Cell[][] Build(int lineCount, int minLineCells, int seed)
    {
        var random = new Random(seed);
        var lines = new Cell[lineCount][];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = BuildLine(random, index, minLineCells);
        }
        return lines;
    }

    public static string[] BuildTextLines(int lineCount, int seed)
    {
        var random = new Random(seed);
        var lines = new string[lineCount];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = (index % 21) switch
            {
                0 => $"## {HeadingTexts[random.Next(HeadingTexts.Length)]}",
                2 => "```csharp",
                3 or 4 or 5 => $"    {CodeLines[random.Next(CodeLines.Length)]}",
                7 => $"- **{ListItems[random.Next(ListItems.Length)]}**：{ProseTexts[random.Next(ProseTexts.Length)]}",
                8 => $"{random.Next(1, 9)}. `{CodeLines[random.Next(CodeLines.Length)]}`",
                9 => $"> {QuoteLines[random.Next(QuoteLines.Length)]}",
                10 or 14 => $"| {TableCells[random.Next(TableCells.Length)]} | {TableCells[random.Next(TableCells.Length)]} | {TableCells[random.Next(TableCells.Length)]} |",
                12 => "| --- | --- | --- |",
                13 => JsonLines[random.Next(JsonLines.Length)],
                15 => $"详见 [渲染优化方案](plans/渲染优化方案.md) 与 `dotTrace` 快照。",
                17 => $"思考：{ProseTexts[random.Next(ProseTexts.Length)]}",
                18 => "---",
                20 => ImageLines[random.Next(ImageLines.Length)],
                _ => ProseTexts[random.Next(ProseTexts.Length)],
            };
        }
        return lines;
    }

    private static Cell[] BuildLine(Random random, int index, int minCells)
    {
        var segments = new List<Segment>();
        switch (index % 22)
        {
            case 0:
                var heading = HeadingTexts[random.Next(HeadingTexts.Length)];
                segments.Add(new Segment($"{'#'} ", AnsiColor.BrightCyan, AnsiColor.Default, CellStyle.Bold));
                segments.Add(new Segment(heading, AnsiColor.BrightCyan, AnsiColor.Default, CellStyle.Bold));
                break;
            case 1 or 6 or 11 or 16:
                AddProse(segments, random);
                break;
            case 2:
                segments.Add(new Segment("```csharp", AnsiColor.BrightGreen, AnsiColor.Default, CellStyle.None));
                break;
            case 3 or 4 or 5:
                var code = CodeLines[random.Next(CodeLines.Length)];
                segments.Add(new Segment("    ", AnsiColor.Default, AnsiColor.Black, CellStyle.None));
                if (random.Next(3) == 0)
                    segments.Add(new Segment(code, AnsiColor.Blue, AnsiColor.Black, CellStyle.None));
                else if (random.Next(3) == 0)
                    segments.Add(new Segment(code, AnsiColor.Green, AnsiColor.Black, CellStyle.None));
                else
                    segments.Add(new Segment(code, AnsiColor.BrightWhite, AnsiColor.Black, CellStyle.None));
                if (random.Next(4) == 0)
                    segments.Add(new Segment($"  // {CommentLines[random.Next(CommentLines.Length)][3..]}", AnsiColor.BrightBlack, AnsiColor.Black, CellStyle.Dim));
                break;
            case 7:
                segments.Add(new Segment($"- **{ListItems[random.Next(ListItems.Length)]}**：", AnsiColor.BrightYellow, AnsiColor.Default, CellStyle.Bold));
                segments.Add(new Segment(ProseTexts[random.Next(ProseTexts.Length)], AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
            case 8:
                segments.Add(new Segment($"{random.Next(1, 9)}. ", AnsiColor.BrightMagenta, AnsiColor.Default, CellStyle.None));
                var snippet = CodeLines[random.Next(CodeLines.Length)];
                segments.Add(new Segment($"`{snippet[..Math.Min(24, snippet.Length)]}`", AnsiColor.Yellow, AnsiColor.Default, CellStyle.None));
                segments.Add(new Segment(" " + ProseTexts[random.Next(ProseTexts.Length)], AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
            case 9:
                segments.Add(new Segment("> ", AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.Dim));
                segments.Add(new Segment(QuoteLines[random.Next(QuoteLines.Length)], AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.Dim));
                break;
            case 10 or 14:
                var cells = new List<Segment> { new Segment("| ", AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.None) };
                for (var column = 0; column < 4; column++)
                {
                    cells.Add(new Segment(TableCells[random.Next(TableCells.Length)].PadRight(12), AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                    cells.Add(new Segment(" | ", AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.None));
                }
                segments.AddRange(cells);
                break;
            case 12:
                segments.Add(new Segment("| --- | --- | --- | --- |", AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.None));
                break;
            case 13:
                segments.Add(new Segment(JsonLines[random.Next(JsonLines.Length)], AnsiColor.Yellow, AnsiColor.Default, CellStyle.None));
                break;
            case 15:
                segments.Add(new Segment("详见 ", AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                segments.Add(new Segment("[渲染优化方案](plans/渲染优化方案.md)", AnsiColor.Blue, AnsiColor.Default, CellStyle.None));
                segments.Add(new Segment(" 与 `dotTrace` 快照，GPU 1.22 -> 0.233 ms（**-81%**）。", AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
            case 17:
                segments.Add(new Segment("思考：", AnsiColor.Magenta, AnsiColor.Default, CellStyle.Dim));
                segments.Add(new Segment(ProseTexts[random.Next(ProseTexts.Length)], AnsiColor.Magenta, AnsiColor.Default, CellStyle.Dim));
                break;
            case 18:
                segments.Add(new Segment("---", AnsiColor.BrightBlack, AnsiColor.Default, CellStyle.None));
                break;
            case 19:
                break;
            case 20:
                segments.Add(new Segment(ImageLines[random.Next(ImageLines.Length)], AnsiColor.Blue, AnsiColor.Default, CellStyle.None));
                break;
            case 21:
                segments.Add(new Segment("效果见 ", AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                segments.Add(new Segment(ImageLines[random.Next(ImageLines.Length)], AnsiColor.Blue, AnsiColor.Default, CellStyle.None));
                segments.Add(new Segment("（点击查看原图）。", AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
        }
        if (segments.Count == 0)
            AddProse(segments, random);
        return ToCells(segments, minCells);
    }

    private static void AddProse(List<Segment> segments, Random random)
    {
        var remaining = ProseTexts[random.Next(ProseTexts.Length)];
        while (remaining.Length > 0)
        {
            var boldStart = remaining.IndexOf("**", StringComparison.Ordinal);
            var codeStart = remaining.IndexOf('`');
            var cut = NextMarkup(boldStart, codeStart);
            if (cut <= 0)
            {
                segments.Add(new Segment(remaining, AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
            }
            segments.Add(new Segment(remaining[..cut], AnsiColor.Default, AnsiColor.Default, CellStyle.None));
            var marker = remaining[cut] == '*' ? "**" : "`";
            var end = remaining.IndexOf(marker, cut + marker.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                segments.Add(new Segment(remaining[cut..], AnsiColor.Default, AnsiColor.Default, CellStyle.None));
                break;
            }
            var inner = remaining[(cut + marker.Length)..end];
            segments.Add(marker == "**"
                ? new Segment(inner, AnsiColor.BrightWhite, AnsiColor.Default, CellStyle.Bold)
                : new Segment(inner, AnsiColor.Yellow, AnsiColor.Default, CellStyle.None));
            remaining = remaining[(end + marker.Length)..];
        }
    }

    private static int NextMarkup(int boldStart, int codeStart)
    {
        if (boldStart < 0)
            return codeStart;
        if (codeStart < 0)
            return boldStart;
        return Math.Min(boldStart, codeStart);
    }

    private static Cell[] ToCells(List<Segment> segments, int minCells)
    {
        var cells = new List<Cell>(minCells);
        foreach (var (text, foreground, background, style) in segments)
        {
            foreach (var character in text)
            {
                cells.Add(new Cell(character, foreground, background, style));
                if (TerminalTextWidth.IsWide(character))
                    cells.Add(new Cell('\0', foreground, background, style));
            }
        }
        while (cells.Count < minCells)
            cells.Add(default);
        return cells.ToArray();
    }

    private readonly record struct Segment(string Text, AnsiColor Foreground, AnsiColor Background, CellStyle Style);
}
