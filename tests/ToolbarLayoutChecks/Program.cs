using Liubai;
using System.Text.Json;

int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException) { count++; return; } throw new Exception("Invalid input accepted"); }
var widths = ToolbarOrder.Defaults.ToDictionary(id => id, _ => 34d);
ToolbarPlan Plan(ToolbarLayoutOptions value, double width = 1000) => ToolbarLayoutPolicy.Plan(ToolbarOrder.Defaults, value, widths, width);
var defaults = new ToolbarLayoutOptions();
Check(Plan(defaults).Visible.SequenceEqual(ToolbarOrder.Defaults) && !Plan(defaults).ShowMore, "Default layout unchanged");
var custom = defaults.WithEntry(new("ai", ToolbarPlacement.More)).WithEntry(new("guard", ToolbarPlacement.Hidden)).WithEntry(new("station", GroupStart: true)) with { Spacing = 8 };
Check(Plan(custom).More.SequenceEqual(new[] { "ai" }) && !Plan(custom).Visible.Contains("guard") && !Plan(custom).More.Contains("guard"), "More and Hidden are distinct");
Check(Plan(custom).ShowMore && Plan(custom).Visible.Contains("settings"), "More and settings accessible");
foreach (double width in new[] { 0d, 34, 80, 130, 200, 500, double.NaN })
{
    var plan = Plan(custom, width);
    Check(plan.Visible.Contains("settings") && !plan.More.Contains("settings"), "Settings retained");
    var all = plan.Visible.Concat(plan.More).ToArray();
    Check(all.Distinct().Count() == 9 && !all.Contains("guard"), "No lost or duplicate entries");
}
Check(Plan(custom, 130).More.Length > 1, "Automatic overflow combines explicit entries");
Check(Plan(defaults, 10).Visible.SequenceEqual(new[] { "settings" }) && !Plan(defaults, 10).ShowMore, "Extremely narrow settings priority");
widths["clock"] = 160;
Check(Plan(defaults, 200).More.Length > 0, "Variable width countdown handled"); widths["clock"] = 34;
var grouped = defaults.WithEntry(new("qr", GroupStart: true));
Check(Plan(grouped, 359).More.Length > Plan(defaults, 359).More.Length, "Grouping reserves space");
foreach (var invalid in new[] { defaults with { Version = 2 }, defaults with { Spacing = -1 }, defaults with { Spacing = 17 }, defaults with { GroupGap = 25 }, defaults.WithEntry(new("settings", ToolbarPlacement.More)), defaults.WithEntry(new("settings", ToolbarPlacement.Hidden)), defaults.WithEntry(new("unknown")), defaults.WithEntry(new("qr", (ToolbarPlacement)99)), defaults with { Entries = [new("ai"), new("ai")] } }) Reject(() => Plan(invalid));
string root = Path.Combine(Path.GetTempPath(), "Liubai-Toolbar-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string path = Path.Combine(root, "layout.json"); var store = new ToolbarLayoutStore(path);
    Check(store.Data == defaults && !File.Exists(path), "Defaults do not create a file");
    int events = 0; store.Changed += () => events++; store.Preview(custom);
    Check(store.Current == custom && store.Data == defaults && !File.Exists(path) && events == 1, "Live preview separate from disk");
    store.Save(store.Current); var read = new ToolbarLayoutStore(path);
    Check(read.Data.Spacing == 8 && read.Data.Entry("ai").Placement == ToolbarPlacement.More && read.Data.Entry("guard").Placement == ToolbarPlacement.Hidden && read.Data.Entry("station").GroupStart, "Complete roundtrip");
    store.Save(defaults); string backup = File.ReadAllText(path + ".bak"); File.WriteAllText(path, "broken");
    read = new(path); Check(!read.ReadOnly && read.Data.Entry("ai").Placement == ToolbarPlacement.More, "Backup recovery");
    read.Save(defaults); Check(File.ReadAllText(path + ".bak") == backup, "Recovered backup preserved");
    File.WriteAllText(path, JsonSerializer.Serialize(defaults with { Version = 2 })); read = new(path);
    Check(read.ReadOnly, "Future version protected"); Reject(() => read.Preview(custom)); Reject(() => read.Save(defaults));
    string corrupt = Path.Combine(root, "corrupt.json"); File.WriteAllText(corrupt, "null"); Check(new ToolbarLayoutStore(corrupt).ReadOnly, "Unrecoverable config retained");
    Console.WriteLine($"PASS: {count} toolbar layout checks");
}
finally { if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true); }
