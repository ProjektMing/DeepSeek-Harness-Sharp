using Dsh.Gui.Services;

namespace Dsh.Tests;

public sealed class ClosePolicyTests
{
    [Theory]
    [InlineData(CloseActionKind.Tray, false, CloseActionKind.Quit)]
    [InlineData(CloseActionKind.Tray, true, CloseActionKind.Tray)]
    [InlineData(CloseActionKind.Quit, false, CloseActionKind.Quit)]
    [InlineData(CloseActionKind.Quit, true, CloseActionKind.Quit)]
    [InlineData(CloseActionKind.Ask, false, CloseActionKind.Ask)]
    [InlineData(CloseActionKind.Ask, true, CloseActionKind.Ask)]
    public void Coerce_Downgrades_Tray_Only_When_Unsupported(CloseActionKind requested, bool traySupported, CloseActionKind expected)
        => Assert.Equal(expected, ClosePolicy.Coerce(requested, traySupported));

    [Fact]
    public void Parse_And_Wire_Roundtrip_Every_Kind()
    {
        foreach (var kind in Enum.GetValues<CloseActionKind>())
            Assert.Equal(kind, ClosePolicy.Parse(ClosePolicy.Wire(kind)));
    }

    [Theory]
    [InlineData("KDE", true)]
    [InlineData("ubuntu:GNOME", true)]
    [InlineData("Unity:Unity7:ubuntu", true)]
    [InlineData("GNOME", false)]
    [InlineData("GNOME-Classic:GNOME", false)]
    [InlineData("", true)]
    public void Desktop_Heuristic_Matches_Real_Desktops(string desktop, bool expected)
        => Assert.Equal(expected, ClosePolicy.DesktopHasTrayByName(desktop));

    [Fact]
    public void TraySupport_Matches_Probe_Or_Heuristic()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.True(ClosePolicy.TraySupported);
            Assert.True(ClosePolicy.TrayAvailable());
            return;
        }
        Assert.Equal(ClosePolicy.ProbeWatcher() ?? ClosePolicy.DesktopHasTrayByName(), ClosePolicy.TraySupported);
        Assert.Equal(ClosePolicy.ProbeWatcher() ?? ClosePolicy.TraySupported, ClosePolicy.TrayAvailable());
    }
}
