namespace Dsh.Gui.Services;

public enum CloseActionKind
{
    Tray,
    Quit,
    Ask,
}

public static class ClosePolicy
{
    public static bool TraySupported { get; } = DetectTraySupport();

    public static CloseActionKind Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        GuiSettings.CloseQuit => CloseActionKind.Quit,
        GuiSettings.CloseAsk => CloseActionKind.Ask,
        _ => CloseActionKind.Tray,
    };

    public static string Wire(CloseActionKind kind) => kind switch
    {
        CloseActionKind.Quit => GuiSettings.CloseQuit,
        CloseActionKind.Ask => GuiSettings.CloseAsk,
        _ => GuiSettings.CloseTray,
    };

    private static bool DetectTraySupport()
    {
        if (!OperatingSystem.IsLinux())
            return true;
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        if (!desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
            return true;
        return desktop.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) || desktop.Contains("unity", StringComparison.OrdinalIgnoreCase);
    }
}
