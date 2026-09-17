using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Dsh.Tui;

public sealed class GpuRenderer : IDisposable
{
    private const int CellPixelWidth = 16;
    private const int CellPixelHeight = 20;

    private readonly GlyphAtlas _atlas = GlyphAtlas.Shared;
    private readonly ChatWindow _chat;
    private readonly GameWindow _window;
    private readonly GpuRenderCore _core = new();
    private CellGrid _grid;
    private UiLayout _layout;
    private CellGrid? _lastGrid;
    private uint[] _packedCells = new uint[80 * 25];
    private readonly List<(int Start, int Count)> _dirtyRanges = [];
    private int _seenRenderVersion = -1;
    private float _mouseX;
    private float _mouseY;
    private bool _disposed;
    private readonly string? _screenshotPath = Environment.GetEnvironmentVariable("DSH_GPU_SCREENSHOT");
    private bool _screenshotTaken;

    public static bool TryDetectDisplay(out string reason)
    {
        reason = "";
        if (!OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return true;
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(display))
        {
            reason = "no display server detected (neither DISPLAY nor WAYLAND_DISPLAY is set)";
            return false;
        }
        if (display.StartsWith(':') && !File.Exists(Path.Combine("/tmp/.X11-unix", $"X{ScreenOf(display)}")))
        {
            reason = $"no display server detected (X11 socket for DISPLAY={display} is missing)";
            return false;
        }
        return true;
    }

    private static string ScreenOf(string display)
    {
        var value = display[1..];
        var dot = value.IndexOf('.');
        return dot < 0 ? value : value[..dot];
    }

    public GpuRenderer(ChatWindow chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
        var settings = new NativeWindowSettings
        {
            ClientSize = new Vector2i(80 * CellPixelWidth, 25 * CellPixelHeight),
            Title = "dsh --gpu",
            API = ContextAPI.OpenGL,
            Profile = ContextProfile.Core,
            APIVersion = new Version(3, 3),
        };
        _window = new GameWindow(GameWindowSettings.Default, settings);
        _grid = new CellGrid(80, 25);
        _layout = LayoutEngine.Calculate(_grid.Width, _grid.Height);
        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.RenderFrame += OnRenderFrame;
        _window.KeyDown += OnKeyDown;
        _window.TextInput += OnTextInput;
        _window.MouseMove += OnMouseMove;
        _window.MouseDown += OnMouseDown;
        _window.MouseWheel += OnMouseWheel;
    }

    public void Run()
        => _window.Run();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _core.Dispose();
        _atlas.SaveCacheIfDirty();
        _window.Dispose();
    }

    private void OnLoad()
    {
        _core.Initialize(_atlas);
    }

    private void OnResize(ResizeEventArgs e)
    {
        var framebufferSize = _window.FramebufferSize;
        var width = Math.Max(1, framebufferSize.X);
        var height = Math.Max(1, framebufferSize.Y);
        GL.Viewport(0, 0, width, height);
        var gridWidth = Math.Max(1, width / CellPixelWidth);
        var gridHeight = Math.Max(1, height / CellPixelHeight);
        if (_grid.Width != gridWidth || _grid.Height != gridHeight)
        {
            _grid = new CellGrid(gridWidth, gridHeight);
            _layout = LayoutEngine.Calculate(gridWidth, gridHeight);
            _lastGrid = null;
            var required = gridWidth * gridHeight;
            if (_packedCells.Length < required)
                _packedCells = new uint[required];
        }
    }

    private void OnRenderFrame(FrameEventArgs e)
    {
        _chat.DrainUi();
        if (_chat.ExitRequested)
        {
            _window.Close();
            return;
        }

        if (_chat.RenderVersion == _seenRenderVersion
            && _lastGrid is not null
            && _lastGrid.Width == _grid.Width
            && _lastGrid.Height == _grid.Height)
            return;
        _seenRenderVersion = _chat.RenderVersion;

        _chat.Draw(_grid, _layout);
        _core.EnsureCellCapacity(_grid.Width * _grid.Height);
        if (CellPacker.CollectDirtyRowRanges(_grid, _lastGrid, _dirtyRanges) > 0)
        {
            foreach (var (start, count) in _dirtyRanges)
            {
                CellPacker.PackRows(_grid, start, count, _packedCells, _atlas);
                _core.UploadCells(_packedCells, start * _grid.Width, count * _grid.Width);
            }
        }

        _lastGrid ??= new CellGrid(_grid.Width, _grid.Height);
        (_grid, _lastGrid) = (_lastGrid, _grid);

        _core.RenderFrame(_atlas, _grid.Width, _grid.Height);

        if (!_screenshotTaken && _screenshotPath is not null)
        {
            SaveScreenshot(_screenshotPath);
            _screenshotTaken = true;
            _chat.RequestExit();
        }

        _window.SwapBuffers();
    }

    private void OnKeyDown(KeyboardKeyEventArgs e)
    {
        if (e.Control && e.Key == Keys.V)
        {
            var clipboard = _window.ClipboardString;
            if (clipboard.Length > 0)
                _chat.InsertText(clipboard);
            return;
        }

        if (!TryMapKey(e.Key, out var consoleKey))
            return;

        _chat.HandleKey(new ConsoleKeyInfo('\0', consoleKey, e.Shift, e.Alt, e.Control));
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        var text = e.AsString;
        if (text.Length == 0)
            return;
        _chat.HandleKey(new ConsoleKeyInfo(text[0], ConsoleKey.NoName, false, false, false));
    }

    private void OnMouseMove(MouseMoveEventArgs e)
    {
        _mouseX = e.X;
        _mouseY = e.Y;
    }

    private void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left || !e.IsPressed)
            return;
        var cellX = (int)(_mouseX / CellPixelWidth);
        var cellY = (int)(_mouseY / CellPixelHeight);
        _chat.HandleMouseClick(cellX, cellY, _layout);
    }

    private void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.OffsetY != 0)
            _chat.HandleMouseWheel((int)e.OffsetY);
    }

    private void SaveScreenshot(string path)
    {
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0)
            return;
        var pixels = new byte[size.X * size.Y * 4];
        GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        using var image = new Image<Rgba32>(size.X, size.Y);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = accessor.Height - 1 - y;
                for (var x = 0; x < accessor.Width; x++)
                {
                    var source = ((sourceY * accessor.Width) + x) * 4;
                    row[x] = new Rgba32(pixels[source], pixels[source + 1], pixels[source + 2], pixels[source + 3]);
                }
            }
        });
        if (path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            image.SaveAsTiff(path);
        else
            image.SaveAsPng(path);
    }
    

    private static bool TryMapKey(Keys key, out ConsoleKey consoleKey)
    {
        if (key is >= Keys.A and <= Keys.Z)
        {
            consoleKey = ConsoleKey.A + (key - Keys.A);
            return true;
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            consoleKey = ConsoleKey.D0 + (key - Keys.D0);
            return true;
        }

        switch (key)
        {
            case Keys.Enter:
                consoleKey = ConsoleKey.Enter;
                return true;
            case Keys.Escape:
                consoleKey = ConsoleKey.Escape;
                return true;
            case Keys.Tab:
                consoleKey = ConsoleKey.Tab;
                return true;
            case Keys.Backspace:
                consoleKey = ConsoleKey.Backspace;
                return true;
            case Keys.Delete:
                consoleKey = ConsoleKey.Delete;
                return true;
            case Keys.Up:
                consoleKey = ConsoleKey.UpArrow;
                return true;
            case Keys.Down:
                consoleKey = ConsoleKey.DownArrow;
                return true;
            case Keys.Left:
                consoleKey = ConsoleKey.LeftArrow;
                return true;
            case Keys.Right:
                consoleKey = ConsoleKey.RightArrow;
                return true;
            case Keys.Home:
                consoleKey = ConsoleKey.Home;
                return true;
            case Keys.End:
                consoleKey = ConsoleKey.End;
                return true;
            case Keys.PageUp:
                consoleKey = ConsoleKey.PageUp;
                return true;
            case Keys.PageDown:
                consoleKey = ConsoleKey.PageDown;
                return true;
            case Keys.Space:
                consoleKey = ConsoleKey.Spacebar;
                return true;
            default:
                consoleKey = default;
                return false;
        }
    }
}
