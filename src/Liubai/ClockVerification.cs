using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    static IEnumerable<T> ClockControls<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in ClockControls<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    async Task VerifyClock()
    {
        string result = "PASS", error = "", notificationWarning = ""; bool nativeNotificationAccepted = false;
        var checks = new List<string>();
        try
        {
            await Task.Delay(600);
            string[] expected = ["截图", "扫码", "时钟", "待办", "随手记", "收纳盒", "屏幕守护", "一键隐私", "AI 快捷窗", "设置"];
            if (!actions.Children.OfType<Button>().Where(b => !ReferenceEquals(b, moreButton)).Select(AutomationProperties.GetName).SequenceEqual(expected)) throw new Exception("Toolbar order mismatch");
            if (!OpenClock()) throw new Exception("Clock entry did not open");
            checks.Add("Clock toolbar before todo; panel opened");
            var store = App.Clock;
            if (store.Data.ShowRemainingInBar || clockRemaining?.Visibility != Visibility.Collapsed || clockButton.Width != 34) throw new Exception("Clock label not off by default");
            var now = DateTimeOffset.Now;
            var next = TimeOnly.FromDateTime(now.LocalDateTime.AddMinutes(3));
            await store.AddAlarm(next, "闹钟测试", now);
            await store.StartCountdown(65, "专注测试", now);
            clockWindow!.SelectPage(false); clockWindow.Hide(); OpenClock(); await Task.Delay(200);
            if (!clockWindow.TimerSelected || clockDot?.Visibility != Visibility.Visible) throw new Exception("Timer tab or running badge missing");
            await store.SetShowRemainingInBar(true); await Task.Delay(100);
            if (clockRemaining?.Visibility != Visibility.Visible || !double.IsNaN(clockButton.Width) || !clockRemaining.Text.StartsWith("01:", StringComparison.Ordinal)) throw new Exception("Remaining label missing when enabled");
            if (!ClockControls<CheckBox>((DependencyObject)clockWindow.Content).Single(b => AutomationProperties.GetName(b) == "在顶栏显示剩余时间").IsChecked.GetValueOrDefault()) throw new Exception("Display preference not reflected in panel");
            double labelWidth = clockRemaining.Width;
            var timerText = ClockControls<TextBlock>((DependencyObject)clockWindow.Content).Any(t => t.FontSize == 46 && t.Text.StartsWith("01:", StringComparison.Ordinal));
            if (!timerText) throw new Exception("Countdown display missing");
            var restoredFields = ClockControls<NumberBox>((DependencyObject)clockWindow.Content).ToArray();
            if (restoredFields.Single(b => AutomationProperties.GetName(b) == "计时分").Value != 1
                || restoredFields.Single(b => AutomationProperties.GetName(b) == "计时秒").Value != 5) throw new Exception("Restored timer fields do not match duration");
            await store.PauseCountdown(DateTimeOffset.Now); await Task.Delay(100);
            clockWindow.SelectPage(false); clockWindow.Hide(); OpenClock();
            if (!clockWindow.TimerSelected || clockRemaining.Text != "Ⅱ" || clockRemaining.Width != labelWidth) throw new Exception("Paused state label or preferred page incorrect");
            OpenClock(store.Data.Alarms.Single().Id);
            if (clockWindow.TimerSelected) throw new Exception("Explicit alarm notification opened timer page");
            OpenClock();
            if (!ClockControls<Button>((DependencyObject)clockWindow.Content).Any(b => Equals(b.Content, "继续"))) throw new Exception("Pause controls not updated");
            checks.Add("Live countdown, pause/continue controls, active icon badge");
            checks.Add("Active/paused timer opens first; explicit alarm target preserved; optional label and stable pause width");
            var exitPrompt = clockWindow.ConfirmQuit(); await Task.Delay(150);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)clockWindow.Content).XamlRoot)
                .SelectMany(p => p.Child is { } child ? ClockControls<ContentDialog>(child) : []).FirstOrDefault();
            if (popup == null || !clockWindow.ModalActive) throw new Exception("Pending-clock exit confirmation missing");
            popup.Hide();
            if (await exitPrompt || !store.HasPending) throw new Exception("Canceling exit lost active timer");
            checks.Add("Exit confirmation shown; continuing preserves active clock");
            if (Environment.GetCommandLineArgs().Contains("--preview-clock")) await Task.Delay(TimeSpan.FromSeconds(60));
            await store.ResumeCountdown(DateTimeOffset.Now);
            clockWindow.Hide();
            if (!store.HasPending || new ClockStore(Path.Combine(App.DataDirectory, "clock.json")).Data.Countdown?.State != ClockState.Running) throw new Exception("Closing panel lost running timer");
            OpenClock(); OpenSettings(); await Task.Delay(200);
            if (clockWindow.IsOpen || settingsWindow?.IsOpen != true) throw new Exception("Clock/settings overlap");
            var clockSettings = ClockControls<Expander>((DependencyObject)settingsWindow.Content).Single(e => Equals(e.Header, "时钟设置"));
            clockSettings.IsExpanded = true; await Task.Delay(100);
            var fontSlider = ClockControls<Slider>((DependencyObject)settingsWindow.Content).Single(s => AutomationProperties.GetName(s) == "计时数字大小");
            fontSlider.Value = 14; await Task.Delay(100);
            if (clockRemaining.FontSize != 14 || preferences.ClockFontSize != 14) throw new Exception("Font slider did not preview on the bar");
            settingsWindow.FlushChanges(force: true);
            if (new AppearanceStore(Path.Combine(App.DataDirectory, "appearance.json")).ClockFontSize != 14) throw new Exception("Font slider did not persist");
            fontSlider.Value = AppearanceStore.DefaultClockFontSize; settingsWindow.FlushChanges(force: true); await Task.Delay(100);
            checks.Add("Clock font slider previews live and persists through settings save");
            settingsWindow.Hide(); OpenClock(); Activate(); await Task.Delay(300);
            if (clockWindow.IsOpen) throw new Exception("Clock did not dismiss outside");
            checks.Add("Panel dismissal preserves timer; settings mutual exclusion; outside dismissal");
            await store.CancelCountdown();
            await Task.Delay(100); OpenClock();
            if (clockWindow.TimerSelected || clockRemaining.Visibility != Visibility.Collapsed || clockButton.Width != 34) throw new Exception("Cancellation did not restore icon and alarm page");
            foreach (var alarm in store.Data.Alarms) await store.RemoveAlarm(alarm.Id);
            await store.StartCountdown(1, "唤醒测试", now);
            var fake = new VerificationClockNotifications(); bool suspended = true;
            var coordinator = new ClockCoordinator(store, fake, _ => { }, () => !suspended);
            await coordinator.Tick(now.AddHours(1));
            if (fake.Count != 0) throw new Exception("Suspension did not suppress delivery");
            suspended = false; await coordinator.Tick(now.AddHours(1)); await coordinator.Tick(now.AddHours(2));
            if (fake.Count != 1 || store.Data.Countdown?.State != ClockState.Finished) throw new Exception("Wake delivery duplicated or missing");
            var delivered = store.Data.Countdown!;
            if (!await coordinator.Handle(new(delivered.Id, delivered.Token, ClockActionKind.Repeat), DateTimeOffset.Now)) throw new Exception("Repeat action failed");
            if (await coordinator.Handle(new(delivered.Id, delivered.Token, ClockActionKind.Stop), DateTimeOffset.Now)) throw new Exception("Stale action changed new timer");
            checks.Add("Suspension/wake, one-time notification, repeat and stale-action protection");
            await store.CancelCountdown();
            var reminders = App.StartReminders(DispatcherQueue, _ => false, true);
            var sink = reminders.ClockNotifications;
            await store.StartCountdown(1, "留白时钟静音验证", DateTimeOffset.Now.AddSeconds(-3));
            nativeNotificationAccepted = await sink.ShowClock(store.Data.Countdown!, DateTimeOffset.Now, TimeZoneInfo.Local);
            notificationWarning = sink.Warning ?? "";
            if (!nativeNotificationAccepted && notificationWarning.Length == 0) throw new Exception("Native notification failed without actionable warning");
            await sink.ClearClock();
            checks.Add(nativeNotificationAccepted ? "Windows accepted actual alarm notification with silent test audio; test group cleaned" : "Notification unavailable: warning preserved and no false delivery stamp");
            await store.CancelCountdown();
            var liveService = App.StartClocks(DispatcherQueue, id => RequestClock(id));
            await store.StartCountdown(2, "后台服务验证", DateTimeOffset.Now);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while (nativeNotificationAccepted && store.Data.Countdown?.State != ClockState.Finished && DateTimeOffset.UtcNow < deadline) await Task.Delay(100);
            if (nativeNotificationAccepted)
            {
                if (store.Data.Countdown?.State != ClockState.Finished) throw new Exception("Live dispatcher service did not finish countdown");
                var liveFinished = store.Data.Countdown!;
                App.DispatchClockAction(ClockPolicy.Arguments(liveFinished, ClockActionKind.Open)); await Task.Delay(350);
                if (!clockWindow!.IsOpen || !clockWindow.TimerSelected) throw new Exception("Notification body did not open timer page");
                App.DispatchClockAction(ClockPolicy.Arguments(liveFinished, ClockActionKind.Repeat)); await Task.Delay(150);
                if (store.Data.Countdown?.State != ClockState.Running) throw new Exception("Notification repeat did not route to live clock service");
                checks.Add("Real dispatcher countdown service, notification-body routing and repeat action");
            }
            await store.CancelCountdown();
            var coldStore = new ClockStore(Path.Combine(App.DataDirectory, "clock-cold-check.json"));
            await coldStore.StartCountdown(1, "锁屏启动验证", DateTimeOffset.Now.AddSeconds(-3));
            var coldNotices = new VerificationClockNotifications();
            var coldService = new ClockService(coldStore, coldNotices, DispatcherQueue, _ => true, initiallySuspended: true);
            try
            {
                await Task.Delay(1100); if (coldNotices.Count != 0) throw new Exception("Cold suspended service delivered before unlock");
                coldService.SetSuspended(false); deadline = DateTimeOffset.UtcNow.AddSeconds(4);
                while (coldStore.Data.Countdown?.State != ClockState.Finished && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
                if (coldNotices.Count != 1 || coldStore.Data.Countdown?.State != ClockState.Finished) throw new Exception("Cold service did not deliver once after unlock");
                checks.Add("Cold-start suspension delays alarm until unlock, then delivers once");
            }
            finally { await coldService.Stop(); }
            if (clockDot?.Visibility != Visibility.Collapsed) { await Task.Delay(150); if (clockDot?.Visibility != Visibility.Collapsed) throw new Exception("Badge remained active after cancellation"); }
            if (store.HasPending) throw new Exception("Verification left pending clock");
            checks.Add("Cancellation clears badge; isolated test leaves no running timer");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            try
            {
                await App.Clock.CancelCountdown();
                foreach (var item in App.Clock.Data.Alarms) await App.Clock.RemoveAlarm(item.Id);
                if (App.Clocks is { } clocks) await clocks.Stop();
                if (App.Reminders is { } reminders) { await reminders.ClockNotifications.ClearClock(); await reminders.ClearVerification(); await reminders.Stop(); }
            }
            catch (Exception ex) { result = "FAIL"; error += "\nCleanup: " + ex; }
            clockWindow?.Hide(); settingsWindow?.Hide();
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "clock-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, nativeNotificationAccepted, notificationWarning, checks }));
            Close();
        }
    }
    sealed class VerificationClockNotifications : IClockNotifications
    {
        internal int Count;
        public string? Warning => null;
        public Task ReconcileClock(IReadOnlyList<ClockEntry> entries) => Task.CompletedTask;
        public Task<bool> ShowClock(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone) { Count++; return Task.FromResult(true); }
        public Task ClearClock() => Task.CompletedTask;
    }
}
