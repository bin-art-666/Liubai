using System.Xml.Linq;
using Microsoft.Windows.AppNotifications;

namespace Liubai;

internal sealed partial class WindowsReminderNotifications
{
    string ClockGroup => group == "Liubai.Test" ? "Liubai.Clock.Test" : "Liubai.Clock";
    string? clockFingerprint;
    string? IClockNotifications.Warning => Warning;
    public async Task ReconcileClock(IReadOnlyList<ClockEntry> entries)
    {
        if (!registered || manager is null) return;
        CheckSetting();
        string fingerprint = string.Join(';', entries.OrderBy(e => e.Id).Select(e => $"{e.Id:N}:{e.Token:N}:{e.State}"));
        if (fingerprint == clockFingerprint) return;
        var valid = entries.Where(e => e.State is ClockState.Running or ClockState.Finished).ToDictionary(e => e.Id);
        foreach (var notification in await manager.GetAllAsync())
        {
            if (notification.Group != ClockGroup) continue;
            string? argument = null;
            try { argument = XDocument.Parse(notification.Payload).Root?.Attribute("launch")?.Value; } catch (System.Xml.XmlException) { }
            if (argument is null || !ClockPolicy.TryParse(argument, out var action)
                || !valid.TryGetValue(action.Id, out var item) || item.Token != action.Token) await manager.RemoveByIdAsync(notification.Id);
        }
        clockFingerprint = fingerprint;
    }
    public Task<bool> ShowClock(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!CheckSetting() || manager is null) return Task.FromResult(false);
        var notification = new AppNotification(ClockPolicy.Payload(item, now, zone, group == "Liubai.Test"))
        { Tag = ReminderPolicy.Tag(item.Id), Group = ClockGroup, Expiration = now.AddDays(1) };
        manager.Show(notification);
        bool accepted = notification.Id != 0;
        Warning = accepted ? null : "Windows 未接受此次提醒，请检查通知设置。";
        return Task.FromResult(accepted);
    }
    public async Task ClearClock()
    {
        if (registered && manager is not null) await manager.RemoveByGroupAsync(ClockGroup);
        clockFingerprint = null;
    }
}
