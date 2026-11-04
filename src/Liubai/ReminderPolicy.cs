using System.Xml.Linq;

namespace Liubai;

internal enum ReminderActionKind { Open, Complete, Snooze }
internal readonly record struct ReminderAction(Guid Id, Guid Token, ReminderActionKind Kind);

internal static class ReminderPolicy
{
    internal static readonly int?[] Leads = [null, 0, 5, 10, 15, 30, 60, 1440];
    internal static bool ValidSchedule(DateOnly? date, TimeOnly? time, int? lead) =>
        (time == null || date != null) && (lead == null || date != null && time != null && Leads.Contains(lead));
    internal static DateTimeOffset? Due(TodoItem item, TimeZoneInfo zone)
    {
        if (item.Completed || item.RemindBefore == null || item.Date == null || item.Time == null) return null;
        if (item.SnoozeUntil is { } snooze) return snooze;
        var local = item.Date.Value.ToDateTime(item.Time.Value, DateTimeKind.Unspecified);
        // A missing clock time during the spring DST jump maps to the first valid
        // local minute; ambiguous autumn times consistently use the standard offset.
        for (int i = 0; zone.IsInvalidTime(local) && i < 180; i++) local = local.AddMinutes(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).AddMinutes(-item.RemindBefore.Value);
    }
    internal static bool IsDue(TodoItem item, DateTimeOffset now, TimeZoneInfo zone) =>
        item.NotifiedAt == null && Due(item, zone) is { } due && due <= now;
    internal static string Tag(Guid id) => id.ToString("N")[..16];
    internal static string ScheduleKey(IEnumerable<TodoItem> tasks) => string.Join(';', tasks.Where(t => t.RemindBefore != null)
        .OrderBy(t => t.Id).Select(t => $"{t.Id:N}:{t.ReminderToken:N}:{t.Completed}"));
    internal static string Arguments(TodoItem item, ReminderActionKind action) =>
        $"action={action.ToString().ToLowerInvariant()}&id={item.Id:N}&token={item.ReminderToken:N}";
    internal static bool TryParse(string argument, out ReminderAction action)
    {
        action = default;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (argument.Length > 512) return false;
        foreach (string entry in argument.Split('&'))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length != 2 || !values.TryAdd(pair[0], pair[1])) return false;
        }
        if (values.Count != 3 || !values.TryGetValue("id", out var idText) || !Guid.TryParseExact(idText, "N", out var id)
            || !values.TryGetValue("token", out var tokenText) || !Guid.TryParseExact(tokenText, "N", out var token)
            || !values.TryGetValue("action", out var kindText) || id == Guid.Empty || token == Guid.Empty) return false;
        var kind = kindText switch { "open" => ReminderActionKind.Open, "complete" => ReminderActionKind.Complete, "snooze" => ReminderActionKind.Snooze, _ => (ReminderActionKind)(-1) };
        if (!Enum.IsDefined(kind)) return false;
        action = new(id, token, kind); return true;
    }
    internal static string Payload(TodoItem item, DateTimeOffset now, TimeZoneInfo zone)
    {
        string when = $"{item.Date:yyyy年M月d日} {item.Time:HH:mm}";
        string heading = Due(item, zone) < now.AddMinutes(-1) ? "错过的提醒 · " : "待办提醒 · ";
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", item.Title), new XElement("text", heading + when));
        if (!string.IsNullOrWhiteSpace(item.Detail)) binding.Add(new XElement("text", item.Detail.Length > 120 ? item.Detail[..120] + "…" : item.Detail));
        XElement Button(string name, ReminderActionKind kind) => new("action", new XAttribute("content", name),
            new XAttribute("arguments", Arguments(item, kind)), new XAttribute("activationType", "background"));
        return new XElement("toast", new XAttribute("launch", Arguments(item, ReminderActionKind.Open)),
            new XAttribute("duration", "long"), new XElement("visual", binding),
            new XElement("actions", Button("完成", ReminderActionKind.Complete), Button("10 分钟后提醒", ReminderActionKind.Snooze), Button("打开任务", ReminderActionKind.Open)))
            .ToString(SaveOptions.DisableFormatting);
    }
}

internal interface IReminderNotifications
{
    Task Reconcile(IReadOnlyList<TodoItem> tasks);
    Task<bool> Show(TodoItem item, DateTimeOffset now);
}

internal sealed class ReminderCoordinator(PlannerStore store, IReminderNotifications notifications, Action<Guid> open, Func<bool>? canNotify = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Dictionary<Guid, Guid> accepted = new();
    internal async Task Tick(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        await gate.WaitAsync();
        try
        {
            zone ??= TimeZoneInfo.Local;
            await notifications.Reconcile(store.Data.Tasks);
            foreach (var id in accepted.Keys.ToArray())
                if (!store.Data.Tasks.Any(t => t.Id == id && t.ReminderToken == accepted[id] && !t.Completed)) accepted.Remove(id);
            foreach (var item in store.Data.Tasks.Where(t => ReminderPolicy.IsDue(t, now, zone)).OrderBy(t => ReminderPolicy.Due(t, zone)).Take(3).ToArray())
            {
                if (canNotify?.Invoke() == false) break;
                // Recheck after awaited notification cleanup, before publishing.
                var current = store.Data.Tasks.FirstOrDefault(t => t.Id == item.Id);
                if (current == null || current.ReminderToken != item.ReminderToken || !ReminderPolicy.IsDue(current, now, zone)) continue;
                // If delivery succeeded but disk persistence failed, retry the
                // stamp without repeatedly raising the same notification.
                if (!accepted.TryGetValue(current.Id, out var token) || token != current.ReminderToken)
                {
                    if (!await notifications.Show(current, now)) continue;
                    accepted[current.Id] = current.ReminderToken;
                }
                await store.MarkNotified(current.Id, current.ReminderToken, now);
                accepted.Remove(current.Id);
            }
        }
        finally { gate.Release(); }
    }
    internal async Task<bool> Handle(ReminderAction action, DateTimeOffset now)
    {
        await gate.WaitAsync();
        try
        {
            if (!await store.ApplyReminderAction(action, now)) return false;
            if (action.Kind == ReminderActionKind.Open) open(action.Id);
            await notifications.Reconcile(store.Data.Tasks); return true;
        }
        finally { gate.Release(); }
    }
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
