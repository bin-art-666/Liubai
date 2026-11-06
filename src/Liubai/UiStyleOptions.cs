using System.Text.Json;

namespace Liubai;

internal enum UiStylePreset { Default, Rounded, Compact, Custom }
internal enum UiDensity { Compact, Standard, Spacious }
internal enum UiIconSize { Small, Standard, Large }
internal enum UiMotion { Off, Gentle }
internal sealed record UiStyleOptions(int Version = 1, UiStylePreset Preset = UiStylePreset.Default,
    int Roundness = 8, UiDensity Density = UiDensity.Standard, UiIconSize Icons = UiIconSize.Standard,
    int Tint = 22, int Shade = 78, UiMotion Motion = UiMotion.Gentle, int Duration = 160)
{
    internal static UiStyleOptions ForPreset(UiStylePreset preset) => preset switch
    {
        UiStylePreset.Default => new(),
        UiStylePreset.Rounded => new(Preset: preset, Roundness: 12, Density: UiDensity.Spacious, Tint: 28, Shade: 82, Duration: 200),
        UiStylePreset.Compact => new(Preset: preset, Roundness: 6, Density: UiDensity.Compact, Icons: UiIconSize.Small, Duration: 120),
        _ => throw new ArgumentException("自定义风格需要保留当前参数。")
    };
    internal static void Validate(UiStyleOptions value)
    {
        if (value.Version != 1) throw new NotSupportedException("界面风格来自更新版本。");
        if (!Enum.IsDefined(value.Preset) || !Enum.IsDefined(value.Density) || !Enum.IsDefined(value.Icons) || !Enum.IsDefined(value.Motion)
            || value.Roundness is < 0 or > 20 || value.Tint is < 0 or > 100 || value.Shade is < 0 or > 100 || value.Duration is < 100 or > 300)
            throw new InvalidDataException("界面风格参数超出范围。");
    }
}

internal sealed class UiStyleStore
{
    readonly string path;
    bool preserveBackup;
    internal UiStyleOptions Data { get; private set; } = new();
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    internal UiStyleStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { Data = Read(path); }
        catch (NotSupportedException) { ReadOnly = true; Warning = "界面风格来自更新版本，已停止写入并保留原文件。"; }
        catch (Exception ex) when (ReadError(ex))
        {
            try { Data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复界面风格。"; }
            catch (Exception backup) when (ReadError(backup) || backup is NotSupportedException)
            { ReadOnly = true; Warning = "界面风格无法读取，暂用默认效果；原文件已保留。"; }
        }
    }
    static bool ReadError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException;
    static UiStyleOptions Read(string path)
    {
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("界面风格配置过大。");
        var value = JsonSerializer.Deserialize<UiStyleOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException("界面风格为空。");
        UiStyleOptions.Validate(value); return value;
    }
    internal void Save(UiStyleOptions value)
    {
        UiStyleOptions.Validate(value);
        if (ReadOnly) throw new IOException(Warning);
        if (value == Data && File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
            else File.Move(path + ".tmp", path);
            preserveBackup = false; Data = value;
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
    }
}
