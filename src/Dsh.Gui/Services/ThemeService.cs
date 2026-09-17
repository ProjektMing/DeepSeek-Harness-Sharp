using Avalonia;
using Avalonia.Styling;

namespace Dsh.Gui.Services;

public static class ThemeService
{
    private const double BaseFontSize = 13.5;
    private const double RoundingStep = 0.5;
    private const double StatusFontSize = 11;
    private const double ListFontSize = 12.5;
    private const double BodyFontSize = 13.5;
    private const double TitleFontSize = 15;
    private const double BrandFontSize = 17;
    private const string StatusKey = "FontSize.Status";
    private const string ListKey = "FontSize.List";
    private const string BodyKey = "FontSize.Body";
    private const string TitleKey = "FontSize.Title";
    private const string BrandKey = "FontSize.Brand";

    public static void Apply(Application app, GuiSettingsSnapshot settings)
    {
        app.RequestedThemeVariant = settings.Theme.Trim().ToLowerInvariant() switch
        {
            GuiSettings.ThemeLight => ThemeVariant.Light,
            GuiSettings.ThemeSystem => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
        var scale = settings.FontSize / BaseFontSize;
        app.Resources[StatusKey] = RoundToStep(StatusFontSize * scale);
        app.Resources[ListKey] = RoundToStep(ListFontSize * scale);
        app.Resources[BodyKey] = RoundToStep(BodyFontSize * scale);
        app.Resources[TitleKey] = RoundToStep(TitleFontSize * scale);
        app.Resources[BrandKey] = RoundToStep(BrandFontSize * scale);
    }

    private static double RoundToStep(double value)
        => Math.Round(value / RoundingStep, MidpointRounding.AwayFromZero) * RoundingStep;
}
