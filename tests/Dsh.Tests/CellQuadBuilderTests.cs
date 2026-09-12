using Dsh.Tui;

namespace Dsh.Tests;

public class CellQuadBuilderTests
{
    [Fact]
    public void Build_ForEmptyCell_ReturnsOnlyBackgroundQuad()
    {
        var grid = new CellGrid(1, 1);

        var quads = CellQuadBuilder.Build(grid);

        Assert.Single(quads);
        Assert.False(quads[0].IsGlyph);
        Assert.Equal(TerminalColorPalette.DefaultBackground, quads[0].Color);
    }

    [Fact]
    public void Build_ForTextCell_ReturnsBackgroundAndGlyphQuads()
    {
        var grid = new CellGrid(1, 1);
        grid[0, 0] = new Cell('A', AnsiColor.Green, AnsiColor.Black);

        var quads = CellQuadBuilder.Build(grid);

        Assert.Equal(2, quads.Count);
        Assert.False(quads[0].IsGlyph);
        Assert.True(quads[1].IsGlyph);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Green), quads[1].Color);
        Assert.True(quads[1].U0 < quads[1].U1);
        Assert.True(quads[1].V0 < quads[1].V1);
    }

    [Fact]
    public void Build_ReverseStyle_SwapsForegroundAndBackground()
    {
        var grid = new CellGrid(1, 1);
        grid[0, 0] = new Cell('x', AnsiColor.Red, AnsiColor.Blue, CellStyle.Reverse);

        var quads = CellQuadBuilder.Build(grid);

        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Red), quads[0].Color);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Blue), quads[1].Color);
    }

    [Fact]
    public void Build_CoversEveryCell()
    {
        var grid = new CellGrid(3, 2);
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
                grid[x, y] = new Cell('a');
        }

        var quads = CellQuadBuilder.Build(grid);

        var backgrounds = quads.Where(quad => !quad.IsGlyph).ToList();
        var glyphs = quads.Where(quad => quad.IsGlyph).ToList();
        Assert.Equal(grid.Height, backgrounds.Count);
        Assert.All(backgrounds, quad => Assert.Equal(grid.Width, (int)quad.Width));
        Assert.All(backgrounds, quad => Assert.Equal(1f, quad.Height));
        Assert.Equal(grid.Width * grid.Height, glyphs.Count);
        Assert.All(glyphs, quad => Assert.Equal(1f, quad.Width));
        Assert.All(glyphs, quad => Assert.Equal(1f, quad.Height));
        var covered = glyphs.Select(quad => ((int)quad.X, (int)quad.Y)).ToHashSet();
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
                Assert.Contains((x, y), covered);
        }
    }

    [Fact]
    public void Build_MixedBackgrounds_ProduceSeparateRuns()
    {
        var grid = new CellGrid(3, 1);
        grid[0, 0] = new Cell('a', AnsiColor.Default, AnsiColor.Red);
        grid[1, 0] = new Cell('b', AnsiColor.Default, AnsiColor.Red);
        grid[2, 0] = new Cell('c', AnsiColor.Default, AnsiColor.Blue);

        var quads = CellQuadBuilder.Build(grid);

        var backgrounds = quads.Where(quad => !quad.IsGlyph).ToList();
        Assert.Equal(2, backgrounds.Count);
        Assert.Equal(2f, backgrounds[0].Width);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Red), backgrounds[0].Color);
        Assert.Equal(1f, backgrounds[1].Width);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Blue), backgrounds[1].Color);
        Assert.Equal(3, quads.Count(quad => quad.IsGlyph));
    }

    [Fact]
    public void FillInstances_ForTextCell_WritesBackgroundThenGlyphInstance()
    {
        var grid = new CellGrid(1, 1);
        grid[0, 0] = new Cell('A', AnsiColor.Green, AnsiColor.Black);
        var backgroundInstances = new float[CellQuadBuilder.InstanceFloatCount(1)];
        var glyphInstances = new float[CellQuadBuilder.InstanceFloatCount(1)];

        var (backgroundCount, glyphCount) = CellQuadBuilder.FillInstances(grid, backgroundInstances, glyphInstances);

        Assert.Equal(1, backgroundCount);
        Assert.Equal(1, glyphCount);
        Assert.Equal(0f, backgroundInstances[0]);
        Assert.Equal(0f, backgroundInstances[1]);
        Assert.Equal(1f, backgroundInstances[2]);
        Assert.Equal(1f, backgroundInstances[3]);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Black).R, (BitConverter.SingleToUInt32Bits(backgroundInstances[4]) & 0xFF) / 255.0, 1.0 / 255.0);
        Assert.Equal(0f, glyphInstances[0]);
        Assert.Equal(0f, glyphInstances[1]);
        Assert.Equal(1f, glyphInstances[2]);
        Assert.Equal(1f, glyphInstances[3]);
        Assert.Equal(TerminalColorPalette.ToRgba(AnsiColor.Green).R, (BitConverter.SingleToUInt32Bits(glyphInstances[4]) & 0xFF) / 255.0, 1.0 / 255.0);
        Assert.True(glyphInstances[5] < glyphInstances[7]);
        Assert.True(glyphInstances[6] < glyphInstances[8]);
    }

    [Fact]
    public void FillInstances_SkipsSpaces_OnlyWritesVisibleInstances()
    {
        var grid = new CellGrid(3, 1);
        grid[0, 0] = new Cell('a');
        grid[2, 0] = new Cell('c');
        var backgroundInstances = new float[CellQuadBuilder.InstanceFloatCount(3)];
        var glyphInstances = new float[CellQuadBuilder.InstanceFloatCount(3)];

        var (backgroundCount, glyphCount) = CellQuadBuilder.FillInstances(grid, backgroundInstances, glyphInstances);

        Assert.Equal(1, backgroundCount);
        Assert.Equal(2, glyphCount);
        Assert.Equal(3f, backgroundInstances[2]);
    }
}
