using System.Text.Json;

namespace Liubai;

internal enum ToolbarPlacement { Bar, More, Hidden }
internal sealed record ToolbarEntry(string Id, ToolbarPlacement Placement = ToolbarPlacement.Bar, bool GroupStart = false);
internal sealed record ToolbarLayoutOptions(int Version = 1, int Spacing = 2, int GroupGap = 10, ToolbarEntry[]? Entries = null)
{
    internal ToolbarEntry Entry(string id) => Entries?.FirstOrDefault(e => e.Id == id) ?? new(id);
    internal ToolbarLayoutOptions WithEntry(ToolbarEntry entry) => this with { Entries = (Entries ?? []).Where(e => e.Id != entry.Id).Append(entry).ToArray() };
    internal static void Validate(ToolbarLayoutOptions value)
    {
        if (value.Version != 1) throw new NotSupportedException();
        var entries = value.Entries ?? [];
        if (value.Spacing is < 0 or > 16 || value.GroupGap is < 4 or > 24 || entries.Length > ToolbarOrder.Defaults.Length
            || entries.Any(e => e == null || !ToolbarOrder.Defaults.Contains(e.Id) || !Enum.IsDefined(e.Placement) || ToolCatalog.BuiltIn[e.Id].RequiredEntry && e.Placement != ToolbarPlacement.Bar)
            || entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length) throw new InvalidDataException("顶栏布局参数无效。");
    }
}
internal sealed record ToolbarPlan(string[] Visible, string[] More, bool ShowMore);
internal static class ToolbarLayoutPolicy
{
    internal static readonly string[] Names = ToolCatalog.BuiltIn.Items.Select(tool => tool.Name).ToArray();
    internal static string Name(string id) => ToolCatalog.BuiltIn[id].Name;
    internal static ToolbarPlan Plan(IEnumerable<string> order, ToolbarLayoutOptions options, IReadOnlyDictionary<string, double> widths, double available)
    {
        ToolbarLayoutOptions.Validate(options);
        var all = ToolbarOrder.Normalize(order);
        double Width(string id) => widths.TryGetValue(id, out double width) && double.IsFinite(width) && width > 0 ? width : 34;
        var visible = all.Where(id => options.Entry(id).Placement == ToolbarPlacement.Bar).ToList();
        var more = all.Where(id => options.Entry(id).Placement == ToolbarPlacement.More).ToHashSet(StringComparer.Ordinal);
        double Cost()
        {
            double size = visible.Sum(Width) + Math.Max(0, visible.Count - 1) * options.Spacing;
            size += visible.Skip(1).Count(id => options.Entry(id).GroupStart) * options.GroupGap;
            return size + (more.Count > 0 ? 34 + (visible.Count > 0 ? options.Spacing : 0) : 0);
        }
        available = double.IsFinite(available) ? Math.Max(0, available) : 0;
        while (Cost() > available && visible.Any(id => !ToolCatalog.BuiltIn[id].RequiredEntry))
        { string id = visible.Last(id => !ToolCatalog.BuiltIn[id].RequiredEntry); visible.Remove(id); more.Add(id); }
        bool showMore = more.Count > 0 && Cost() <= available;
        return new(visible.ToArray(), all.Where(more.Contains).ToArray(), showMore);
    }
}
internal sealed class ToolbarLayoutStore
{
    readonly string path;
    bool preserveBackup;
    ToolbarLayoutOptions? preview;
    internal ToolbarLayoutOptions Data { get; private set; } = new();
    internal ToolbarLayoutOptions Current => preview ?? Data;
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    internal event Action? Changed;
    internal ToolbarLayoutStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { Data = Read(path); }
        catch (NotSupportedException) { ReadOnly = true; Warning = "顶栏布局来自更新版本，已保留原文件并停止写入。"; }
        catch (Exception ex) when (ReadError(ex))
        {
            try { Data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复顶栏布局。"; }
            catch (Exception backup) when (ReadError(backup) || backup is NotSupportedException)
            { ReadOnly = true; Warning = "顶栏布局无法读取，暂用默认排列；原文件已保留。"; }
        }
    }
    static bool ReadError(Exception ex) => ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException;
    static ToolbarLayoutOptions Read(string path)
    {
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize<ToolbarLayoutOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        ToolbarLayoutOptions.Validate(value); return value;
    }
    internal void Save(ToolbarLayoutOptions value)
    {
        ToolbarLayoutOptions.Validate(value); if (ReadOnly) throw new IOException(Warning);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak"); else File.Move(path + ".tmp", path);
            Data = value; preview = null; preserveBackup = false;
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        Changed?.Invoke();
    }
    internal void Preview(ToolbarLayoutOptions value)
    {
        ToolbarLayoutOptions.Validate(value); if (ReadOnly) throw new IOException(Warning);
        preview = value; Changed?.Invoke();
    }
}
