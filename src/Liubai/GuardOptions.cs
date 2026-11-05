using System.Text.Json;

namespace Liubai;

internal enum GuardAlertMode { Prompt, Blackout }
internal sealed record GuardOptions(int Version = 1, string CameraId = "", GuardAlertMode Mode = GuardAlertMode.Prompt,
    string Message = "检测到其他人，请留意周围。", int HoldMilliseconds = 1500);
internal sealed class GuardOptionsStore
{
    readonly string path;
    GuardOptions data = new();
    bool preserveBackup;
    internal GuardOptions Data => Volatile.Read(ref data);
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    internal event Action? Changed;
    internal GuardOptionsStore(string path)
    {
        this.path = path;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { data = Read(path); }
        catch (NotSupportedException) { ReadOnly = true; Warning = "守护设置来自更新版本，已停止写入。"; }
        catch (Exception ex) when (ReadError(ex))
        {
            try { data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复守护设置。"; }
            catch (Exception backup) when (ReadError(backup) || backup is NotSupportedException)
            { ReadOnly = true; Warning = "守护设置无法读取，已停止写入。"; }
        }
    }
    static bool ReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    static GuardOptions Read(string path)
    {
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize<GuardOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        if (value.Version != 1) throw new NotSupportedException();
        Validate(value); return value;
    }
    internal static void Validate(GuardOptions value)
    {
        if (value.Version != 1 || value.CameraId is null || value.CameraId.Length > 8192 || value.CameraId.Any(char.IsControl)
            || !Enum.IsDefined(value.Mode) || string.IsNullOrWhiteSpace(value.Message) || value.Message.Length > 200
            || value.Message.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
            || value.HoldMilliseconds is < 500 or > 5000) throw new InvalidDataException("守护设置无效：提示限 200 字，触发时间为 0.5～5 秒。");
    }
    internal void Save(GuardOptions value)
    {
        Validate(value);
        if (ReadOnly) throw new IOException(Warning);
        if (value == data) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, value); file.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
            else File.Move(path + ".tmp", path);
            Volatile.Write(ref data, value); preserveBackup = false; Changed?.Invoke();
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
    }
}

internal sealed class GuardTrigger
{
    readonly object gate = new();
    long? candidate, previous;
    long cooldownUntil;
    bool active;
    internal bool Active { get { lock (gate) return active; } }
    internal bool Cooling(long now) { lock (gate) return now < cooldownUntil; }
    internal bool Observe(int faces, long now, int hold)
    {
        lock (gate)
        {
            if (active || now < cooldownUntil) { candidate = previous = null; return false; }
            if (previous is { } last && (now < last || now - last > 1250)) candidate = null;
            previous = now;
            if (faces < 2) { candidate = null; return false; }
            candidate ??= now;
            if (now - candidate.Value < hold) return false;
            active = true; candidate = null; return true;
        }
    }
    internal void Dismiss(long now) { lock (gate) { active = false; candidate = previous = null; cooldownUntil = now + 30000; } }
    internal void Reset() { lock (gate) { active = false; candidate = previous = null; cooldownUntil = 0; } }
    internal void RestartCandidate() { lock (gate) { candidate = previous = null; } }
}

internal sealed class GuardPause
{
    internal long Until { get; private set; }
    internal bool Pending { get; private set; }
    internal bool Active(long now) => Pending && now < Until;
    internal void Begin(long now, long milliseconds)
    {
        if (milliseconds is < 1 or > 86400000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Until = checked(now + milliseconds); Pending = true;
    }
    internal void Clear() { Until = 0; Pending = false; }
}
