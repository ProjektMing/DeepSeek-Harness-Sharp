using System.Runtime.InteropServices;
using System.Text;

namespace Dsh.Tui;

public sealed class AnsiRenderer
{
    private readonly StringBuilder _builder = new();
    private char[] _outputBuffer = new char[64 * 1024];
    private CellGrid? _previous;

    public string Render(CellGrid grid, int cursorX, int cursorY, bool forceFull = false)
    {
        BuildFrame(grid, cursorX, cursorY, forceFull);
        return _builder.ToString();
    }

    public int Render(CellGrid grid, int cursorX, int cursorY, Span<char> destination, bool forceFull = false)
    {
        BuildFrame(grid, cursorX, cursorY, forceFull);
        var builder = _builder;
        if (destination.Length < builder.Length)
            throw new ArgumentException($"输出缓冲区需要至少 {builder.Length} 个字符", nameof(destination));
        builder.CopyTo(0, destination, builder.Length);
        return builder.Length;
    }

    public ReadOnlyMemory<char> RenderToBuffer(CellGrid grid, int cursorX, int cursorY, bool forceFull = false)
    {
        BuildFrame(grid, cursorX, cursorY, forceFull);
        var builder = _builder;
        if (_outputBuffer.Length < builder.Length)
            _outputBuffer = new char[Math.Max(builder.Length, _outputBuffer.Length * 2)];
        builder.CopyTo(0, _outputBuffer, builder.Length);
        return _outputBuffer.AsMemory(0, builder.Length);
    }

    public void Reset()
        => _previous = null;

    private void BuildFrame(CellGrid grid, int cursorX, int cursorY, bool forceFull)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var needsFull = forceFull
            || _previous is null
            || _previous.Width != grid.Width
            || _previous.Height != grid.Height;

        var builder = _builder;
        builder.Clear();
        if (needsFull)
        {
            AppendFull(builder, grid);
            if (_previous is null || _previous.Width != grid.Width || _previous.Height != grid.Height)
                _previous = grid.Clone();
            else
                grid.CopyTo(_previous);
        }
        else
        {
            AppendDiff(builder, grid, _previous!);
        }

        AppendCursor(builder, cursorX, cursorY, grid.Width, grid.Height);
    }

    private static void AppendFull(StringBuilder builder, CellGrid grid)
    {
        builder.Append("\x1b[2J\x1b[H");
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = grid[x, y];
                AppendSgr(builder, cell);
                builder.Append(Sanitize(cell.Character));
                if (TerminalTextWidth.IsWide(cell.Character))
                    x++;
            }

            builder.Append("\x1b[0m");
            if (y < grid.Height - 1)
                builder.Append("\r\n");
        }
    }

    private static void AppendDiff(StringBuilder builder, CellGrid grid, CellGrid previous)
    {
        for (var y = 0; y < grid.Height; y++)
        {
            var row = grid.Row(y);
            if (MemoryMarshal.Cast<Cell, byte>(row).SequenceEqual(MemoryMarshal.Cast<Cell, byte>(previous.Row(y))))
                continue;
            AppendPosition(builder, 0, y);
            var x = 0;
            var runOpen = false;
            AnsiColor runForeground = default;
            AnsiColor runBackground = default;
            CellStyle runStyle = default;
            while (x < row.Length)
            {
                var cell = row[x];
                if (cell.Character == '\0' && x > 0 && TerminalTextWidth.IsWide(row[x - 1].Character))
                {
                    x++;
                    continue;
                }

                if (!runOpen || cell.Foreground != runForeground || cell.Background != runBackground || cell.Style != runStyle)
                {
                    if (runOpen)
                        builder.Append("\x1b[0m");
                    AppendSgr(builder, cell);
                    (runForeground, runBackground, runStyle) = (cell.Foreground, cell.Background, cell.Style);
                    runOpen = true;
                }
                builder.Append(Sanitize(cell.Character));
                x++;
            }
            builder.Append("\x1b[0m");
            row.CopyTo(previous.RawCells.AsSpan(y * row.Length, row.Length));
        }
    }

    private static void AppendPosition(StringBuilder builder, int x, int y)
    {
        builder.Append("\x1b[");
        builder.Append(y + 1);
        builder.Append(';');
        builder.Append(x + 1);
        builder.Append('H');
    }

    private static void AppendCursor(StringBuilder builder, int cursorX, int cursorY, int width, int height)
    {
        cursorX = Math.Clamp(cursorX, 0, width - 1);
        cursorY = Math.Clamp(cursorY, 0, height - 1);
        AppendPosition(builder, cursorX, cursorY);
    }

    private static void AppendSgr(StringBuilder builder, Cell cell)
    {
        builder.Append("\x1b[");
        var separator = false;
        if ((cell.Style & CellStyle.Bold) != 0)
        {
            builder.Append('1');
            separator = true;
        }
        if ((cell.Style & CellStyle.Dim) != 0)
        {
            if (separator)
                builder.Append(';');
            builder.Append('2');
            separator = true;
        }
        if ((cell.Style & CellStyle.Reverse) != 0)
        {
            if (separator)
                builder.Append(';');
            builder.Append('7');
            separator = true;
        }
        if (separator)
            builder.Append(';');
        builder.Append(ForegroundCode(cell.Foreground));
        builder.Append(';');
        builder.Append(BackgroundCode(cell.Background));
        builder.Append('m');
    }

    private static int ForegroundCode(AnsiColor color)
    {
        if (color == AnsiColor.Default)
            return 39;
        if (color >= AnsiColor.BrightBlack)
            return 90 + (color - AnsiColor.BrightBlack);
        return 30 + (color - AnsiColor.Black);
    }

    private static int BackgroundCode(AnsiColor color)
    {
        if (color == AnsiColor.Default)
            return 49;
        if (color >= AnsiColor.BrightBlack)
            return 100 + (color - AnsiColor.BrightBlack);
        return 40 + (color - AnsiColor.Black);
    }

    private static char Sanitize(char value)
        => value == '\0' ? ' ' : value;
}