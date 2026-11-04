using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyExpanded()
    {
        string result = "PASS", error = "";
        var checks = new List<object>();
        var saved = preferences.Expanded;
        Window? probe = null;
        Window? occluder = null;
        appearanceMonitor?.SetEnabled(false);
        try
        {
            await Task.Delay(350);
            settingsWindow?.Hide();
            var background = new SolidColorBrush();
            probe = new Window { Title = "留白外观自检", Content = new Grid { Background = background } };
            probe.Activate();
            // The verification process may be launched with SW_HIDE. Explicitly show
            // the probe after WinUI's initial activation consumes the startup flag.
            probe.AppWindow.Show(true);
            var presenter = (OverlappedPresenter)probe.AppWindow.Presenter;
            presenter.Maximize();
            IntPtr probeHandle = WinRT.Interop.WindowNative.GetWindowHandle(probe);
            var sampler = new WindowColorSampler();
            static uint Rgb(Color color) => ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            void CheckRender(uint expected, bool white)
            {
                if (SystemBackdrop is not GlassBackdrop || surface.Background is not SolidColorBrush fill || Rgb(fill.Color) != expected)
                    throw new InvalidOperationException($"Unexpected solid background, expected #{expected:X6}.");
                if (((FontIcon)settings.Content).Foreground is not SolidColorBrush ink || Rgb(ink.Color) != (white ? 0xFFFFFFu : 0))
                    throw new InvalidOperationException("Icon contrast was not applied.");
                foreach (var button in new[] { screenshot, todo, quickNote, shelf })
                {
                    var outline = (Microsoft.UI.Xaml.Shapes.Path)((Canvas)((Viewbox)button.Content).Child).Children[0];
                    if (outline.Stroke is not SolidColorBrush iconInk || Rgb(iconInk.Color) != Rgb(ink.Color))
                        throw new InvalidOperationException("Toolbar icons did not share the settings contrast color.");
                }
            }
            var displayInfo = Native.PrimaryMonitor();
            occluder = new Window { Title = "留白局部遮挡自检", Content = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 240, 30, 30)) } };
            ((OverlappedPresenter)occluder.AppWindow.Presenter).SetBorderAndTitleBar(false, false);
            occluder.Activate(); occluder.AppWindow.Show(true);
            var occluderHandle = WinRT.Interop.WindowNative.GetWindowHandle(occluder);
            Native.RemoveWindowFrame(occluderHandle);
            occluder.AppWindow.MoveAndResize(new RectInt32(displayInfo.Work.Left, displayInfo.Work.Top,
                (displayInfo.Work.Right - displayInfo.Work.Left) * 3 / 4, 300));
            Native.KeepOnTop(occluderHandle); Native.KeepOnTop(handle);
            await Task.Delay(300);
            var fragment = sampler.ReadTarget(probeHandle, AppWindow.Size.Height);
            if (!fragment.IsExpanded || fragment.Color != null)
                throw new InvalidOperationException("A small uncovered fragment was accepted as a full-width window color.");
            var floating = sampler.ReadTarget(occluderHandle, AppWindow.Size.Height);
            if (floating.IsExpanded || floating.Color != null)
                throw new InvalidOperationException("A floating red window entered the maximized appearance or invoked color sampling.");
            expandedSnapshot = new(true, probeHandle.ToInt64(), 0xFF0000, true, false);
            preferences.SetExpandedAppearance(new(true, ExpandedMode.FollowWindow)); RenderAppearance();
            if (SystemBackdrop is not GlassBackdrop) throw new InvalidOperationException("An unknown window color did not fall back to daily glass.");
            preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#9AE5C7")); RenderAppearance(); CheckRender(0x9AE5C7, false);
            occluder.Close(); occluder = null;
            Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
            var withoutCapture = sampler.ReadTarget(probeHandle, AppWindow.Size.Height, sampleColor: false);
            if (!withoutCapture.IsExpanded || withoutCapture.Color != null)
                throw new InvalidOperationException("Custom mode did not separate the scene decision from color capture.");
            checks.Add(new { partialOcclusion = "PASS", floatingWindow = "PASS", unknownColorFallback = "PASS", customWithoutCapture = "PASS" });
            var terminalSurface = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 12, 12, 12)) };
            terminalSurface.Children.Add(new Border { Height = 26, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)) });
            probe.Content = terminalSurface;
            await Task.Delay(350);
            var terminalSample = sampler.ReadTarget(probeHandle, AppWindow.Size.Height);
            if (!terminalSample.IsExpanded || terminalSample.Color is not { } terminalColor || WindowColorAnalysis.Brightness(terminalColor) >= .2)
                throw new InvalidOperationException($"Layered dark title bar did not yield a dark background: {terminalSample}.");
            checks.Add(new { layeredDarkTitleBar = "PASS", captured = $"#{terminalColor:X6}" });
            probe.Content = new Grid { Background = background };
            foreach (uint rgb in new uint[] { 0x141414, 0xFAFAFA, 0x2E6594 })
            {
                background.Color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
                await Task.Delay(500);
                int barHeight = AppWindow.Size.Height;
                var sample = await Task.Run(() => sampler.ReadTarget(probeHandle, barHeight));
                if (!sample.IsExpanded || sample.Color is not { } captured || !WindowColorAnalysis.Similar(rgb, captured, 18))
                    throw new InvalidOperationException($"Window top color capture failed for #{rgb:X6}: {sample}.");
                var tracker = new ExpandedAppearanceTracker();
                tracker.Observe(sample, 0);
                expandedSnapshot = tracker.Observe(sample, 300) ?? throw new InvalidOperationException("Stable sample was not committed.");
                preferences.SetExpandedAppearance(new(true, ExpandedMode.Automatic)); RenderAppearance();
                uint automatic = expandedSnapshot.IsDark ? 0u : 0xFFFFFF;
                CheckRender(automatic, automatic == 0);
                preferences.SetExpandedAppearance(new(true, ExpandedMode.FollowWindow)); RenderAppearance();
                CheckRender(captured, WindowColorAnalysis.WhiteForeground(captured));
                preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#9AE5C7")); RenderAppearance();
                CheckRender(0x9AE5C7, false);
                checks.Add(new { source = $"#{rgb:X6}", captured = $"#{captured:X6}", automatic = $"#{automatic:X6}", modes = "PASS" });
            }
            // Exercise the real composed overlay, not just the destination brush.
            expandedSnapshot = new(true, probeHandle.ToInt64(), 0, true);
            preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#000000")); RenderAppearance();
            await Task.Delay(100);
            IntPtr foregroundBefore = Native.GetForegroundWindow();
            preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#FFFFFF")); RenderAppearance(true);
            bool animationStarted = transition.Active;
            var shades = new List<int>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (transition.Active && clock.ElapsedMilliseconds < 900)
            {
                await Task.Delay(15);
                var pixel = BarAppearanceTransition.Capture(new RectInt32(AppWindow.Position.X + 100, AppWindow.Position.Y + AppWindow.Size.Height / 2, 1, 1));
                if (pixel != null) shades.Add(pixel[0]);
            }
            clock.Stop();
            if (transition.Active) throw new InvalidOperationException("Transition overlay did not clear.");
            if (animationStarted && !shades.Any(v => v > 12 && v < 243))
                throw new InvalidOperationException("No intermediate composed colors were displayed during the transition.");
            if (Native.GetForegroundWindow() != foregroundBefore) throw new InvalidOperationException("Transition stole window focus.");
            CheckRender(0xFFFFFF, false);
            preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#000000", Animation: AppearanceAnimation.Soft)); RenderAppearance(true);
            await Task.Delay(60);
            preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, "#2E6594", Animation: AppearanceAnimation.Off)); RenderAppearance(true);
            if (transition.Active) throw new InvalidOperationException("Animation-off did not cancel an interrupted transition.");
            CheckRender(0x2E6594, true);
            await Task.Delay(350);
            CheckRender(0x2E6594, true);
            checks.Add(new { transition = animationStarted ? "PASS" : "System animations disabled", elapsedMs = clock.ElapsedMilliseconds,
                intermediateFrames = shades.Count(v => v > 12 && v < 243), interruption = "PASS", focus = "PASS" });
            presenter.Restore();
            var monitor = Native.PrimaryMonitor();
            probe.AppWindow.MoveAndResize(new RectInt32(monitor.Monitor.Left + 100, monitor.Work.Top + 100, 600, 400));
            await Task.Delay(350);
            var restored = sampler.ReadTarget(probeHandle, AppWindow.Size.Height);
            if (restored.IsExpanded || restored.Color != null)
                throw new InvalidOperationException("Restored window was classified as maximized.");
            expandedSnapshot = ExpandedSnapshot.Normal; RenderAppearance();
            if (SystemBackdrop is not GlassBackdrop) throw new InvalidOperationException("Daily glass was not restored.");
            checks.Add(new { restoredGlass = "PASS" });
            presenter.SetBorderAndTitleBar(false, false);
            Native.RemoveWindowFrame(probeHandle);
            var bounds = monitor.Monitor;
            probe.AppWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
            await Task.Delay(350);
            if (!sampler.ReadTarget(probeHandle, AppWindow.Size.Height).IsExpanded)
                throw new InvalidOperationException("Borderless fullscreen window was not recognized.");
            expandedSnapshot = new(true, probeHandle.ToInt64(), 0x141414, true);
            preferences.SetExpandedAppearance(new(true)); fullscreen = true; UpdateVisibility(); RenderAppearance();
            if (!windowShown) throw new InvalidOperationException("Bar was hidden in enabled fullscreen mode.");
            preferences.SetExpandedAppearance(new(false)); RenderAppearance(); UpdateVisibility();
            if (windowShown) throw new InvalidOperationException("Disabled fullscreen override did not retain auto-hide.");
            fullscreen = false; UpdateVisibility();
            checks.Add(new { fullscreen = "PASS", enabledVisibility = "PASS", disabledAutoHide = "PASS" });
            var stripes = new Grid();
            int stripeCount = (int)Math.Ceiling((bounds.Right - bounds.Left) / (4 * Display.Dpi / 96d));
            for (int column = 0; column < stripeCount; column++)
            {
                stripes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                var stripe = new Border { Background = new SolidColorBrush(column % 2 == 0 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White) };
                Grid.SetColumn(stripe, column); stripes.Children.Add(stripe);
            }
            probe.Content = stripes;
            probe.AppWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
            var flatOptions = new MaterialOptions(false, 0, 0, false, "#FF0000", false, true);
            var flatBackdrop = new GlassBackdrop(flatOptions);
            SystemBackdrop = flatBackdrop; surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); surface.BorderThickness = new Thickness(0);
            await Task.Delay(400);
            byte[] StripPixels() => BarAppearanceTransition.Capture(new RectInt32(AppWindow.Position.X + 100,
                AppWindow.Position.Y + AppWindow.Size.Height / 2, 192, 1)) ?? throw new InvalidOperationException("Could not capture no-base test pixels.");
            var clearPixels = StripPixels();
            var levels = Enumerable.Range(0, clearPixels.Length / 4).Select(i => (int)clearPixels[i * 4]).ToArray();
            if (!flatBackdrop.IsNoBaseActive || levels.Max() - levels.Min() < 220)
                throw new InvalidOperationException("No-base mode blurred the high-frequency background or remained opaque.");
            flatBackdrop.Update(flatOptions with { TintOpacity = .5f }); await Task.Delay(150);
            var tintedPixels = StripPixels();
            if (Enumerable.Range(0, tintedPixels.Length / 4).Min(i => tintedPixels[i * 4 + 2]) < 100
                || Enumerable.Range(0, tintedPixels.Length / 4).Max(i => tintedPixels[i * 4 + 1]) < 100)
                throw new InvalidOperationException("No-base tint did not alpha-blend with the visible background.");
            flatBackdrop.Update(flatOptions with { LuminosityOpacity = 1 }); await Task.Delay(150);
            var maskPixels = StripPixels();
            byte expectedMask = surface.ActualTheme == ElementTheme.Dark ? (byte)0 : (byte)255;
            if (Enumerable.Range(0, maskPixels.Length / 4).Any(i => Math.Abs(maskPixels[i * 4] - expectedMask) > 3))
                throw new InvalidOperationException("No-base light/dark mask was not applied.");
            RenderAppearance(); await Task.Delay(350);
            if (flatBackdrop.IsNoBaseActive || !flatBackdrop.IsReady)
                throw new InvalidOperationException("Switching back to acrylic did not restore the native base.");
            flatBackdrop.Update(flatOptions); await Task.Delay(150);
            var switchedPixels = StripPixels();
            var switchedLevels = Enumerable.Range(0, switchedPixels.Length / 4).Select(i => (int)switchedPixels[i * 4]).ToArray();
            if (!flatBackdrop.IsNoBaseActive || switchedLevels.Max() - switchedLevels.Min() < 220)
                throw new InvalidOperationException("Changing an existing acrylic base to no-base did not remove the blur.");
            RenderAppearance(); await Task.Delay(150);
            checks.Add(new { noBaseTransparency = "PASS", unblurredContrast = levels.Max() - levels.Min(), tintBlending = "PASS", flatMask = "PASS", restoreAcrylic = "PASS" });
            foreach (var restoreCase in new[] { (color: 0x000000u, fullscreen: true), (color: 0xFFFFFFu, fullscreen: true), (color: 0x2E6594u, fullscreen: false) })
            {
                preferences.SetExpandedAppearance(new(true, ExpandedMode.Custom, $"#{restoreCase.color:X6}"));
                if (restoreCase.fullscreen) probe.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                else
                {
                    probe.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    ((OverlappedPresenter)probe.AppWindow.Presenter).Maximize();
                }
                Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
                await Task.Delay(200);
                expandedSnapshot = new(true, probeHandle.ToInt64(), restoreCase.color, WindowColorAnalysis.WhiteForeground(restoreCase.color));
                RenderAppearance(); await Task.Delay(100);
                var warm = SystemBackdrop as GlassBackdrop ?? throw new InvalidOperationException("Maximized surface disconnected the glass material.");
                int connections = warm.ConnectionCount;
                probe.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                ((OverlappedPresenter)probe.AppWindow.Presenter).Restore();
                probe.AppWindow.MoveAndResize(new RectInt32(monitor.Monitor.Left + 120, monitor.Work.Top + 120, 600, 400));
                Native.KeepOnTop(handle);
                expandedSnapshot = ExpandedSnapshot.Normal; RenderAppearance(true);
                bool restoring = transition.Active;
                appearanceMonitor?.SetEnabled(true);
                appearanceMonitor?.Refresh(); RefreshEnvironment();
                if (restoring && !transition.Active) throw new InvalidOperationException("Restore notifications removed the transition cover prematurely.");
                var restoreClock = System.Diagnostics.Stopwatch.StartNew();
                var frames = new List<uint>();
                while (transition.Active && restoreClock.ElapsedMilliseconds < 1200)
                {
                    await Task.Delay(15);
                    appearanceMonitor?.Refresh();
                    var data = BarAppearanceTransition.Capture(new RectInt32(AppWindow.Position.X + 100, AppWindow.Position.Y + AppWindow.Size.Height / 2, 1, 1));
                    if (data != null) frames.Add(((uint)data[2] << 16) | ((uint)data[1] << 8) | data[0]);
                }
                appearanceMonitor?.SetEnabled(false);
                if (transition.Active || !ReferenceEquals(SystemBackdrop, warm) || warm.ConnectionCount != connections)
                    throw new InvalidOperationException("Restoring recreated the native material or left a stale transition cover.");
                if (surface.Background is not SolidColorBrush restoredFill || restoredFill.Color.A != 0 && DesktopAcrylicController.IsSupported())
                    throw new InvalidOperationException("Restoring did not reveal the daily glass surface.");
                checks.Add(new { restore = restoreCase.fullscreen ? "fullscreen-to-window" : "maximized-to-window",
                    source = $"#{restoreCase.color:X6}", warmMaterial = "PASS", notificationContinuity = "PASS", sampledFrames = frames.Count,
                    elapsedMs = restoreClock.ElapsedMilliseconds });
            }
            // Exercise the real monitor under repeated wake notifications. The
            // explicit reader allows our in-process probe through the normal
            // production exclusion of Liubai's own windows.
            probe.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            Native.KeepOnTop(probeHandle); Native.KeepOnTop(handle);
            await Task.Delay(200);
            var latencyClock = new System.Diagnostics.Stopwatch();
            long exitMs = -1;
            using var restoreMonitor = new ExpandedWindowMonitor(DispatcherQueue, () => AppWindow.Size.Height,
                snapshot =>
                {
                    expandedSnapshot = snapshot; RenderAppearance(true);
                    if (!snapshot.IsExpanded && latencyClock.IsRunning) exitMs = latencyClock.ElapsedMilliseconds;
                }, () => Display, () => true, (height, display, color) => sampler.ReadTarget(probeHandle, height, display, color));
            restoreMonitor.SetSpeed(AppearanceSpeed.Balanced);
            restoreMonitor.SetEnabled(true);
            await Task.Delay(650);
            if (!restoreMonitor.Current.IsExpanded) throw new InvalidOperationException("Restore latency probe did not enter fullscreen.");
            latencyClock.Start();
            probe.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            ((OverlappedPresenter)probe.AppWindow.Presenter).Restore();
            probe.AppWindow.MoveAndResize(new RectInt32(monitor.Monitor.Left + 120, monitor.Work.Top + 120, 600, 400));
            while (latencyClock.ElapsedMilliseconds < 900 && (exitMs < 0 || transition.Active))
            {
                restoreMonitor.Refresh();
                await Task.Delay(15);
            }
            if (exitMs < 0 || exitMs > 300 || transition.Active)
                throw new InvalidOperationException($"Restore was delayed by repeated events: detection={exitMs}ms, total={latencyClock.ElapsedMilliseconds}ms.");
            checks.Add(new { monitorRestoreLatency = "PASS", repeatedEvents = "PASS", detectionMs = exitMs,
                completedMs = latencyClock.ElapsedMilliseconds });
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            occluder?.Close();
            probe?.Close();
            preferences.SetExpandedAppearance(saved);
            expandedSnapshot = ExpandedSnapshot.Normal;
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "expanded-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
