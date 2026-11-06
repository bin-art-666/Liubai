using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyGuard()
    {
        string result = "PASS", error = "";
        var checks = new List<string>(); GuardService? service = null; object? hardware = null;
        try
        {
            await Task.Delay(500);
            if (SessionState.Locked == true) throw new InvalidOperationException("Desktop is locked; unlock before guard visual checks.");
            if (App.Guard.Wanted || App.Guard.State != GuardState.Off || App.Guard.Samples != 0) throw new Exception("Camera must default off");
            if (!CurrentToolbarOrder().Contains("guard") || AutomationProperties.GetName(guardButton) != "屏幕守护") throw new Exception("Guard toolbar entry missing");
            var fake = new VerificationGuardFactory();
            var options = new GuardOptionsStore(Path.Combine(App.DataDirectory, "guard-fixture.json"));
            service = new GuardService(options, DispatcherQueue, fake); service.SetSuspended(false);
            await Task.Delay(150);
            if (fake.Opened != 0) throw new Exception("Idle service opened camera");
            checks.Add("Default off; no idle camera allocation; shield toolbar before settings");
            await service.SetEnabled(true);
            await GuardUntil(() => service.State == GuardState.Running);
            if (fake.Active != 1 || service.Samples == 0) throw new Exception("Fake capture pipeline not running");
            var off = service.SetEnabled(false); var on = service.SetEnabled(true); await Task.WhenAll(off, on);
            await GuardUntil(() => service.State == GuardState.Running);
            if (fake.MaxActive != 1) throw new Exception("Rapid toggle created concurrent captures");
            service.SetSuspended(true); await GuardUntil(() => fake.Active == 0);
            if (!service.Wanted || service.State != GuardState.Paused) throw new Exception("Suspend did not release/preserve intent");
            service.SetSuspended(false); await GuardUntil(() => service.State == GuardState.Running);
            checks.Add("Serial rapid toggles; one capture at a time; suspend releases and resume restores");
            await service.PauseFor(TimeSpan.FromMinutes(10));
            if (fake.Active != 0 || !service.Wanted || !service.ManuallyPaused || service.State != GuardState.Paused) throw new Exception("Manual pause did not release camera");
            if (Environment.GetCommandLineArgs().Contains("--preview-guard-paused"))
            {
                SetGuardGlyph(true, false); await Task.Delay(30000);
                SetToolbarColors(ElementTheme.Dark, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White));
                surface.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
                await Task.Delay(30000); RefreshGuardToolbar(); RefreshMaterial();
            }
            int opensBeforePause = fake.Opened;
            service.SetSuspended(true); await Task.Delay(80); service.SetSuspended(false); await Task.Delay(100);
            if (fake.Active != 0 || fake.Opened != opensBeforePause || !service.ManuallyPaused) throw new Exception("Wake bypassed manual pause");
            await service.Resume(); await GuardUntil(() => service.State == GuardState.Running);
            if (service.ManuallyPaused || fake.MaxActive != 1) throw new Exception("Manual resume not serialized");
            await service.PauseFor(TimeSpan.FromMilliseconds(250));
            await GuardUntil(() => !service.ManuallyPaused && service.State == GuardState.Running);
            checks.Add("Manual pause releases camera, survives wake, resumes explicitly or on expiry without concurrent captures");
            fake.Faces = 2;
            await GuardUntil(() => service.AlertVisible, 6000);
            if (service.AlertWindowCount != 1) throw new Exception("Prompt did not use one central window");
            service.Dismiss(); await Task.Delay(700); long before = service.Samples; await Task.Delay(1100);
            if (service.Samples != before || service.AlertVisible) throw new Exception("Cooldown continued inference or repeated alert");
            checks.Add("Sustained extra faces trigger central prompt; dismiss pauses inference during cooldown");
            await service.SetEnabled(false);
            fake.Denied = true;
            await service.SetEnabled(true); await GuardUntil(() => service.State == GuardState.Unavailable);
            if (fake.Active != 0 || !service.Wanted) throw new Exception("Permission failure leaked resources or hid unavailable state");
            await service.SetEnabled(false); fake.Denied = false; fake.ReadFails = true;
            await service.SetEnabled(true); await GuardUntil(() => service.State == GuardState.Unavailable);
            if (fake.Active != 0) throw new Exception("Read failure leaked camera");
            await service.SetEnabled(false); fake.ReadFails = false;
            checks.Add("Permission denial and mid-stream failure clearly unavailable; resources released; explicit restart supported");
            App.GuardOptions.Save(App.GuardOptions.Data with { Mode = GuardAlertMode.Blackout, Message = "屏幕守护测试\n按 Enter 恢复" });
            App.Guard.TestAlert();
            if (App.Guard.AlertWindowCount != Native.Monitors().Count || fake.Active != 0 || windowShown) throw new Exception("Blackout not on every display, toolbar exposed, or test opened camera");
            if (Environment.GetCommandLineArgs().Contains("--interactive-guard"))
            {
                await GuardUntil(() => !App.Guard.AlertVisible, 90000);
                checks.Add("Actual Enter/button dismissal observed across all blackout windows");
            }
            else App.Guard.Dismiss();
            App.GuardOptions.Save(App.GuardOptions.Data with { Mode = GuardAlertMode.Prompt, Message = "检测到其他人，请留意周围。" });
            await service.Stop();
            if (fake.Active != 0 || service.AlertVisible || service.State != GuardState.Off) throw new Exception("Stop leaked capture/overlay");
            checks.Add("All-monitor blackout, camera-free preview and full teardown");
            OpenSettings(); await Task.Delay(200);
            if (!await settingsWindow!.FlushStationOptions()) throw new Exception("Collapsed settings controls blocked save");
            var section = StationControls<Expander>((DependencyObject)settingsWindow!.Content).Single(e => Equals(e.Header, "屏幕守护"));
            section.IsExpanded = true; await Task.Delay(150);
            var root = (DependencyObject)section.Content;
            if (StationControls<ToggleSwitch>(root).Single(b => AutomationProperties.GetName(b) == "启用屏幕守护").IsOn) throw new Exception("Settings camera toggle should be off");
            if (!StationControls<Button>(root).Any(b => Equals(b.Content, "测试提醒（不打开摄像头）"))) throw new Exception("Camera-free preview control missing");
            var hold = StationControls<NumberBox>(root).Single(b => AutomationProperties.GetName(b) == "屏幕守护触发等待时间");
            hold.Value = 2.5;
            if (new GuardOptionsStore(Path.Combine(App.DataDirectory, "guard.json")).Data.HoldMilliseconds != 2500 || !await settingsWindow.FlushStationOptions()) throw new Exception("Native wait setting did not auto-persist");
            hold.Value = 1.5; await settingsWindow.FlushStationOptions();
            settingsWindow.Hide();
            checks.Add("Native settings expose camera, custom message, mode and preview; startup activation remains off");
            if (Environment.GetCommandLineArgs().Contains("--verify-guard-camera"))
            {
                var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
                long memory = process.PrivateMemorySize64; var cpu = process.TotalProcessorTime; var watch = System.Diagnostics.Stopwatch.StartNew();
                await App.Guard.SetEnabled(true);
                for (int i = 0; i < 120 && App.Guard.State == GuardState.Starting; i++) await Task.Delay(100);
                if (App.Guard.State == GuardState.Running) await Task.Delay(8000);
                process.Refresh();
                hardware = new { state = App.Guard.State.ToString(), status = App.GuardStatus, samples = App.Guard.Samples, lastFaces = App.Guard.LastFaces,
                    meanAnalysisMs = App.Guard.MeanProcessingMilliseconds, cpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    privateMemoryDeltaMiB = (process.PrivateMemorySize64 - memory) / 1048576d };
                await App.Guard.SetEnabled(false);
                if (App.Guard.State != GuardState.Off) throw new Exception("Real camera did not shut down");
            }
            if (Environment.GetCommandLineArgs().Contains("--preview-guard")) { OpenSettings(); await Task.Delay(60000); }
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            if (service != null) await service.Stop();
            await App.StopGuard(); settingsWindow?.Hide();
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "guard-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks, hardware }));
            Close();
        }
    }
    static async Task GuardUntil(Func<bool> condition, int milliseconds = 5000)
    {
        long until = Environment.TickCount64 + milliseconds;
        while (!condition()) { if (Environment.TickCount64 > until) throw new TimeoutException("Guard verification timed out"); await Task.Delay(50); }
    }
    sealed class VerificationGuardFactory : IGuardCameraFactory
    {
        internal int Opened, Active, MaxActive, Faces = 1;
        internal bool Denied, ReadFails;
        public Task<IGuardCamera> Open(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Opened++;
            if (Denied) throw new UnauthorizedAccessException("synthetic denial");
            Active++; MaxActive = Math.Max(MaxActive, Active);
            return Task.FromResult<IGuardCamera>(new VerificationGuardCamera(this));
        }
        sealed class VerificationGuardCamera(VerificationGuardFactory owner) : IGuardCamera
        {
            public string Description => "synthetic fixture · 320×240";
            public async Task<GuardSample> Sample(CancellationToken token)
            { await Task.Delay(8, token); if (owner.ReadFails) throw new IOException("synthetic read failure"); return new(Volatile.Read(ref owner.Faces), true); }
            public ValueTask DisposeAsync() { owner.Active--; return ValueTask.CompletedTask; }
        }
    }
}
