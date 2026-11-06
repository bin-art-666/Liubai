using System.Text.Json;
using Liubai;

int count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IOException or UnauthorizedAccessException) { count++; return; } throw new Exception("Invalid setting accepted"); }
string root = Path.Combine(Path.GetTempPath(), "Liubai-UiStyle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string path = Path.Combine(root, "ui-style.json"); var store = new UiStyleStore(path);
    Check(store.Data == new UiStyleOptions() && !File.Exists(path), "First run uses defaults without creating files");
    foreach (var preset in new[] { UiStylePreset.Default, UiStylePreset.Rounded, UiStylePreset.Compact })
    { var value = UiStyleOptions.ForPreset(preset); UiStyleOptions.Validate(value); Check(value.Preset == preset, "Preset identity"); }
    Reject(() => UiStyleOptions.ForPreset(UiStylePreset.Custom));
    var custom = new UiStyleOptions(Preset: UiStylePreset.Custom, Roundness: 20, Density: UiDensity.Spacious, Icons: UiIconSize.Large, Tint: 0, Shade: 100, Motion: UiMotion.Off, Duration: 300);
    store.Save(custom); Check(new UiStyleStore(path).Data == custom, "All custom parameters survive reload");
    foreach (var invalid in new[] { custom with { Version = 2 }, custom with { Roundness = -1 }, custom with { Roundness = 21 }, custom with { Tint = 101 }, custom with { Shade = -1 }, custom with { Duration = 99 }, custom with { Duration = 301 }, custom with { Density = (UiDensity)99 }, custom with { Icons = (UiIconSize)99 }, custom with { Motion = (UiMotion)99 }, custom with { Preset = (UiStylePreset)99 } }) Reject(() => store.Save(invalid));
    Check(new UiStyleStore(path).Data == custom, "Invalid writes leave existing settings intact");
    store.Save(new()); string backup = File.ReadAllText(path + ".bak"); File.WriteAllText(path, "{broken");
    var recovered = new UiStyleStore(path); Check(recovered.Data == custom && !recovered.ReadOnly && recovered.Warning != null, "Valid backup recovers corrupt primary");
    recovered.Save(new()); Check(File.ReadAllText(path + ".bak") == backup && new UiStyleStore(path).Data == new UiStyleOptions(), "Recovery save preserves known-good backup");
    File.WriteAllText(path, JsonSerializer.Serialize(custom with { Version = 2 })); string future = File.ReadAllText(path);
    var newer = new UiStyleStore(path); Check(newer.ReadOnly, "Future version remains read-only even with old backup"); Reject(() => newer.Save(new()));
    Check(File.ReadAllText(path) == future, "Future file preserved");
    string bad = Path.Combine(root, "bad.json"); File.WriteAllText(bad, "null"); var unreadable = new UiStyleStore(bad);
    Check(unreadable.ReadOnly && unreadable.Data == new UiStyleOptions(), "Unrecoverable data falls back without overwriting"); Reject(() => unreadable.Save(new()));
    string blocked = Path.Combine(root, "blocked.json"); var blockedStore = new UiStyleStore(blocked); Directory.CreateDirectory(blocked + ".tmp");
    Reject(() => blockedStore.Save(custom)); Check(blockedStore.Data == new UiStyleOptions() && !File.Exists(blocked), "Failed atomic save does not claim success");
    Console.WriteLine($"PASS: {count} UI style checks");
}
finally { if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true); }
