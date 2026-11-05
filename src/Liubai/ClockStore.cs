using System.Text.Json;

namespace Liubai;

internal sealed class ClockStore
{
    internal const int AlarmCapacity = 20;
    readonly string path;
    readonly SemaphoreSlim gate = new(1, 1);
    ClockDocument data = new(1, [], null);
    bool preserveBackup;
    internal ClockDocument Data => data with { Alarms = data.Alarms.ToArray() };
    internal bool ReadOnly { get; }
    internal string? LoadWarning { get; }
    internal bool HasPending => ClockPolicy.Entries(data).Any(e => e.State is ClockState.Running or ClockState.Paused);
    internal event Action? Changed;
    internal ClockStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(this.path) && !File.Exists(this.path + ".bak")) return;
        try { data = Read(this.path); }
        catch (NotSupportedException) { ReadOnly = true; LoadWarning = "时钟数据来自更新版本，已停止写入。"; }
        catch (Exception ex) when (ReadError(ex))
        {
            try { data = Read(this.path + ".bak"); preserveBackup = true; LoadWarning = "已从备份恢复时钟数据。"; }
            catch (Exception backup) when (ReadError(backup) || backup is NotSupportedException)
            { ReadOnly = true; LoadWarning = "时钟数据无法读取，已停止写入以保护原记录。"; }
        }
    }
    static bool ReadError(Exception ex) => ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException;
    static ClockDocument Read(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("时钟数据过大。");
        var document = JsonSerializer.Deserialize<ClockDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        if (document.Version != 1) throw new NotSupportedException();
        Validate(document); return document;
    }
    static void Validate(ClockDocument document)
    {
        if (document.Alarms is null || document.Alarms.Length > AlarmCapacity || document.Alarms.Any(e => e is null || e.Kind != ClockKind.Alarm)
            || document.Countdown is { Kind: not ClockKind.Countdown }) throw new InvalidDataException("时钟数据无效。");
        var entries = ClockPolicy.Entries(document).ToArray();
        if (entries.Select(e => e.Id).Distinct().Count() != entries.Length) throw new InvalidDataException("时钟编号重复。");
        foreach (var e in entries)
        {
            if (e.Id == Guid.Empty || e.Token == Guid.Empty || !Enum.IsDefined(e.State) || e.Title is null || e.Title.Length > 120
                || e.Title.Any(char.IsControl)) throw new InvalidDataException("时钟内容无效。");
            if (e.State == ClockState.Running && e.NotifiedAt != null || e.State == ClockState.Finished && e.NotifiedAt == null
                || e.Kind == ClockKind.Countdown && e.SnoozeUntil != null) throw new InvalidDataException("时钟提醒状态无效。");
            if (e.Kind == ClockKind.Alarm && (e.LocalAlarm is null || e.DurationSeconds != 0 || e.State == ClockState.Paused)
                || e.Kind == ClockKind.Countdown && (e.DurationSeconds is < 1 or > ClockPolicy.MaximumSeconds || e.LocalAlarm != null
                    || e.RemainingTicks < 0 || e.RemainingTicks > (long)e.DurationSeconds * TimeSpan.TicksPerSecond
                    || e.State == ClockState.Running && e.EndsAt == null || e.State == ClockState.Paused && e.RemainingTicks <= 0))
                throw new InvalidDataException("时钟时间无效。");
        }
    }
    static string Title(string title)
    {
        title = title.Trim();
        if (title.Length > 120 || title.Any(char.IsControl)) throw new InvalidDataException("备注最多 120 字，不能包含换行。");
        return title;
    }
    async Task Change(Func<ClockDocument, ClockDocument> change)
    {
        await gate.WaitAsync(); bool changed = false;
        try
        {
            if (ReadOnly) throw new InvalidDataException(LoadWarning);
            var next = change(data); if (ReferenceEquals(next, data)) return;
            Validate(next);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                    { JsonSerializer.Serialize(stream, next); stream.Flush(true); }
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
                    else File.Move(path + ".tmp", path);
                }
                finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            });
            data = next; preserveBackup = false; changed = true;
        }
        finally { gate.Release(); }
        if (changed) Changed?.Invoke();
    }
    static ClockEntry Alarm(Guid id, TimeOnly time, string title, DateTimeOffset now, TimeZoneInfo zone, bool enabled)
    {
        var local = ClockPolicy.NextAlarm(time, now, zone);
        return new(id, Guid.NewGuid(), ClockKind.Alarm, enabled ? ClockState.Running : ClockState.Stopped,
            Title(title), 0, 0, ClockPolicy.Resolve(local, zone), local, null, null);
    }
    internal Task AddAlarm(TimeOnly time, string title, DateTimeOffset now, TimeZoneInfo? zone = null) => Change(d =>
    {
        if (d.Alarms.Length >= AlarmCapacity) throw new InvalidDataException("最多保存 20 个闹钟，请先整理已有闹钟。");
        return d with { Alarms = [.. d.Alarms, Alarm(Guid.NewGuid(), time, title, now, zone ?? TimeZoneInfo.Local, true)] };
    });
    internal Task EditAlarm(Guid id, TimeOnly time, string title, bool enabled, DateTimeOffset now, TimeZoneInfo? zone = null) => Change(d =>
        d.Alarms.Any(e => e.Id == id) ? d with { Alarms = d.Alarms.Select(e => e.Id == id ? Alarm(id, time, title, now, zone ?? TimeZoneInfo.Local, enabled) : e).ToArray() } : d);
    internal Task SetAlarmEnabled(Guid id, bool enabled, DateTimeOffset now) => Change(d =>
        d.Alarms.Any(e => e.Id == id) ? d with { Alarms = d.Alarms.Select(e => e.Id != id ? e : enabled
            ? Alarm(id, TimeOnly.FromDateTime(e.LocalAlarm!.Value), e.Title, now, TimeZoneInfo.Local, true)
            : e with { State = ClockState.Stopped, Token = Guid.NewGuid(), SnoozeUntil = null }).ToArray() } : d);
    internal Task RemoveAlarm(Guid id) => Change(d => d.Alarms.Any(e => e.Id == id) ? d with { Alarms = d.Alarms.Where(e => e.Id != id).ToArray() } : d);
    static ClockEntry Countdown(int seconds, string title, DateTimeOffset now)
    {
        if (seconds is < 1 or > ClockPolicy.MaximumSeconds) throw new InvalidDataException("计时范围为 1 秒至 99 小时 59 分 59 秒。");
        return new(Guid.NewGuid(), Guid.NewGuid(), ClockKind.Countdown, ClockState.Running, Title(title), seconds, 0, now.AddSeconds(seconds), null, null, null);
    }
    internal Task StartCountdown(int seconds, string title, DateTimeOffset now) => Change(d =>
    {
        if (d.Countdown?.State is ClockState.Running or ClockState.Paused) throw new InvalidDataException("已有倒计时，请先取消或完成它。");
        return d with { Countdown = Countdown(seconds, title, now) };
    });
    internal Task PauseCountdown(DateTimeOffset now) => Change(d =>
    {
        if (d.Countdown is not { State: ClockState.Running } item) return d;
        long remaining = Math.Min((long)item.DurationSeconds * TimeSpan.TicksPerSecond, Math.Max(0, (item.EndsAt!.Value - now).Ticks));
        if (remaining == 0) throw new InvalidDataException("计时已结束，正在等待提醒。");
        return d with { Countdown = item with { State = ClockState.Paused, RemainingTicks = remaining, EndsAt = null, Token = Guid.NewGuid() } };
    });
    internal Task ResumeCountdown(DateTimeOffset now) => Change(d => d.Countdown is { State: ClockState.Paused } item
        ? d with { Countdown = item with { State = ClockState.Running, EndsAt = now.AddTicks(item.RemainingTicks), RemainingTicks = 0, Token = Guid.NewGuid() } } : d);
    internal Task CancelCountdown() => Change(d => d.Countdown is null ? d : d with { Countdown = null });
    internal Task SetShowRemainingInBar(bool enabled) => Change(d => d.ShowRemainingInBar == enabled ? d : d with { ShowRemainingInBar = enabled });
    static ClockDocument Replace(ClockDocument d, ClockEntry value) => value.Kind == ClockKind.Alarm
        ? d with { Alarms = d.Alarms.Select(e => e.Id == value.Id ? value : e).ToArray() } : d with { Countdown = value };
    internal Task MarkNotified(Guid id, Guid token, DateTimeOffset now) => Change(d =>
        ClockPolicy.Entries(d).FirstOrDefault(e => e.Id == id && e.Token == token && e.State == ClockState.Running) is { } item
            ? Replace(d, item with { State = ClockState.Finished, NotifiedAt = now }) : d);
    internal async Task<bool> ApplyAction(ClockAction action, DateTimeOffset now)
    {
        bool applied = false;
        await Change(d =>
        {
            var item = ClockPolicy.Entries(d).FirstOrDefault(e => e.Id == action.Id && e.Token == action.Token);
            if (item is null || item.State is ClockState.Stopped or ClockState.Paused) return d;
            if (action.Kind == ClockActionKind.Open) { applied = true; return d; }
            if (item.NotifiedAt is null && !ClockPolicy.IsDue(item, now, TimeZoneInfo.Local)) return d;
            ClockEntry? updated = action.Kind switch
            {
                ClockActionKind.Stop => item with { State = item.Kind == ClockKind.Alarm ? ClockState.Stopped : ClockState.Finished, Token = Guid.NewGuid(), NotifiedAt = now },
                ClockActionKind.Snooze when item.Kind == ClockKind.Alarm => item with { State = ClockState.Running, SnoozeUntil = now.AddMinutes(5), NotifiedAt = null, Token = Guid.NewGuid() },
                ClockActionKind.Repeat when item.Kind == ClockKind.Countdown => item with { State = ClockState.Running, EndsAt = now.AddSeconds(item.DurationSeconds), NotifiedAt = null, Token = Guid.NewGuid(), RemainingTicks = 0 },
                _ => null
            };
            if (updated is null) return d;
            applied = true; return Replace(d, updated);
        });
        return applied;
    }
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
