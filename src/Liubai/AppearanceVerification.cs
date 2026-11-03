using Microsoft.UI.Windowing;
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
                if (SystemBackdrop != null || surface.Background is not SolidColorBrush fill || Rgb(fill.Color) != expected)
                    throw new InvalidOperationException($"Unexpected solid background, expected #{expected:X6}.");
                if (((FontIcon)settings.Content).Foreground is not SolidColorBrush ink || Rgb(ink.Color) != (white ? 0xFFFFFFu : 0))
                    throw new InvalidOperationException("Icon contrast was not applied.");
            }
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
            if (sampler.ReadTarget(probeHandle, AppWindow.Size.Height).IsExpanded)
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
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            probe?.Close();
            preferences.SetExpandedAppearance(saved);
            expandedSnapshot = ExpandedSnapshot.Normal;
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "expanded-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
