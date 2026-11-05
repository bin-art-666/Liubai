using System.Globalization;
using System.Xml.Linq;

namespace Liubai;

internal enum ClockKind { Alarm, Countdown }
internal enum ClockState { Running, Paused, Finished, Stopped }
internal sealed record ClockEntry(Guid Id, Guid Token, ClockKind Kind, ClockState State, string Title,
    int DurationSeconds, long RemainingTicks, DateTimeOffset? EndsAt, DateTime? LocalAlarm,
    DateTimeOffset? SnoozeUntil, DateTimeOffset? NotifiedAt);
internal sealed record ClockDocument(int Version, ClockEntry[] Alarms, ClockEntry? Countdown, bool ShowRemainingInBar = false);
internal enum ClockActionKind { Open, Stop, Snooze, Repeat }
internal readonly record struct ClockAction(Guid Id, Guid Token, ClockActionKind Kind);

internal static class ClockPolicy
{
    internal const int MaximumSeconds = 99 * 3600 + 59 * 60 + 59;
    internal static bool PreferCountdown(ClockDocument data, Guid? requestedId = null) => requestedId is { } id
        ? data.Countdown?.Id == id : data.Countdown?.State is ClockState.Running or ClockState.Paused;
    internal static string ToolbarText(ClockDocument data, DateTimeOffset now, TimeZoneInfo zone) => data.ShowRemainingInBar
        && data.Countdown is { State: ClockState.Running or ClockState.Paused } item
            ? item.State == ClockState.Paused ? "Ⅱ" : Format(Remaining(item, now, zone)) : "";
    internal static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Map a missing spring DST time to the first valid minute. Standard time
        // consistently resolves the repeated autumn hour.
        for (int i = 0; zone.IsInvalidTime(local) && i < 180; i++) local = local.AddMinutes(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
    internal static DateTime NextAlarm(TimeOnly time, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).Date + time.ToTimeSpan();
        return Resolve(local, zone) <= now ? local.AddDays(1) : local;
    }
    internal static DateTimeOffset? Due(ClockEntry item, TimeZoneInfo zone) => item.SnoozeUntil
        ?? (item.Kind == ClockKind.Alarm && item.LocalAlarm is { } local ? Resolve(local, zone) : item.EndsAt);
    internal static bool IsDue(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone) => item.State == ClockState.Running
        && item.NotifiedAt is null && Due(item, zone) is { } due && due <= now;
    internal static TimeSpan Remaining(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone) => item.State == ClockState.Paused
        ? TimeSpan.FromTicks(item.RemainingTicks) : item.State == ClockState.Running && Due(item, zone) is { } due && due > now ? due - now : TimeSpan.Zero;
    internal static string Format(TimeSpan remaining)
    {
        long seconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        return seconds >= 3600 ? $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    }
    internal static string When(DateTime local, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        string day = local.Date == today ? "今天" : local.Date == today.AddDays(1) ? "明天" : local.ToString("M月d日", CultureInfo.InvariantCulture);
        return $"{day} {local:HH:mm}";
    }
    internal static IEnumerable<ClockEntry> Entries(ClockDocument data) => data.Countdown is { } timer ? data.Alarms.Append(timer) : data.Alarms;
    internal static string Arguments(ClockEntry item, ClockActionKind kind) => $"clock|{kind.ToString().ToLowerInvariant()}|{item.Id:N}|{item.Token:N}";
    internal static bool TryParse(string argument, out ClockAction action)
    {
        action = default;
        if (argument.Length > 150) return false;
        var parts = argument.Split('|');
        if (parts.Length != 4 || parts[0] != "clock" || !Guid.TryParseExact(parts[2], "N", out var id)
            || !Guid.TryParseExact(parts[3], "N", out var token) || id == Guid.Empty || token == Guid.Empty) return false;
        var kind = parts[1] switch { "open" => ClockActionKind.Open, "stop" => ClockActionKind.Stop,
            "snooze" => ClockActionKind.Snooze, "repeat" => ClockActionKind.Repeat, _ => (ClockActionKind)(-1) };
        if (!Enum.IsDefined(kind)) return false;
        action = new(id, token, kind); return true;
    }
    internal static string Payload(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone, bool silent = false)
    {
        bool missed = Due(item, zone) < now.AddMinutes(-1);
        string heading = (missed ? "错过的提醒 · " : "") + (item.Kind == ClockKind.Alarm ? "闹钟" : "倒计时结束");
        XElement Button(string name, ClockActionKind kind) => new("action", new XAttribute("content", name),
            new XAttribute("arguments", Arguments(item, kind)), new XAttribute("activationType", "background"));
        var toast = new XElement("toast", new XAttribute("launch", Arguments(item, ClockActionKind.Open)), new XAttribute("scenario", "alarm"),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", item.Title.Length == 0 ? heading : item.Title), new XElement("text", heading))),
            new XElement("actions", Button("停止", ClockActionKind.Stop), item.Kind == ClockKind.Alarm
                ? Button("稍后 5 分钟", ClockActionKind.Snooze) : Button("再计一次", ClockActionKind.Repeat)));
        if (silent) toast.Add(new XElement("audio", new XAttribute("silent", true)));
        return toast.ToString(SaveOptions.DisableFormatting);
    }
}

internal interface IClockNotifications
{
    string? Warning { get; }
    Task ReconcileClock(IReadOnlyList<ClockEntry> entries);
    Task<bool> ShowClock(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone);
    Task ClearClock();
}

internal sealed class ClockCoordinator(ClockStore store, IClockNotifications notifications, Action<Guid> open, Func<bool>? canNotify = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Dictionary<Guid, Guid> accepted = new();
    internal async Task Tick(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        await gate.WaitAsync();
        try
        {
            zone ??= TimeZoneInfo.Local;
            await notifications.ReconcileClock(ClockPolicy.Entries(store.Data).ToArray());
            foreach (var id in accepted.Keys.ToArray())
                if (!ClockPolicy.Entries(store.Data).Any(e => e.Id == id && e.Token == accepted[id])) accepted.Remove(id);
            foreach (var item in ClockPolicy.Entries(store.Data).Where(e => ClockPolicy.IsDue(e, now, zone)).OrderBy(e => ClockPolicy.Due(e, zone)).Take(3).ToArray())
            {
                if (canNotify?.Invoke() == false || store.ReadOnly) break;
                var current = ClockPolicy.Entries(store.Data).FirstOrDefault(e => e.Id == item.Id);
                if (current is null || current.Token != item.Token || !ClockPolicy.IsDue(current, now, zone)) continue;
                if (!accepted.TryGetValue(item.Id, out var token) || token != item.Token)
                {
                    if (!await notifications.ShowClock(current, now, zone)) continue;
                    accepted[item.Id] = item.Token;
                }
                await store.MarkNotified(item.Id, item.Token, now);
                accepted.Remove(item.Id);
            }
        }
        finally { gate.Release(); }
    }
    internal async Task<bool> Handle(ClockAction action, DateTimeOffset now)
    {
        await gate.WaitAsync();
        try
        {
            if (!await store.ApplyAction(action, now)) return false;
            if (action.Kind == ClockActionKind.Open) open(action.Id);
            await notifications.ReconcileClock(ClockPolicy.Entries(store.Data).ToArray()); return true;
        }
        finally { gate.Release(); }
    }
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
