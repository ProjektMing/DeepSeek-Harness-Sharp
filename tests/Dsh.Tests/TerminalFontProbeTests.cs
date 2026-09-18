using Dsh.Tui;

namespace Dsh.Tests;

public class TerminalFontProbeTests
{
    [Theory]
    [InlineData("\x1b[6;16;10t", 10, 16)]
    [InlineData("\x1b[6;24;12t", 12, 24)]
    [InlineData("prefix\x1b[6;20;11t", 11, 20)]
    public void Parse_ValidReply_ReturnsCellPixels(string reply, int expectedWidth, int expectedHeight)
    {
        var cell = TerminalFontProbe.Parse(reply);

        Assert.Equal((expectedWidth, expectedHeight), cell);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\x1b[4;600;800t")]
    [InlineData("\x1b[6;0;0t")]
    [InlineData("\x1b[6;16t")]
    [InlineData("\x1b[6;16;10")]
    [InlineData("garbage")]
    public void Parse_InvalidReply_ReturnsNull(string reply)
    {
        Assert.Null(TerminalFontProbe.Parse(reply));
    }
}
