using System.Runtime.InteropServices;
using OpenTK.Graphics;
using OpenTK.Graphics.Egl;
using OpenTK.Graphics.OpenGL;

namespace Dsh.Tests;

public sealed class HeadlessEgl : IDisposable
{
    private const int PlatformDeviceExt = 0x313F;

    private readonly EGLDisplay _display;
    private readonly EGLContext _context;
    private readonly EGLSurface _surface;

    private HeadlessEgl(EGLDisplay display, EGLContext context, EGLSurface surface)
    {
        _display = display;
        _context = context;
        _surface = surface;
    }

    public static HeadlessEgl Create(int width, int height)
    {
        var queryDevices = Marshal.GetDelegateForFunctionPointer<QueryDevicesExt>(Egl.GetProcAddress("eglQueryDevicesEXT"));
        var count = new int[1];
        if (queryDevices(0, null, count) == 0 || count[0] == 0)
            throw new InvalidOperationException($"eglQueryDevicesEXT 失败, err={Egl.GetError()}");
        var devices = new IntPtr[count[0]];
        if (queryDevices(count[0], devices, count) == 0)
            throw new InvalidOperationException($"eglQueryDevicesEXT 枚举失败, err={Egl.GetError()}");
        var display = Egl.GetPlatformDisplay((Platform)PlatformDeviceExt, devices[0], Array.Empty<IntPtr>());
        if (display.Value == IntPtr.Zero)
            throw new InvalidOperationException($"eglGetPlatformDisplay 失败, err={Egl.GetError()}");
        if (!Egl.Initialize(display, out _, out _))
            throw new InvalidOperationException($"eglInitialize 失败, err={Egl.GetError()}");
        if (!Egl.BindAPI(RenderApi.OpenglApi))
            throw new InvalidOperationException($"eglBindAPI 失败, err={Egl.GetError()}");

        int[] configAttribs = [0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3040, 4, 0x3033, 0x1, 0x3038];
        var configs = new EGLConfig[8];
        var numConfigs = new int[1];
        if (!Egl.ChooseConfig(display, configAttribs, configs, configs.Length, numConfigs) || numConfigs[0] == 0)
            throw new InvalidOperationException($"eglChooseConfig 失败, err={Egl.GetError()}");
        int[] contextAttribs = [0x3098, 3, 0x30FB, 3, 0x30FD, 0x1, 0x3038];
        var context = Egl.CreateContext(display, configs[0], default, contextAttribs);
        if (context.Value == IntPtr.Zero)
            throw new InvalidOperationException($"eglCreateContext 失败, err={Egl.GetError()}");
        int[] surfaceAttribs = [0x3057, width, 0x3056, height, 0x3038];
        var surface = Egl.CreatePbufferSurface(display, configs[0], surfaceAttribs);
        if (surface.Value == IntPtr.Zero)
            throw new InvalidOperationException($"eglCreatePbufferSurface 失败, err={Egl.GetError()}");
        if (!Egl.MakeCurrent(display, surface, surface, context))
            throw new InvalidOperationException($"eglMakeCurrent 失败, err={Egl.GetError()}");
        GLLoader.LoadBindings(new EglBindings());
        return new HeadlessEgl(display, context, surface);
    }

    public void Dispose()
    {
        Egl.MakeCurrent(_display, default, default, default);
        Egl.DestroySurface(_display, _surface);
        Egl.DestroyContext(_display, _context);
    }

    private sealed class EglBindings : OpenTK.IBindingsContext
    {
        public IntPtr GetProcAddress(string procName) => Egl.GetProcAddress(procName);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int QueryDevicesExt(int maxDevices, [Out] IntPtr[]? devices, [Out] int[] numDevices);
}

