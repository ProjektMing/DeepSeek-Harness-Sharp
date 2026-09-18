using System.Runtime.InteropServices;
using Dsh.Tui;
using OpenTK.Graphics;
using OpenTK.Graphics.Egl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using GLFW = OpenTK.Windowing.GraphicsLibraryFramework.GLFW;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;
using WindowHintBool = OpenTK.Windowing.GraphicsLibraryFramework.WindowHintBool;

namespace Dsh.Tests;

/**
 * 无头 GL 上下文。
 * Windows: GLFW/WGL(AMD WGL, 或给进程设 HKCU\...\DirectX\UserGpuPreferences=GpuPreference=2 后的 NVIDIA WGL)。
 *   注: WGL_NV_gpu_affinity 是 NVIDIA 私有实现、默认适配器(AMD)上下文拿不到, WGL_AMD_gpu_association 只能选 AMD, 均不可行, 选卡只能走进程级偏好。
 * Linux 有显示服务器: 隐藏 GLFW 窗口(GLX/EGL 都经 X server 的 DRI 路由选驱动, 真机 NVIDIA 由 PRIME 变量选卡)。
 * Linux 无显示(真无头): EGL 平台设备枚举(系统 libEGL.so.1 由驱动包提供)。
 */
public sealed class HeadlessGl : IDisposable
{
    private const int PlatformDeviceExt = 0x313F;
    private const int ProbeSurfaceSize = 16;

    /** GLFW 初始化/建窗是进程级全局状态, 测试集合内仍可能跨线程并发, 串行化创建。 */
    private static readonly object GlfwGate = new();

    private readonly EGLDisplay _display;
    private readonly EGLContext _context;
    private readonly EGLSurface _surface;
    private readonly NativeWindow? _window;

    private HeadlessGl(EGLDisplay display, EGLContext context, EGLSurface surface, string renderer, IReadOnlyList<string> enumeratedDevices)
    {
        _display = display;
        _context = context;
        _surface = surface;
        Renderer = renderer;
        EnumeratedDevices = enumeratedDevices;
    }

    private HeadlessGl(NativeWindow window, string renderer)
    {
        _window = window;
        Renderer = renderer;
        EnumeratedDevices = [$"GLFW/X11: {renderer}"];
    }

    /** 选中的 GL 设备的 GL_RENDERER 字符串。 */
    public string Renderer { get; }

    /** 枚举到的全部设备描述(探针渲染器名或失败原因), 供报告多卡环境。 */
    public IReadOnlyList<string> EnumeratedDevices { get; }

    public static HeadlessGl Create(int width, int height)
    {
        if (OperatingSystem.IsWindows() || GpuRenderer.TryDetectDisplay(out _))
            return CreateGlfw(width, height);
        return CreateEgl(width, height);
    }

    private static HeadlessGl CreateGlfw(int width, int height)
    {
        lock (GlfwGate)
        {
            // 测试跑在线程池线程上, OpenTK 的"主线程"认定(入口方法在栈上)不适用, 关掉。
            GLFWProvider.CheckForMainThread = false;
            GpuRenderer.RequestRobustnessOnAmd();
            GLFWProvider.EnsureInitialized();
            GLFW.WindowHint(WindowHintBool.Visible, false);
            var window = new NativeWindow(new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title = "dsh-headless-bench",
                API = ContextAPI.OpenGL,
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3),
                StartVisible = false,
            });
            window.Context.MakeCurrent();
            GLLoader.LoadBindings(new GLFWBindingsContext());
            return new HeadlessGl(window, GL.GetString(OpenTK.Graphics.OpenGL.StringName.Renderer) ?? "");
        }
    }

    private static HeadlessGl CreateEgl(int width, int height)
    {
        var queryDevices = Marshal.GetDelegateForFunctionPointer<QueryDevicesExt>(Egl.GetProcAddress("eglQueryDevicesEXT"));
        var count = new int[1];
        if (queryDevices(0, null, count) == 0 || count[0] == 0)
            throw new InvalidOperationException($"eglQueryDevicesEXT 失败, err={Egl.GetError()}");
        var devices = new IntPtr[count[0]];
        if (queryDevices(count[0], devices, count) == 0)
            throw new InvalidOperationException($"eglQueryDevicesEXT 枚举失败, err={Egl.GetError()}");

        var candidates = new List<EglCandidate>();
        var failures = new List<string>();
        var enumerated = new List<string>();
        foreach (var device in devices)
        {
            var candidate = EglCandidate.TryProbe(device, out var failure);
            if (candidate is not null)
            {
                candidates.Add(candidate);
                enumerated.Add(candidate.Renderer);
            }
            else
            {
                failures.Add(failure);
                enumerated.Add($"probe 失败: {failure}");
            }
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException($"没有任何 EGL 设备能创建 GL 3.3 core 上下文 (devices={devices.Length}): {string.Join(" | ", failures)}");

        var chosen = candidates.OrderByDescending(candidate => candidate.Score).First();
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate, chosen))
                candidate.Dispose();
        }

        int[] surfaceAttribs = [0x3057, width, 0x3056, height, 0x3038];
        var surface = Egl.CreatePbufferSurface(chosen.Display, chosen.Config, surfaceAttribs);
        if (surface.Value == IntPtr.Zero)
            throw new InvalidOperationException($"eglCreatePbufferSurface 失败, err={Egl.GetError()}");
        if (!Egl.MakeCurrent(chosen.Display, surface, surface, chosen.Context))
            throw new InvalidOperationException($"eglMakeCurrent 失败, err={Egl.GetError()}");
        chosen.DropProbeSurface();
        GLLoader.LoadBindings(new EglBindings());
        return new HeadlessGl(chosen.Display, chosen.Context, surface, chosen.Renderer, enumerated);
    }

    public void Dispose()
    {
        if (_window is not null)
        {
            _window.Dispose();
            return;
        }
        Egl.MakeCurrent(_display, default, default, default);
        Egl.DestroySurface(_display, _surface);
        Egl.DestroyContext(_display, _context);
    }

    private sealed class EglCandidate : IDisposable
    {
        private EGLSurface _probeSurface;

        private EglCandidate(EGLDisplay display, EGLConfig config, EGLContext context, EGLSurface probeSurface, string renderer)
        {
            Display = display;
            Config = config;
            Context = context;
            _probeSurface = probeSurface;
            Renderer = renderer;
            Score = ScoreRenderer(renderer);
        }

        public EGLDisplay Display { get; }
        public EGLConfig Config { get; }
        public EGLContext Context { get; }
        public string Renderer { get; }
        public int Score { get; }

        public static EglCandidate? TryProbe(IntPtr device, out string failure)
        {
            failure = "";
            try
            {
                var display = Egl.GetPlatformDisplay((Platform)PlatformDeviceExt, device, Array.Empty<IntPtr>());
                if (display.Value == IntPtr.Zero)
                {
                    failure = $"GetPlatformDisplay err={Egl.GetError()}";
                    return null;
                }
                if (!Egl.Initialize(display, out _, out _))
                {
                    failure = $"Initialize err={Egl.GetError()}";
                    return null;
                }
                if (!Egl.BindAPI(RenderApi.OpenglApi))
                {
                    failure = $"BindAPI err={Egl.GetError()}";
                    return null;
                }
                int[] configAttribs = [0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3040, 4, 0x3033, 0x1, 0x3038];
                var configs = new EGLConfig[8];
                var numConfigs = new int[1];
                if (!Egl.ChooseConfig(display, configAttribs, configs, configs.Length, numConfigs) || numConfigs[0] == 0)
                {
                    failure = $"ChooseConfig err={Egl.GetError()}";
                    return null;
                }
                int[] contextAttribs = [0x3098, 3, 0x30FB, 3, 0x30FD, 0x1, 0x3038];
                var context = Egl.CreateContext(display, configs[0], default, contextAttribs);
                if (context.Value == IntPtr.Zero)
                {
                    failure = $"CreateContext err={Egl.GetError()}";
                    return null;
                }
                int[] surfaceAttribs = [0x3057, ProbeSurfaceSize, 0x3056, ProbeSurfaceSize, 0x3038];
                var probeSurface = Egl.CreatePbufferSurface(display, configs[0], surfaceAttribs);
                if (probeSurface.Value == IntPtr.Zero || !Egl.MakeCurrent(display, probeSurface, probeSurface, context))
                {
                    failure = $"Pbuffer/MakeCurrent err={Egl.GetError()}";
                    Egl.DestroyContext(display, context);
                    return null;
                }
                GLLoader.LoadBindings(new EglBindings());
                var renderer = GL.GetString(OpenTK.Graphics.OpenGL.StringName.Renderer) ?? "";
                return new EglCandidate(display, configs[0], context, probeSurface, renderer);
            }
            catch (Exception error)
            {
                failure = error.Message;
                return null;
            }
        }

        public void Dispose()
        {
            Egl.MakeCurrent(Display, default, default, default);
            Egl.DestroySurface(Display, _probeSurface);
            Egl.DestroyContext(Display, Context);
        }

        public void DropProbeSurface()
        {
            Egl.DestroySurface(Display, _probeSurface);
            _probeSurface = default;
        }
    }

    private static int ScoreRenderer(string renderer)
    {
        if (renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("softpipe", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("basic render", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("lavapipe", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (renderer.Contains("nvidia", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("geforce", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("quadro", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 2;
    }

    private sealed class EglBindings : OpenTK.IBindingsContext
    {
        public IntPtr GetProcAddress(string procName) => Egl.GetProcAddress(procName);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int QueryDevicesExt(int maxDevices, [Out] IntPtr[]? devices, [Out] int[] numDevices);
}
