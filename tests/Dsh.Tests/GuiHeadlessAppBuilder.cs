using Avalonia;
using Avalonia.Headless;
using Dsh.Gui;
using Dsh.Tests;

[assembly: AvaloniaTestApplication(typeof(GuiHeadlessAppBuilder))]

namespace Dsh.Tests;

/** Avalonia headless 宿主: 让 UI 测试在没有窗口系统的环境下真正渲染(含 Skia 绘制与截图)。 */
public static class GuiHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
