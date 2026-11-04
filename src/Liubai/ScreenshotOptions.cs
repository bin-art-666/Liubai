using System.Text.Json;

namespace Liubai;

internal sealed record ScreenshotOptions(bool SaveFiles = true, string? Directory = null, int Version = 1)
{
    internal static string DefaultDirectory
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures)) pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            return Path.Combine(pictures, "留白截图");
        }
    }
    internal string SaveDirectory => Directory ?? DefaultDirectory;
}

// Local paths belong to this machine and do not travel with appearance presets.
internal sealed class ScreenshotOptionsStore
{
    readonly string path;
    bool preserveBackup;
    internal ScreenshotOptions Value { get; private set; } = new();
    internal ScreenshotOptionsStore(string path)
    {
        this.path = path;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { Value = Read(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            try { Value = Read(path + ".bak"); preserveBackup = true; }
            catch (Exception backup) when (backup is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
    }
    static ScreenshotOptions Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("截图设置无效。");
        var value = JsonSerializer.Deserialize<ScreenshotOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException("截图设置为空。");
        Validate(value); return value;
    }
    static void Validate(ScreenshotOptions value)
    {
        if (value.Version != 1 || !Path.IsPathFullyQualified(value.SaveDirectory)) throw new InvalidDataException("截图目录必须是完整路径。");
        _ = Path.GetFullPath(value.SaveDirectory);
    }
    internal void Save(ScreenshotOptions value)
    {
        Validate(value);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
        else File.Move(path + ".tmp", path);
        preserveBackup = false; Value = value;
    }
}
