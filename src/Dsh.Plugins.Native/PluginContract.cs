namespace Dsh.Plugins.Native;

/** 原生插件的声明面:作者只声明包名与工具集合;
 *  导出握手、句柄表、两段式缓冲协议与异常拦截由生成器产出,作者不手写 ABI 胶水。 */
public interface IDshNativePlugin
{
    string Package { get; }

    IReadOnlyList<NativePluginTool> Tools { get; }
}

/** 原生插件暴露给宿主的工具:ParametersJson 是 JSON Schema;Invoke 收参数 JSON,返回结果 JSON。 */
public sealed record NativePluginTool(
    string Name,
    string Description,
    string ParametersJson,
    Func<string, string?> Invoke);

/** 作者侧运行期入口:生成的导出层在激活时挂上日志接收器,作者代码可随时记日志(未激活时静默)。 */
public static class DshNativePluginRuntime
{
    public static Action<int, string>? LogSink { get; set; }

    public static void Log(int level, string message) => LogSink?.Invoke(level, message);
}
