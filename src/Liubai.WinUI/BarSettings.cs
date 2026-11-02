using System.Text.Json;

namespace Liubai.Modern;

public sealed class BarSettings
{
    sealed class SavedMode { public bool AutoHide { get; set; } }
    readonly string path;
    public bool AutoHide { get; private set; }
    public BarSettings(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        try { AutoHide = Read(path); }
        catch { try { AutoHide = Read(path + ".bak"); } catch { AutoHide = false; } }
    }
    static bool Read(string path) => (JsonSerializer.Deserialize<SavedMode>(File.ReadAllText(path)) ?? throw new InvalidDataException()).AutoHide;
    public void Save(bool autoHide)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new SavedMode { AutoHide = autoHide }); stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
        else File.Move(path + ".tmp", path);
        AutoHide = autoHide;
    }
}
