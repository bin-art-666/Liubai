using Microsoft.UI.Dispatching;

namespace Liubai;

internal sealed class ClockService
{
    readonly ClockStore store;
    readonly IClockNotifications notifications;
    readonly ClockCoordinator coordinator;
    readonly DispatcherQueue dispatcher;
    readonly DispatcherQueueTimer timer;
    readonly Func<Guid, bool> tryOpen;
    bool busy, pending, stopped, suspended;
    Guid? pendingOpen;
    DateTimeOffset retryAfter;
    string? error;
    internal string? Warning => store.LoadWarning ?? error ?? notifications.Warning;
    internal event Action? Pulse;
    internal ClockService(ClockStore store, IClockNotifications notifications, DispatcherQueue dispatcher, Func<Guid, bool> tryOpen, bool initiallySuspended = false)
    {
        this.store = store; this.notifications = notifications; this.dispatcher = dispatcher; this.tryOpen = tryOpen;
        suspended = initiallySuspended;
        coordinator = new ClockCoordinator(store, notifications, id => pendingOpen = id, () => !stopped && !suspended);
        timer = dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += (_, _) => { Pulse?.Invoke(); Wake(); };
        store.Changed += Changed; if (!suspended) timer.Start(); Wake();
    }
    void Changed() => dispatcher.TryEnqueue(() => { retryAfter = default; Pulse?.Invoke(); Wake(); });
    internal void SetSuspended(bool value)
    { suspended = value; if (value) timer.Stop(); else { timer.Start(); retryAfter = default; Wake(); Pulse?.Invoke(); } }
    internal void Wake(bool force = false)
    {
        if (force) retryAfter = default;
        if (stopped || suspended || DateTimeOffset.Now < retryAfter) return;
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
            if (notifications.Warning != null) retryAfter = DateTimeOffset.Now.AddSeconds(5);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { error = "时钟提醒暂未完成，将自动重试。"; retryAfter = DateTimeOffset.Now.AddSeconds(5); }
        finally
        {
            busy = false; Pulse?.Invoke();
            if (!stopped && !suspended)
            {
                if (pendingOpen != null || ClockPolicy.Entries(store.Data).Any(e => e.State == ClockState.Running)) timer.Start(); else timer.Stop();
            }
            if (pending) { pending = false; if (!stopped && !suspended) dispatcher.TryEnqueue(() => Wake()); }
        }
    }
    internal async Task Handle(string argument)
    {
        if (stopped || !ClockPolicy.TryParse(argument, out var action)) return;
        try { await coordinator.Handle(action, DateTimeOffset.Now); error = null; retryAfter = default; Wake(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { error = "提醒操作未保存，请打开时钟重试。"; }
        Pulse?.Invoke();
    }
    internal async Task Stop()
    {
        if (stopped) return;
        stopped = true; timer.Stop(); store.Changed -= Changed;
        await coordinator.Flush(); await store.Flush();
        try { await notifications.ClearClock(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
}
