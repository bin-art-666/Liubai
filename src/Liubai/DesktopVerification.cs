using System.Text.Json;

namespace Liubai;

internal sealed partial class BarManager
{
    internal async Task VerifyDesktop()
    {
        string result = "PASS", error = "";
        var checks = new List<object>();
        int originalHeight = preferences.BarHeight;
        try
        {
            await Task.Delay(800);
            void CheckLayout()
            {
                var displays = Native.Monitors().Where(m => preferences.AllDisplays || m.Primary).ToArray();
                if (bars.Count != displays.Length) throw new InvalidOperationException("Display count did not match bars.");
                foreach (var display in displays)
                {
                    var bar = bars[display.Device];
                    var expected = display.BarBounds(preferences.BarHeight);
                    expected.Y = originalWork[display.Device].Top;
                    if (!Native.Same(bar.BarRectangle, expected) || display.Info.Work.Top != expected.Y + expected.Height)
                        throw new InvalidOperationException($"Invalid bar/work area on {display.Device}.");
                    if (bar.TrayRegistered != display.Primary) throw new InvalidOperationException("Tray icon must exist only on the primary display.");
                    checks.Add(new { display = display.Device, primary = display.Primary, dpi = display.Dpi, bounds = bar.BarRectangle.ToString(), layout = "PASS" });
                }
            }
            preferences.SetAllDisplays(true); RefreshDisplays(); await Task.Delay(300); CheckLayout();
            preferences.SetAllDisplays(false); RefreshDisplays(); await Task.Delay(300); CheckLayout();
            preferences.SetAllDisplays(true); RefreshDisplays(); await Task.Delay(300);
            foreach (int height in new[] { 1, 24, 120, originalHeight })
            { preferences.SetHeight(height); ApplyHeight(); await Task.Delay(150); CheckLayout(); }
            preferences.SetFollowPaused(true); ApplySettings(); await Task.Delay(200);
            var counts = bars.Values.ToDictionary(b => b.Display.Device, b => b.SampleCount);
            await Task.Delay(400);
            if (bars.Values.Any(b => b.Monitoring || b.SampleCount != counts[b.Display.Device]))
                throw new InvalidOperationException("Paused appearance continued sampling.");
            preferences.SetFollowPaused(false); ApplySettings(); await Task.Delay(200);
            if (bars.Values.Any(b => !b.Monitoring)) throw new InvalidOperationException("Sampling did not resume.");
            foreach (var pair in new[] { (0x0218u, 4L, 18L), (0x02B1u, 7L, 8L) })
            {
                EnvironmentChanged(pair.Item1, pair.Item2);
                if (bars.Values.Any(b => b.Monitoring)) throw new InvalidOperationException("Suspend/lock did not stop sampling.");
                EnvironmentChanged(pair.Item1, pair.Item3); await Task.Delay(350);
                if (bars.Values.Any(b => !b.Monitoring)) throw new InvalidOperationException("Resume/unlock did not restart sampling.");
            }
            EnvironmentChanged(0x007E, 0); EnvironmentChanged(0x02E0, 0); EnvironmentChanged(0x001A, 20);
            await Task.Delay(450); CheckLayout();
            checks.Add(new { pause = "PASS", resumeHandlers = "PASS", lockHandlers = "PASS", displayDpiWallpaperHandlers = "PASS",
                physicalDisplays = Native.Monitors().Count, note = "Environment events are simulated; no sleep, wallpaper or scaling settings were changed." });
            ToggleVisibility(); await Task.Delay(200);
            if (bars.Values.Any(b => b.Display.Info.Work.Top != originalWork[b.Display.Device].Top)) throw new InvalidOperationException("Hidden bars did not release all work areas.");
            ToggleVisibility(); await Task.Delay(250); CheckLayout();
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            refreshTimer.Stop();
            foreach (var bar in bars.Values.ToArray()) bar.Shutdown();
            bars.Clear();
            var released = Native.Monitors().All(m => !originalWork.TryGetValue(m.Device, out var work) || m.Info.Work.Top == work.Top);
            if (!released) { result = "FAIL"; error += " Work area was not released."; }
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "desktop-check.json"), JsonSerializer.Serialize(new { result, error, released, checks }));
            Quit();
        }
    }
}
