using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace Dsh.Tui;

public readonly record struct GlyphUv(float MinX, float MinY, float MaxX, float MaxY);

public sealed class GlyphAtlas
{
    public const int GlyphWidth = 16;
    public const int GlyphHeight = 20;
    public const int Columns = 128;
    public const int Rows = 64;
    public const int Capacity = Columns * Rows;

    private const float FontSize = 15f;
    private const string CacheMagic = "DSHGLYF1";

    private static readonly Lazy<GlyphAtlas> SharedInstance = new(() => new GlyphAtlas());
    private static readonly Lazy<Font> SharedFont = new(ResolveFont);
    private static readonly Lazy<IReadOnlyList<FontFamily>> SharedFallbacks = new(ResolveFallbackFamilies);
    private static readonly Lazy<float> SharedOffsetY = new(() => MeasureVerticalOffset(CreateOptions()));

    public static GlyphAtlas Shared => SharedInstance.Value;

    private readonly object _gate = new();
    private readonly int[] _map = CreateEmptyMap();
    private readonly char[] _slotChars = new char[Capacity];
    private readonly long[] _slotTicks = new long[Capacity];
    private readonly GlyphUv[] _uvs = BuildUvs();
    private readonly byte[] _textureData = new byte[Columns * GlyphWidth * Rows * GlyphHeight];
    private readonly List<int> _dirtySlots = [];
    private readonly string _cachePath;
    private int _nextSlot;
    private bool _cacheDirty;

    public GlyphAtlas(string? cachePath = null)
    {
        _cachePath = cachePath ?? DefaultCachePath();
        LoadCache();
    }

    public int AtlasWidth => Columns * GlyphWidth;

    public int AtlasHeight => Rows * GlyphHeight;

    internal byte[] TextureData => _textureData;

    public int DirtyCount
    {
        get
        {
            lock (_gate)
                return _dirtySlots.Count;
        }
    }

    public int GetGlyphIndex(char character)
    {
        var slot = _map[character];
        if (slot >= 0)
        {
            _slotTicks[slot] = Environment.TickCount64;
            return slot;
        }
        return BakeSlow(character);
    }

    public GlyphUv GetUv(char character) => _uvs[GetGlyphIndex(character)];

    public bool IsPixelSet(char character, int x, int y)
    {
        if ((uint)x >= GlyphWidth || (uint)y >= GlyphHeight)
            throw new ArgumentOutOfRangeException(nameof(x));
        var slot = GetGlyphIndex(character);
        return _textureData[((slot / Columns) * GlyphHeight + y) * AtlasWidth + ((slot % Columns) * GlyphWidth + x)] != 0;
    }

    public byte[] CreateTextureData() => (byte[])_textureData.Clone();

    public int FlushDirtyRegions(Span<int> slots)
    {
        lock (_gate)
        {
            var count = Math.Min(slots.Length, _dirtySlots.Count);
            for (var index = 0; index < count; index++)
                slots[index] = _dirtySlots[index];
            _dirtySlots.RemoveRange(0, count);
            return count;
        }
    }

    public void Prewarm()
    {
        for (var character = ' '; character <= '~'; character++)
            GetGlyphIndex(character);
        for (var character = '─'; character <= '┿'; character++)
            GetGlyphIndex(character);
        for (var character = '▀'; character <= '▟'; character++)
            GetGlyphIndex(character);
    }

    public void SaveCacheIfDirty()
    {
        lock (_gate)
        {
            if (!_cacheDirty)
                return;
            var entries = new List<(char Character, int Slot)>();
            for (var slot = 0; slot < _nextSlot; slot++)
                entries.Add((_slotChars[slot], slot));
            var directory = Path.GetDirectoryName(_cachePath);
            if (directory is not null)
                Directory.CreateDirectory(directory);
            var tempPath = $"{_cachePath}.tmp";
            using (var stream = File.Create(tempPath))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CacheMagic);
                var fontName = SharedFont.Value.Family.Name;
                writer.Write(fontName);
                writer.Write(GlyphWidth);
                writer.Write(GlyphHeight);
                writer.Write(Columns);
                writer.Write(Rows);
                writer.Write(FontSize);
                writer.Write(entries.Count);
                foreach (var (character, slot) in entries)
                {
                    writer.Write((ushort)character);
                    writer.Write(slot);
                }
                foreach (var (_, slot) in entries)
                {
                    var slotX = (slot % Columns) * GlyphWidth;
                    var slotY = (slot / Columns) * GlyphHeight;
                    for (var y = 0; y < GlyphHeight; y++)
                        writer.Write(_textureData, ((slotY + y) * AtlasWidth) + slotX, GlyphWidth);
                }
            }
            File.Move(tempPath, _cachePath, overwrite: true);
            _cacheDirty = false;
        }
    }

    private int BakeSlow(char character)
    {
        if (ShouldFallback(character))
            character = '?';
        lock (_gate)
        {
            var slot = _map[character];
            if (slot >= 0)
                return slot;
            slot = _nextSlot < Capacity ? _nextSlot++ : EvictOldest();
            Bake(character, slot);
            _slotChars[slot] = character;
            _slotTicks[slot] = Environment.TickCount64;
            _map[character] = slot;
            _dirtySlots.Add(slot);
            _cacheDirty = true;
            return slot;
        }
    }

    private int EvictOldest()
    {
        var oldest = 0;
        for (var slot = 1; slot < Capacity; slot++)
        {
            if (_slotTicks[slot] < _slotTicks[oldest])
                oldest = slot;
        }
        _map[_slotChars[oldest]] = -1;
        return oldest;
    }

    private void Bake(char character, int slot)
    {
        using var image = new Image<Rgba32>(GlyphWidth, GlyphHeight);
        using (var canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new DrawingOptions()))
        {
            var options = CreateOptions();
            options.Origin = new PointF(0, SharedOffsetY.Value);
            var glyphs = TextBuilder.GenerateGlyphs(character.ToString(), options);
            foreach (var glyph in glyphs)
                canvas.Fill(Brushes.Solid(Color.White), glyph.Paths);
        }

        var slotX = (slot % Columns) * GlyphWidth;
        var slotY = (slot / Columns) * GlyphHeight;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var rowSpan = accessor.GetRowSpan(y);
                for (var x = 0; x < accessor.Width; x++)
                    _textureData[((slotY + y) * AtlasWidth) + slotX + x] = rowSpan[x].A;
            }
        });
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return;
            using var stream = File.OpenRead(_cachePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadString() != CacheMagic)
                return;
            var fontName = reader.ReadString();
            if (fontName != SharedFont.Value.Family.Name
                || reader.ReadInt32() != GlyphWidth
                || reader.ReadInt32() != GlyphHeight
                || reader.ReadInt32() != Columns
                || reader.ReadInt32() != Rows
                || Math.Abs(reader.ReadSingle() - FontSize) > 0.001f)
                return;
            var count = reader.ReadInt32();
            if (count <= 0 || count > Capacity)
                return;
            var entries = new (char Character, int Slot)[count];
            for (var index = 0; index < count; index++)
            {
                var character = (char)reader.ReadUInt16();
                var slot = reader.ReadInt32();
                if (slot < 0 || slot >= Capacity)
                    return;
                entries[index] = (character, slot);
            }
            foreach (var (character, slot) in entries)
            {
                var slotX = (slot % Columns) * GlyphWidth;
                var slotY = (slot / Columns) * GlyphHeight;
                for (var y = 0; y < GlyphHeight; y++)
                {
                    var read = reader.Read(_textureData, ((slotY + y) * AtlasWidth) + slotX, GlyphWidth);
                    if (read != GlyphWidth)
                        return;
                }
                _slotChars[slot] = character;
                _slotTicks[slot] = Environment.TickCount64;
                _map[character] = slot;
                _nextSlot = Math.Max(_nextSlot, slot + 1);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ShouldFallback(char character)
        => character < ' ' || character is >= '\u007F' and <= '\u009F' or >= '\uE000' and <= '\uF8FF';

    private static int[] CreateEmptyMap()
    {
        var map = new int[char.MaxValue + 1];
        Array.Fill(map, -1);
        return map;
    }

    private static GlyphUv[] BuildUvs()
    {
        var uvs = new GlyphUv[Capacity];
        for (var slot = 0; slot < Capacity; slot++)
        {
            var column = slot % Columns;
            var row = slot / Columns;
            uvs[slot] = new GlyphUv(
                column / (float)Columns,
                row / (float)Rows,
                (column + 1) / (float)Columns,
                (row + 1) / (float)Rows);
        }
        return uvs;
    }

    private static TextOptions CreateOptions()
        => new(SharedFont.Value)
        {
            FallbackFontFamilies = SharedFallbacks.Value,
        };

    private static string DefaultCachePath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "cache", "glyph-atlas.bin");

    private static float MeasureVerticalOffset(TextOptions options)
    {
        var probe = TextBuilder.GenerateGlyphs("Hg中", options);
        if (probe.Count == 0)
            return 0f;
        var top = probe.Min(glyph => glyph.Bounds.Y);
        var bottom = probe.Max(glyph => glyph.Bounds.Y + glyph.Bounds.Height);
        return ((GlyphHeight - (bottom - top)) / 2f) - top;
    }

    private static IReadOnlyList<FontFamily> ResolveFallbackFamilies()
    {
        string[] names =
        [
            "Microsoft YaHei",
            "DengXian",
            "SimSun",
            "MS Gothic",
            "PingFang SC",
            "Hiragino Sans GB",
            "Noto Sans Mono CJK SC",
            "Noto Sans Mono CJK TC",
            "Noto Sans CJK SC",
            "Noto Sans CJK TC",
            "Noto Sans CJK JP",
            "WenQuanYi Micro Hei",
            "Segoe UI Symbol",
            "Segoe UI Emoji",
            "Apple Color Emoji",
            "Noto Color Emoji",
        ];

        var families = new List<FontFamily>();
        foreach (var name in names)
        {
            if (SystemFonts.TryGet(name, out var family))
                families.Add(family);
        }

        return families;
    }

    private static Font ResolveFont()
    {
        string[] preferredNames =
        [
            "Cascadia Mono",
            "Cascadia Code",
            "JetBrains Mono",
            "Consolas",
            "Menlo",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Noto Sans Mono CJK SC",
            "Noto Sans Mono CJK TC",
            "Noto Sans CJK SC",
            "Noto Sans CJK TC",
            "Microsoft YaHei",
        ];

        foreach (var name in preferredNames)
        {
            if (SystemFonts.TryGet(name, out var family))
                return family.CreateFont(FontSize);
        }

        foreach (var family in SystemFonts.Families)
        {
            if (family.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                family.Name.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                family.Name.Contains("CJK", StringComparison.OrdinalIgnoreCase))
            {
                return family.CreateFont(FontSize);
            }
        }

        var fallbackName = SystemFonts.GetDefaultFamilyName();
        return SystemFonts.CreateFont(fallbackName, FontSize);
    }
}
