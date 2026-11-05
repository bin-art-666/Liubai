using System.Text.Json;

namespace Liubai;

internal sealed record StationPanelRect(int X, int Y, int Width, int Height);
internal sealed record StationPanelOptions(int Version = 1, double Width = 430, double Height = 660,
    double RightOffset = 0, double TopOffset = 0, StationPanelRect? Floating = null, StationQuery? View = null);
internal static class StationPanelPolicy
{
    internal const double MinimumWidth = 360, MaximumWidth = 1000, MinimumHeight = 360, MaximumHeight = 1200;
    internal static void Validate(StationPanelOptions value)
    {
        if (value.Version != 1) throw new NotSupportedException();
        if (value.View != null) StationQuery.Validate(value.View);
        if (!double.IsFinite(value.Width) || value.Width is < MinimumWidth or > MaximumWidth
            || !double.IsFinite(value.Height) || value.Height is < MinimumHeight or > MaximumHeight
            || !double.IsFinite(value.RightOffset) || value.RightOffset is < 0 or > 20000
            || !double.IsFinite(value.TopOffset) || value.TopOffset is < 0 or > 20000
            || value.Floating is { } f && (f.Width is < 100 or > 20000 || f.Height is < 100 or > 20000 || Math.Abs((long)f.X) > 100000 || Math.Abs((long)f.Y) > 100000))
            throw new InvalidDataException("中转站窗口尺寸无效。");
    }
    internal static StationPanelRect Bounds(StationPanelOptions value, StationPanelRect work, int anchor, uint dpi)
    {
        double scale = Math.Max(96, dpi) / 96d;
        int right = (int)Math.Clamp((long)work.X + work.Width - (long)Math.Round(value.RightOffset * scale), work.X + Math.Min(work.Width, (int)Math.Round(MinimumWidth * scale)), (long)work.X + work.Width);
        int top = (int)Math.Clamp((long)Math.Max(anchor, work.Y) + (long)Math.Round(value.TopOffset * scale), work.Y, (long)work.Y + work.Height - Math.Min(work.Height, (int)Math.Round(MinimumHeight * scale)));
        int width = Math.Min((int)Math.Round(value.Width * scale), right - work.X);
        int height = Math.Min((int)Math.Round(value.Height * scale), work.Y + work.Height - top);
        return new(right - width, top, width, height);
    }
}
internal sealed class StationPanelLayout
{
    readonly string path;
    bool preserveBackup;
    internal StationPanelOptions Data { get; private set; } = new();
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    internal StationPanelLayout(string path)
    {
        this.path = path;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { Data = Read(path); }
        catch (NotSupportedException) { ReadOnly = true; Warning = "窗口设置来自更新版本，已停止写入。"; }
        catch
        {
            try { Data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复中转站窗口尺寸。"; }
            catch { ReadOnly = true; Warning = "窗口尺寸设置无法读取，暂用默认尺寸；原文件已保留。"; }
        }
    }
    static StationPanelOptions Read(string path)
    {
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize<StationPanelOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        StationPanelPolicy.Validate(value); return value;
    }
    internal void Save(StationPanelOptions value)
    {
        StationPanelPolicy.Validate(value); if (ReadOnly) throw new IOException(Warning);
        if (value == Data) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak"); else File.Move(path + ".tmp", path);
            Data = value; preserveBackup = false;
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
    }
}
