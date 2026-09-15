namespace Dsh.Gui.Host;

/** GUI 启动器入口:把参数原样转给宿主的 `gui` 分支。 */
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
        => DeepSeek_Harness_Sharp.Program.Main(["gui", .. args]).GetAwaiter().GetResult();
}
