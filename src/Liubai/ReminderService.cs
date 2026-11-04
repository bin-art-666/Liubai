using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppLifecycle;

namespace Liubai;

internal sealed class WindowsReminderNotifications : IReminderNotifications, IDisposable
{
    readonly string group;
    AppNotificationManager? manager;
    readonly Action<string> invoked;
    string? fingerprint;
    bool registered;
    internal string? Warning { get; private set; }
    internal WindowsReminderNotifications(Action<string> invoked, bool verification = false)
    { this.invoked = invoked; group = verification ? "Liubai.Test" : "Liubai.Reminders"; }
    internal void Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) { Warning = "此环境暂不支持 Windows 通知。任务会继续保存。"; return; }
            manager = AppNotificationManager.Default;
            manager.NotificationInvoked += Invoked;
            manager.Register("留白", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Liubai.ico")));
            registered = true; CheckSetting();
            // A COM launch stores its first activation until AppLifecycle reads
            // it. Warm-process clicks arrive through NotificationInvoked instead.
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs args)
                invoked(args.Argument);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { Warning = "Windows 通知初始化失败，提醒尚未发送。"; Diagnostic = ex.ToString(); }
    }
    internal string? Diagnostic { get; private set; }
    void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => invoked(args.Argument);
    bool CheckSetting()
    {
        if (!registered || manager == null) return false;
        bool enabled = manager.Setting == AppNotificationSetting.Enabled;
        Warning = enabled ? null : "Windows 已关闭留白通知，请在系统通知设置中开启；尚未发送的提醒会保留。";
        return enabled;
    }
    public async Task Reconcile(IReadOnlyList<TodoItem> tasks)
    {
        if (!registered || manager == null) return;
        CheckSetting();
        string key = ReminderPolicy.ScheduleKey(tasks);
        if (fingerprint == key) return;
        var current = tasks.Where(t => !t.Completed && t.RemindBefore != null).ToDictionary(t => t.Id);
        foreach (var notification in await manager.GetAllAsync())
        {
            if (notification.Group != group) continue;
            string? argument = null;
            try { argument = XDocument.Parse(notification.Payload).Root?.Attribute("launch")?.Value; }
            catch (System.Xml.XmlException) { /* Remove an unreadable stale notification below. */ }
            if (argument == null || !ReminderPolicy.TryParse(argument, out var action)
                || !current.TryGetValue(action.Id, out var item) || item.ReminderToken != action.Token)
                await manager.RemoveByIdAsync(notification.Id);
        }
        fingerprint = key;
    }
    public Task<bool> Show(TodoItem item, DateTimeOffset now)
    {
        if (!CheckSetting() || manager == null) return Task.FromResult(false);
        var notification = new AppNotification(ReminderPolicy.Payload(item, now, TimeZoneInfo.Local))
        { Tag = ReminderPolicy.Tag(item.Id), Group = group, Expiration = now.AddDays(3) };
        manager.Show(notification);
        bool accepted = notification.Id != 0;
        Warning = accepted ? null : "Windows 未接受此次提醒，请检查通知设置。";
        return Task.FromResult(accepted);
    }
    internal async Task ClearVerification()
    { if (registered && manager != null && group == "Liubai.Test") await manager.RemoveByGroupAsync(group); }
    public void Dispose()
    {
        if (manager == null) return;
        manager.NotificationInvoked -= Invoked;
        if (registered)
        {
            try { manager.Unregister(); } catch (COMException) { /* Process teardown also releases its COM server. */ }
            registered = false;
        }
    }
}

internal sealed class ReminderService
{
    readonly PlannerStore store;
    readonly DispatcherQueue dispatcher;
    readonly DispatcherQueueTimer timer;
    readonly WindowsReminderNotifications notifications;
    readonly ReminderCoordinator coordinator;
    readonly Func<Guid, bool> tryOpen;
    bool busy, pending, stopped, suspended;
    Guid? pendingOpen;
    string? lastWarning, error, lastSchedule;
    internal string? Warning => error ?? notifications.Warning;
    internal string? Diagnostic => notifications.Diagnostic;
    internal event Action? StatusChanged;
    internal ReminderService(PlannerStore store, DispatcherQueue dispatcher, Func<Guid, bool> tryOpen, bool verification = false)
    {
        this.store = store; this.dispatcher = dispatcher; this.tryOpen = tryOpen;
        notifications = new WindowsReminderNotifications(argument => dispatcher.TryEnqueue(() => _ = Handle(argument)), verification);
        coordinator = new ReminderCoordinator(store, notifications, id => pendingOpen = id, () => !stopped && !suspended);
        timer = dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += (_, _) => Wake(); store.Changed += StoreChanged;
        lastSchedule = ReminderPolicy.ScheduleKey(store.Data.Tasks);
        notifications.Register(); timer.Start(); Wake();
    }
    void StoreChanged() => dispatcher.TryEnqueue(() =>
    {
        string key = ReminderPolicy.ScheduleKey(store.Data.Tasks);
        if (lastSchedule == key) return;
        lastSchedule = key; Wake();
    });
    internal void SetSuspended(bool value)
    {
        suspended = value;
        if (value) timer.Stop(); else { timer.Start(); Wake(); }
    }
    internal void Wake()
    {
        if (stopped || suspended) return;
        if (busy) { pending = true; return; }
        _ = Tick();
    }
    async Task Tick()
    {
        busy = true;
        try
        {
            if (pendingOpen is { } id && tryOpen(id)) pendingOpen = null;
            await coordinator.Tick(DateTimeOffset.Now); error = null;
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { error = "提醒暂未发送成功，将在留白运行时重试。"; }
        finally
        {
            busy = false; PublishStatus();
            // Coalesce changes from our own persisted delivery stamps too.
            if (pending) { pending = false; if (!stopped && !suspended) dispatcher.TryEnqueue(Wake); }
        }
    }
    async Task Handle(string argument)
    {
        if (stopped || !ReminderPolicy.TryParse(argument, out var action)) return;
        try { await coordinator.Handle(action, DateTimeOffset.Now); error = null; Wake(); }
        catch (Exception ex) when (ex is COMException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { error = "通知操作未保存，请打开待办重试。"; }
        PublishStatus();
    }
    void PublishStatus()
    { if (lastWarning != Warning) { lastWarning = Warning; StatusChanged?.Invoke(); } }
    internal async Task Stop()
    {
        if (stopped) return;
        stopped = true; timer.Stop(); store.Changed -= StoreChanged;
        await coordinator.Flush(); notifications.Dispose();
    }
    internal async Task ClearVerification() => await notifications.ClearVerification();
}
