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
    private CellGrid _grid;
    private UiLayout _layout;
    private CellGrid? _lastGrid;
    private float[] _backgroundInstances = new float[CellQuadBuilder.InstanceFloatCount(80 * 25)];
    private float[] _glyphInstances = new float[CellQuadBuilder.InstanceFloatCount(80 * 25)];
    private readonly int[] _dirtySlots = new int[64];
    private int _backgroundGpuCapacity;
    private int _glyphGpuCapacity;
    private int _seenRenderVersion = -1;
    private int _unitVbo;
    private int _ebo;
    private int _backgroundVao;
    private int _backgroundInstanceVbo;
    private int _glyphVao;
    private int _glyphInstanceVbo;
    private int _shader;
    private int _texture;
    private int _backgroundInstanceCount;
    private int _glyphInstanceCount;
    private int _textureLocation;
    private int _gridSizeLocation;
    private int _texEnabledLocation;
    private float _mouseX;
    private float _mouseY;
    private bool _disposed;
    private readonly string? _screenshotPath = Environment.GetEnvironmentVariable("DSH_GPU_SCREENSHOT");
    private bool _screenshotTaken;

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
        if (_shader != 0)
            GL.DeleteProgram(_shader);
        if (_texture != 0)
            GL.DeleteTexture(_texture);
        if (_backgroundVao != 0)
            GL.DeleteVertexArray(_backgroundVao);
        if (_backgroundInstanceVbo != 0)
            GL.DeleteBuffer(_backgroundInstanceVbo);
        if (_glyphVao != 0)
            GL.DeleteVertexArray(_glyphVao);
        if (_glyphInstanceVbo != 0)
            GL.DeleteBuffer(_glyphInstanceVbo);
        if (_unitVbo != 0)
            GL.DeleteBuffer(_unitVbo);
        if (_ebo != 0)
            GL.DeleteBuffer(_ebo);
        _atlas.SaveCacheIfDirty();
        _window.Dispose();
    }

    private void FlushAtlasDirty()
    {
        if (_atlas.DirtyCount == 0)
            return;
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.PixelStorei(PixelStoreParameter.UnpackRowLength, _atlas.AtlasWidth);
        var flushed = _atlas.FlushDirtyRegions(_dirtySlots);
        while (flushed > 0)
        {
            for (var index = 0; index < flushed; index++)
            {
                var slot = _dirtySlots[index];
                var slotX = (slot % GlyphAtlas.Columns) * GlyphAtlas.GlyphWidth;
                var slotY = (slot / GlyphAtlas.Columns) * GlyphAtlas.GlyphHeight;
                GL.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    slotX,
                    slotY,
                    GlyphAtlas.GlyphWidth,
                    GlyphAtlas.GlyphHeight,
                    PixelFormat.Red,
                    PixelType.UnsignedByte,
                    ref _atlas.TextureData[(slotY * _atlas.AtlasWidth) + slotX]);
            }
            flushed = _atlas.FlushDirtyRegions(_dirtySlots);
        }
        GL.PixelStorei(PixelStoreParameter.UnpackRowLength, 0);
    }

    private static void Upload(int vbo, float[] vertices, int floatCount, int capacity, ref int gpuCapacity)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        if (capacity > gpuCapacity)
            gpuCapacity = capacity;
        GL.BufferData(BufferTarget.ArrayBuffer, gpuCapacity * sizeof(float), IntPtr.Zero, BufferUsage.DynamicDraw);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, floatCount * sizeof(float), vertices);
    }

    private void OnLoad()
    {
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader = CreateShader();
        _texture = CreateTexture();
        _textureLocation = GL.GetUniformLocation(_shader, "uTexture");
        _gridSizeLocation = GL.GetUniformLocation(_shader, "uGridSize");
        _texEnabledLocation = GL.GetUniformLocation(_shader, "uTexEnabled");

        float[] unitQuad = [0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f];
        _unitVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _unitVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, unitQuad.Length * sizeof(float), unitQuad, BufferUsage.StaticDraw);

        ushort[] indices = [0, 1, 2, 2, 1, 3];
        _ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsage.StaticDraw);

        _backgroundInstanceVbo = GL.GenBuffer();
        _backgroundVao = CreateInstanceVao(_backgroundInstanceVbo);
        _glyphInstanceVbo = GL.GenBuffer();
        _glyphVao = CreateInstanceVao(_glyphInstanceVbo);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private int CreateInstanceVao(int instanceVbo)
    {
        var vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _unitVbo);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
        var stride = CellQuadBuilder.FloatsPerInstance * sizeof(float);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribDivisor(1, 1);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
        GL.VertexAttribDivisor(2, 1);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.VertexAttribDivisor(3, 1);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.VertexAttribDivisor(4, 1);
        return vao;
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
            var required = CellQuadBuilder.InstanceFloatCount(gridWidth * gridHeight);
            if (_backgroundInstances.Length < required)
                _backgroundInstances = new float[required];
            if (_glyphInstances.Length < required)
                _glyphInstances = new float[required];
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
        var changed = _lastGrid is null
            || _lastGrid.Width != _grid.Width
            || _lastGrid.Height != _grid.Height
            || !GridsEqual(_grid, _lastGrid);
        if (changed)
        {
            var (backgroundCount, glyphCount) = CellQuadBuilder.FillInstances(_grid, _backgroundInstances, _glyphInstances);
            _backgroundInstanceCount = backgroundCount;
            _glyphInstanceCount = glyphCount;
            var cellCount = _grid.Width * _grid.Height;
            Upload(_backgroundInstanceVbo, _backgroundInstances, backgroundCount * CellQuadBuilder.FloatsPerInstance, CellQuadBuilder.InstanceFloatCount(cellCount), ref _backgroundGpuCapacity);
            Upload(_glyphInstanceVbo, _glyphInstances, glyphCount * CellQuadBuilder.FloatsPerInstance, CellQuadBuilder.InstanceFloatCount(cellCount), ref _glyphGpuCapacity);
        }

        _lastGrid ??= new CellGrid(_grid.Width, _grid.Height);
        (_grid, _lastGrid) = (_lastGrid, _grid);

        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(_shader);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.Uniform1i(_textureLocation, 0);
        GL.Uniform2f(_gridSizeLocation, _grid.Width, _grid.Height);
        FlushAtlasDirty();

        GL.Uniform1f(_texEnabledLocation, 0f);
        GL.BindVertexArray(_backgroundVao);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, IntPtr.Zero, _backgroundInstanceCount);
        GL.Uniform1f(_texEnabledLocation, 1f);
        GL.BindVertexArray(_glyphVao);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, IntPtr.Zero, _glyphInstanceCount);
        GL.BindVertexArray(0);

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
    

    private int CreateTexture()
    {
        var texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texture);
        var data = _atlas.CreateTextureData();
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.R8,
            GlyphAtlas.Columns * GlyphAtlas.GlyphWidth,
            GlyphAtlas.Rows * GlyphAtlas.GlyphHeight,
            0,
            PixelFormat.Red,
            PixelType.UnsignedByte,
            data);
        GL.TexParameteri(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameteri(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameteri(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameteri(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    private static int CreateShader()
    {
        const string vertexSource = """
            #version 330 core
            layout(location = 0) in vec2 aCorner;
            layout(location = 1) in vec2 aOrigin;
            layout(location = 2) in vec2 aSize;
            layout(location = 3) in float aColorPacked;
            layout(location = 4) in vec4 aUv;
            uniform vec2 uGridSize;
            out vec4 vColor;
            out vec2 vUv;
            void main()
            {
                vec2 cellPos = aOrigin + aCorner * aSize;
                vec2 ndc = vec2(
                    cellPos.x / uGridSize.x * 2.0 - 1.0,
                    1.0 - cellPos.y / uGridSize.y * 2.0);
                gl_Position = vec4(ndc, 0.0, 1.0);
                uint packedColor = floatBitsToUint(aColorPacked);
                vColor = vec4(
                    float(packedColor & 0xFFu),
                    float((packedColor >> 8) & 0xFFu),
                    float((packedColor >> 16) & 0xFFu),
                    float((packedColor >> 24) & 0xFFu)) / 255.0;
                vUv = mix(aUv.xy, aUv.zw, aCorner);
            }
            """;

        const string fragmentSource = """
            #version 330 core
            in vec4 vColor;
            in vec2 vUv;
            uniform sampler2D uTexture;
            uniform float uTexEnabled;
            out vec4 FragColor;
            void main()
            {
                if (uTexEnabled > 0.5)
                {
                    float alpha = texture(uTexture, vUv).r;
                    FragColor = vec4(vColor.rgb, alpha);
                }
                else
                {
                    FragColor = vColor;
                }
            }
            """;

        var vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);
        GL.GetShaderi(vertexShader, ShaderParameterName.CompileStatus, out var vertexStatus);
        if (vertexStatus == 0)
            throw new InvalidOperationException($"Vertex shader compile error: {GL.GetShaderInfoLog(vertexShader)}");

        var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);
        GL.GetShaderi(fragmentShader, ShaderParameterName.CompileStatus, out var fragmentStatus);
        if (fragmentStatus == 0)
            throw new InvalidOperationException($"Fragment shader compile error: {GL.GetShaderInfoLog(fragmentShader)}");

        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);
        GL.GetProgrami(program, ProgramProperty.LinkStatus, out var linkStatus);
        if (linkStatus == 0)
            throw new InvalidOperationException($"Shader link error: {GL.GetProgramInfoLog(program)}");

        GL.DetachShader(program, vertexShader);
        GL.DetachShader(program, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
        return program;
    }

    private static bool GridsEqual(CellGrid current, CellGrid previous)
    {
        for (var y = 0; y < current.Height; y++)
        {
            for (var x = 0; x < current.Width; x++)
            {
                if (current[x, y] != previous[x, y])
                    return false;
            }
        }
        return true;
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
