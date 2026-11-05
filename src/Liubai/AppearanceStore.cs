using System.Text.Json;

namespace Liubai;

public enum GlassStyle { Light, Frosted, Acrylic, Custom }

public sealed record MaterialOptions(bool Thin = false, float TintOpacity = .22f,
    float LuminosityOpacity = .78f, bool FollowTheme = true, string TintHex = "#D5E9E1", bool ShowLine = false, bool NoBase = false)
{
    internal (byte Alpha, uint Rgb) FlatColor(bool dark)
    {
        uint tint = FollowTheme ? (dark ? 0x141D1Cu : 0xF6FBF9u) : Convert.ToUInt32(TintHex[1..], 16);
        double tintAlpha = TintOpacity, maskAlpha = LuminosityOpacity * (1 - tintAlpha);
        double alpha = tintAlpha + maskAlpha;
        if (alpha == 0) return (0, 0);
        uint Channel(int shift) => (uint)Math.Clamp(Math.Round((((tint >> shift) & 255) * tintAlpha
            + (dark ? 0 : 255) * maskAlpha) / alpha), 0, 255);
        return ((byte)Math.Round(alpha * 255), (Channel(16) << 16) | (Channel(8) << 8) | Channel(0));
    }
    public static MaterialOptions ForPreset(GlassStyle style) => style switch
    {
        GlassStyle.Light => new(true, .08f, .55f),
        GlassStyle.Acrylic => new(false, .45f, .94f),
        _ => new()
    };

    public static bool IsColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    internal bool IsValid => float.IsFinite(TintOpacity) && TintOpacity is >= 0 and <= 1
        && float.IsFinite(LuminosityOpacity) && LuminosityOpacity is >= 0 and <= 1 && IsColor(TintHex);
}

public sealed record PersonalPreset(string Id, string Name, GlassStyle Style, MaterialOptions Custom,
    int BarHeight, ExpandedAppearanceOptions Expanded);

internal sealed record AppearanceConfiguration(GlassStyle Style = GlassStyle.Frosted, MaterialOptions? Custom = null,
    int BarHeight = AppearanceStore.DefaultHeight, ExpandedAppearanceOptions? Expanded = null,
    bool AllDisplays = true, bool FollowPaused = false, PersonalPreset[]? Presets = null, int Version = 1,
    int ClockFontSize = AppearanceStore.DefaultClockFontSize, string[]? ToolbarOrder = null);

public sealed class AppearanceStore
{
    public const int DefaultHeight = 36, MinimumHeight = 1, MaximumHeight = 120;
    public const int DefaultClockFontSize = 18, MinimumClockFontSize = 12, MaximumClockFontSize = 26;
    readonly string path;
    public GlassStyle Style { get; private set; } = GlassStyle.Frosted;
    public int BarHeight { get; private set; } = DefaultHeight;
    public int ClockFontSize { get; private set; } = DefaultClockFontSize;
    public event Action? ClockFontSizeChanged;
    public IReadOnlyList<string> ToolbarItems { get; private set; } = Array.AsReadOnly(ToolbarOrder.Normalize(null));
    public event Action? ToolbarOrderChanged;
    public MaterialOptions Custom { get; private set; } = new();
    public ExpandedAppearanceOptions Expanded { get; private set; } = new();
    public bool AllDisplays { get; private set; } = true;
    public bool FollowPaused { get; private set; }
    public IReadOnlyList<PersonalPreset> Presets { get; private set; } = Array.Empty<PersonalPreset>();
    public MaterialOptions Material => Style == GlassStyle.Custom ? Custom : MaterialOptions.ForPreset(Style);

    public AppearanceStore(string path)
    {
        this.path = path;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        AppearanceConfiguration saved;
        try { saved = Read(path); }
        catch { try { saved = Read(path + ".bak"); } catch { return; } }
        Apply(saved);
    }

    void Apply(AppearanceConfiguration saved)
    {
        bool fontChanged = ClockFontSize != saved.ClockFontSize;
        var order = ToolbarOrder.Normalize(saved.ToolbarOrder);
        bool orderChanged = !ToolbarItems.SequenceEqual(order);
        Style = saved.Style;
        Custom = saved.Custom ?? new();
        BarHeight = saved.BarHeight;
        ClockFontSize = saved.ClockFontSize;
        Expanded = saved.Expanded ?? new();
        AllDisplays = saved.AllDisplays; FollowPaused = saved.FollowPaused;
        Presets = Array.AsReadOnly(saved.Presets ?? []);
        ToolbarItems = Array.AsReadOnly(order);
        if (fontChanged) ClockFontSizeChanged?.Invoke();
        if (orderChanged) ToolbarOrderChanged?.Invoke();
    }

    static AppearanceConfiguration Read(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("配置文件超过 1 MB。");
        var value = JsonSerializer.Deserialize<AppearanceConfiguration>(File.ReadAllText(path));
        Validate(value);
        return value!;
    }

    static void Validate(AppearanceConfiguration? value)
    {
        bool valid = value != null && value.Version == 1 && Enum.IsDefined(value.Style) && (value.Custom == null || value.Custom.IsValid)
            && value.BarHeight is >= MinimumHeight and <= MaximumHeight
            && value.ClockFontSize is >= MinimumClockFontSize and <= MaximumClockFontSize
            && (value.ToolbarOrder == null || value.ToolbarOrder.Length <= 64)
            && (value.Expanded == null || value.Expanded.IsValid);
        var presets = value?.Presets ?? [];
        valid &= presets.Length <= 30 && presets.All(p => p != null && Guid.TryParse(p.Id, out _)
            && !string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 40 && p.Name == p.Name.Trim()
            && Enum.IsDefined(p.Style) && p.Custom != null && p.Custom.IsValid && p.Expanded != null && p.Expanded.IsValid
            && p.BarHeight is >= MinimumHeight and <= MaximumHeight);
        if (!valid || presets.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Length
            || presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Length)
            throw new InvalidDataException("配置格式或参数无效，当前设置未更改。");
    }

    AppearanceConfiguration Snapshot() => new(Style, Custom, BarHeight, Expanded, AllDisplays, FollowPaused, Presets.ToArray(), ClockFontSize: ClockFontSize, ToolbarOrder: ToolbarItems.ToArray());
    public void SaveToolbarOrder(IEnumerable<string> order) => Commit(Snapshot() with { ToolbarOrder = ToolbarOrder.Normalize(order) });
    public void SetAllDisplays(bool value) => AllDisplays = value;
    public void SetFollowPaused(bool value) => FollowPaused = value;

    public void Export(string destination) => Write(destination, Snapshot(), false);
    public void Import(string source) => Commit(Read(source));
    public void ResetDefaults() => Commit(new(Presets: Presets.ToArray()));

    public void SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40) throw new ArgumentException("预设名称需为 1–40 个字符。");
        var existing = Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing == null && Presets.Count >= 30) throw new InvalidOperationException("最多保存 30 个个人预设。");
        var preset = new PersonalPreset(existing?.Id ?? Guid.NewGuid().ToString("N"), name, Style, Custom, BarHeight, Expanded);
        Commit(Snapshot() with { Presets = Presets.Where(p => p.Id != preset.Id).Append(preset).ToArray() });
    }
    public void ApplyPreset(string id)
    {
        var preset = Presets.First(p => p.Id == id);
        Commit(Snapshot() with { Style = preset.Style, Custom = preset.Custom, BarHeight = preset.BarHeight, Expanded = preset.Expanded });
    }
    public void DeletePreset(string id) => Commit(Snapshot() with { Presets = Presets.Where(p => p.Id != id).ToArray() });

    void Commit(AppearanceConfiguration value) { Validate(value); Write(path, value); Apply(value); }

    public void Preview(MaterialOptions material)
    {
        if (!material.IsValid) throw new ArgumentOutOfRangeException(nameof(material));
        Custom = material;
        Style = GlassStyle.Custom;
    }

    public void SaveCurrent() => Save(Style);

    public void SetExpandedAppearance(ExpandedAppearanceOptions options)
    {
        if (!options.IsValid) throw new ArgumentOutOfRangeException(nameof(options));
        Expanded = options;
    }

    public void SetHeight(int height)
    {
        if (height is < MinimumHeight or > MaximumHeight) throw new ArgumentOutOfRangeException(nameof(height));
        BarHeight = height;
    }

    public void SetClockFontSize(int size)
    {
        if (size is < MinimumClockFontSize or > MaximumClockFontSize) throw new ArgumentOutOfRangeException(nameof(size));
        if (ClockFontSize == size) return;
        ClockFontSize = size; ClockFontSizeChanged?.Invoke();
    }

    public void Save(GlassStyle style)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        Commit(Snapshot() with { Style = style });
    }

    static void Write(string destination, AppearanceConfiguration value, bool backup = true)
    {
        Validate(value);
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var stream = new FileStream(destination + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        if (File.Exists(destination)) File.Replace(destination + ".tmp", destination, backup ? destination + ".bak" : null);
        else File.Move(destination + ".tmp", destination);
    }
}
