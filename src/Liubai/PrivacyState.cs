using System.Text.Json;

namespace Liubai;

internal sealed record PrivacyDevice(string Id, string Name, string Kind, bool Enabled, bool Disabled);
internal sealed record PrivacyEntry(string Id, string Name, string Kind, string State = "planned", string Error = "");
internal sealed record PrivacyJournal(int Version = 1, bool Active = false, PrivacyEntry[]? Entries = null);
internal sealed record PrivacyOptions(int Version = 1, string[]? Devices = null);
internal static class PrivacySelection
{
    internal static string[] Resolve(PrivacyOptions options, bool configured, IEnumerable<PrivacyDevice> available)
        => configured ? (options.Devices ?? []).ToArray()
            : available.Where(d => d.Kind is "摄像头" or "麦克风").Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
internal static class PrivacyFiles
{
    internal static T Read<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;
        if (new FileInfo(path).Length > 262144) throw new InvalidDataException("隐私记录过大，已停止操作。");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("隐私记录无法读取。");
    }
    internal static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void Validate(PrivacyJournal value)
    {
        if (value.Version != 1 || value.Entries is null || value.Entries.Length > 128
            || value.Entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Entries.Length
            || value.Entries.Any(e => !ValidId(e.Id) || e.Name is null || e.Name.Length > 1024
                || e.Kind is not ("摄像头" or "麦克风") || e.State is not ("planned" or "pending" or "disabled" or "failed" or "restored")
                || e.Error is null || e.Error.Length > 2048)) throw new InvalidDataException("隐私恢复记录无效，已停止操作。");
    }
    internal static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 2048 && !id.Any(char.IsControl);
}
internal interface IPrivacyDevices
{
    PrivacyDevice? Find(string id);
    void SetEnabled(string id, bool enabled);
}
internal static class PrivacyOperation
{
    // Persist intent before every device mutation. Never enable a pre-disabled/unselected device.
    internal static PrivacyJournal Execute(PrivacyJournal journal, bool restore, IPrivacyDevices devices, Action<PrivacyJournal> save)
    {
        PrivacyFiles.Validate(journal);
        if (!journal.Active) return journal;
        var entries = journal.Entries!.ToArray();
        void Commit() { journal = journal with { Entries = entries.ToArray() }; save(journal); }
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (restore && entry.State is "planned" or "failed" or "restored")
            { entries[i] = entry with { State = "restored", Error = "" }; Commit(); continue; }
            if (!restore && entry.State != "planned") continue;
            try
            {
                var device = devices.Find(entry.Id) ?? throw new IOException("设备未连接，请连接后重试恢复。");
                if (device.Kind != entry.Kind) throw new IOException("设备类型已变化，未执行操作。");
                if (restore)
                {
                    if (device.Disabled) devices.SetEnabled(entry.Id, true);
                    if (devices.Find(entry.Id)?.Enabled != true) throw new IOException("未能确认设备已恢复，记录已保留。");
                    entries[i] = entry with { State = "restored", Error = "" };
                }
                else
                {
                    if (!device.Enabled) { entries[i] = entry with { State = "restored", Error = "设备原本不可用，未修改。" }; Commit(); continue; }
                    entries[i] = entry with { State = "pending" }; Commit();
                    devices.SetEnabled(entry.Id, false);
                    if (devices.Find(entry.Id)?.Disabled != true) throw new IOException("未能确认禁用成功，请检查设备管理器。");
                    entries[i] = entry with { State = "disabled", Error = "" };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { entries[i] = entries[i] with { State = restore ? entries[i].State : entries[i].State == "pending" ? "pending" : "failed", Error = ex.Message }; }
            Commit();
        }
        if (restore && entries.All(e => e.State == "restored")) { journal = journal with { Active = false }; save(journal); }
        return journal;
    }
}
