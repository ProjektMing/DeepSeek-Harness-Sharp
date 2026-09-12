namespace Dsh.Tui;

public readonly record struct Rgba(float R, float G, float B, float A = 1f);

public readonly record struct CellQuad(
    float X,
    float Y,
    float Width,
    float Height,
    Rgba Color,
    float U0,
    float V0,
    float U1,
    float V1,
    bool IsGlyph);

public static class TerminalColorPalette
{
    public static readonly Rgba DefaultBackground = new(0f, 0f, 0f);
    public static readonly Rgba DefaultForeground = new(0.9f, 0.9f, 0.9f);

    public static Rgba ToRgba(AnsiColor color) => color switch
    {
        AnsiColor.Default => DefaultForeground,
        AnsiColor.Black => new Rgba(0.0f, 0.0f, 0.0f),
        AnsiColor.Red => new Rgba(0.80f, 0.19f, 0.19f),
        AnsiColor.Green => new Rgba(0.05f, 0.74f, 0.47f),
        AnsiColor.Yellow => new Rgba(0.90f, 0.90f, 0.06f),
        AnsiColor.Blue => new Rgba(0.14f, 0.45f, 0.78f),
        AnsiColor.Magenta => new Rgba(0.74f, 0.25f, 0.74f),
        AnsiColor.Cyan => new Rgba(0.07f, 0.66f, 0.80f),
        AnsiColor.White => new Rgba(0.90f, 0.90f, 0.90f),
        AnsiColor.BrightBlack => new Rgba(0.40f, 0.40f, 0.40f),
        AnsiColor.BrightRed => new Rgba(0.95f, 0.30f, 0.30f),
        AnsiColor.BrightGreen => new Rgba(0.14f, 0.82f, 0.55f),
        AnsiColor.BrightYellow => new Rgba(0.96f, 0.96f, 0.26f),
        AnsiColor.BrightBlue => new Rgba(0.23f, 0.56f, 0.92f),
        AnsiColor.BrightMagenta => new Rgba(0.84f, 0.44f, 0.84f),
        AnsiColor.BrightCyan => new Rgba(0.16f, 0.72f, 0.86f),
        AnsiColor.BrightWhite => new Rgba(1.0f, 1.0f, 1.0f),
        _ => DefaultForeground,
    };
}

public static class CellQuadBuilder
{
    public const int FloatsPerInstance = 9;

    private static readonly GlyphAtlas Atlas = GlyphAtlas.Shared;
    private static readonly float PackedDefaultBackground = PackColor(TerminalColorPalette.DefaultBackground);
    private static readonly float PackedDefaultForeground = PackColor(TerminalColorPalette.DefaultForeground);
    private static readonly float[] PackedPalette = BuildPackedPalette();

    public static IReadOnlyList<CellQuad> Build(CellGrid grid)
    {
        var quads = new List<CellQuad>(grid.Width * grid.Height * 2);
        Fill(grid, quads);
        return quads;
    }

    public static void Fill(CellGrid grid, List<CellQuad> quads)
    {
        ArgumentNullException.ThrowIfNull(grid);
        quads.Clear();

        for (var y = 0; y < grid.Height; y++)
        {
            var x = 0;
            while (x < grid.Width)
            {
                var background = BackgroundOf(grid[x, y]);
                var runStart = x;
                while (x + 1 < grid.Width && BackgroundOf(grid[x + 1, y]) == background)
                    x++;

                quads.Add(new CellQuad(
                    runStart,
                    y,
                    x - runStart + 1f,
                    1f,
                    background == AnsiColor.Default ? TerminalColorPalette.DefaultBackground : TerminalColorPalette.ToRgba(background),
                    0f,
                    0f,
                    0f,
                    0f,
                    false));

                for (var glyphX = runStart; glyphX <= x; glyphX++)
                {
                    var cell = grid[glyphX, y];
                    var character = cell.Character == '\0' ? ' ' : cell.Character;
                    if (character == ' ')
                        continue;

                    var foreground = (cell.Style & CellStyle.Reverse) != 0 ? cell.Background : cell.Foreground;
                    var uv = Atlas.GetUv(character);
                    var color = foreground == AnsiColor.Default ? TerminalColorPalette.DefaultForeground : TerminalColorPalette.ToRgba(foreground);
                    if ((cell.Style & CellStyle.Dim) != 0)
                        color = color with { R = color.R * 0.5f, G = color.G * 0.5f, B = color.B * 0.5f };

                    quads.Add(new CellQuad(
                        glyphX,
                        y,
                        1f,
                        1f,
                        color,
                        uv.MinX,
                        uv.MinY,
                        uv.MaxX,
                        uv.MaxY,
                        true));
                }
                x++;
            }
        }
    }

    public static int InstanceFloatCount(int quadCount) => quadCount * FloatsPerInstance;

    public static (int BackgroundCount, int GlyphCount) FillInstances(CellGrid grid, float[] backgroundInstances, float[] glyphInstances)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(backgroundInstances);
        ArgumentNullException.ThrowIfNull(glyphInstances);
        var cellCount = grid.Width * grid.Height;
        if (backgroundInstances.Length < InstanceFloatCount(cellCount))
            throw new ArgumentException($"背景实例数组需要至少 {InstanceFloatCount(cellCount)} 个 float", nameof(backgroundInstances));
        if (glyphInstances.Length < InstanceFloatCount(cellCount))
            throw new ArgumentException($"字形实例数组需要至少 {InstanceFloatCount(cellCount)} 个 float", nameof(glyphInstances));

        var backgroundOffset = 0;
        var glyphOffset = 0;
        var cells = grid.RawCells;
        var width = grid.Width;
        for (var y = 0; y < grid.Height; y++)
        {
            var row = y * width;
            var x = 0;
            while (x < width)
            {
                var first = cells[row + x];
                var background = (first.Style & CellStyle.Reverse) != 0 ? first.Foreground : first.Background;
                var runStart = x;
                while (x + 1 < width)
                {
                    var next = cells[row + x + 1];
                    var nextBackground = (next.Style & CellStyle.Reverse) != 0 ? next.Foreground : next.Background;
                    if (nextBackground != background)
                        break;
                    x++;
                }

                var packedBackground = background == AnsiColor.Default ? PackedDefaultBackground : PackedPalette[(int)background];
                WriteInstance(backgroundInstances, backgroundOffset, runStart, y, x + 1f - runStart, 1f, packedBackground, 0f, 0f, 0f, 0f);
                backgroundOffset += FloatsPerInstance;

                for (var glyphX = runStart; glyphX <= x; glyphX++)
                {
                    var cell = cells[row + glyphX];
                    var character = cell.Character == '\0' ? ' ' : cell.Character;
                    if (character == ' ')
                        continue;

                    var foreground = (cell.Style & CellStyle.Reverse) != 0 ? cell.Background : cell.Foreground;
                    var uv = Atlas.GetUv(character);
                    float packedForeground;
                    if ((cell.Style & CellStyle.Dim) != 0)
                    {
                        var rgba = foreground == AnsiColor.Default ? TerminalColorPalette.DefaultForeground : TerminalColorPalette.ToRgba(foreground);
                        packedForeground = PackColor(rgba with { R = rgba.R * 0.5f, G = rgba.G * 0.5f, B = rgba.B * 0.5f });
                    }
                    else
                    {
                        packedForeground = foreground == AnsiColor.Default ? PackedDefaultForeground : PackedPalette[(int)foreground];
                    }

                    WriteInstance(glyphInstances, glyphOffset, glyphX, y, 1f, 1f, packedForeground, uv.MinX, uv.MinY, uv.MaxX, uv.MaxY);
                    glyphOffset += FloatsPerInstance;
                }
                x++;
            }
        }
        return (backgroundOffset / FloatsPerInstance, glyphOffset / FloatsPerInstance);
    }

    private static void WriteInstance(float[] instances, int i, float x, float y, float width, float height, float packed, float u0, float v0, float u1, float v1)
    {
        instances[i] = x;
        instances[i + 1] = y;
        instances[i + 2] = width;
        instances[i + 3] = height;
        instances[i + 4] = packed;
        instances[i + 5] = u0;
        instances[i + 6] = v0;
        instances[i + 7] = u1;
        instances[i + 8] = v1;
    }

    private static float PackColor(Rgba color)
    {
        var r = (uint)(color.R * 255f + 0.5f);
        var g = (uint)(color.G * 255f + 0.5f);
        var b = (uint)(color.B * 255f + 0.5f);
        var a = (uint)(color.A * 255f + 0.5f);
        return BitConverter.UInt32BitsToSingle(r | (g << 8) | (b << 16) | (a << 24));
    }

    private static float[] BuildPackedPalette()
    {
        var colors = Enum.GetValues<AnsiColor>();
        var packed = new float[colors.Length];
        foreach (var color in colors)
            packed[(int)color] = PackColor(TerminalColorPalette.ToRgba(color));
        return packed;
    }

    private static AnsiColor BackgroundOf(Cell cell)
        => (cell.Style & CellStyle.Reverse) != 0 ? cell.Foreground : cell.Background;
}
