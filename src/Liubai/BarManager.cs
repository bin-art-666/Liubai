using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Liubai;

internal sealed partial class BarManager
{
    readonly AppearanceStore preferences;
    readonly Dictionary<string, BarWindow> bars = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Native.Rect> originalWork = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherQueueTimer refreshTimer;
    readonly Dictionary<(uint, long), long> environmentEvents = new();
    bool quitting, reconciling, asleep, locked, visible = true;
    internal bool Suspended => asleep || locked;

    internal BarManager(AppearanceStore preferences, DispatcherQueue dispatcher)
    {
        this.preferences = preferences;
        refreshTimer = dispatcher.CreateTimer(); refreshTimer.IsRepeating = false;
        refreshTimer.Interval = TimeSpan.FromMilliseconds(250);
        refreshTimer.Tick += (_, _) => RefreshDisplays();
    }
    internal void Start(bool showSettings)
    {
        RefreshDisplays();
        if (showSettings) bars.Values.FirstOrDefault(b => b.Display.Primary)?.RequestSettings();
    }
    internal void RefreshDisplays()
    {
        if (quitting || reconciling) return;
        reconciling = true;
        try
        {
            var displays = Native.Monitors().Where(m => preferences.AllDisplays || m.Primary).ToArray();
            if (displays.Length == 0) { refreshTimer.Start(); return; }
            // Keep existing windows on unchanged displays; disconnecting releases only its reservation.
            foreach (string removed in bars.Keys.Except(displays.Select(m => m.Device), StringComparer.OrdinalIgnoreCase).ToArray())
            { var old = bars[removed]; bars.Remove(removed); old.Shutdown(); }
            foreach (var display in displays)
            {
                if (bars.TryGetValue(display.Device, out var bar)) bar.UpdateDisplay(display);
                else
                {
                    originalWork[display.Device] = display.Info.Work;
                    bar = new BarWindow(preferences, display, this); bars.Add(display.Device, bar);
                    bar.Start(false); bar.SetVisible(visible);
                }
            }
        }
        finally { reconciling = false; }
    }
    internal void EnvironmentChanged(uint message, long parameter)
    {
        if (quitting) return;
        long now = Environment.TickCount64;
        if (environmentEvents.TryGetValue((message, parameter), out long previous) && now - previous < 150) return;
        if (environmentEvents.Count > 64) environmentEvents.Clear();
        environmentEvents[(message, parameter)] = now;
        if (message == 0x0218 && parameter == 4) asleep = true;
        if (message == 0x0218 && parameter is 7 or 18) asleep = false;
        if (message == 0x02B1 && parameter == 7) locked = true;
        if (message == 0x02B1 && parameter == 8) locked = false;
        if (message is 0x0218 or 0x02B1)
            foreach (var bar in bars.Values.ToArray()) bar.Recover(Suspended);
        if (message is 0x007E or 0x02E0 or 0x031A or 0x031E || message == 0x001A && parameter != 47
            || message == 0x0218 && parameter is 7 or 18 || message == 0x02B1 && parameter == 8)
        {
            refreshTimer.Stop(); refreshTimer.Start();
            if (message is 0x001A or 0x031A or 0x031E)
                foreach (var bar in bars.Values.ToArray()) bar.Recover(Suspended);
        }
    }
    internal void ApplyMaterial() { foreach (var bar in bars.Values.ToArray()) bar.RefreshMaterial(); }
    internal void ApplyHeight() { foreach (var bar in bars.Values.ToArray()) bar.RefreshHeight(); }
    internal void ApplySettings()
    {
        foreach (var bar in bars.Values.ToArray()) bar.RefreshSettings();
        refreshTimer.Stop(); refreshTimer.Start();
    }
    internal void HideOtherPanels(BarWindow owner)
    { foreach (var bar in bars.Values) if (bar != owner) bar.HideSettings(); }
    internal void ToggleVisibility()
    { visible = !visible; foreach (var bar in bars.Values.ToArray()) bar.SetVisible(visible); }
    internal void TogglePause()
    {
        bool before = preferences.FollowPaused;
        preferences.SetFollowPaused(!before);
        try { preferences.SaveCurrent(); }
        catch { preferences.SetFollowPaused(before); bars.Values.FirstOrDefault(b => b.Display.Primary)?.RequestSettings(); return; }
        ApplySettings();
        foreach (var bar in bars.Values) bar.RefreshPanel();
    }
    internal void Quit()
    {
        if (quitting) return;
        quitting = true; refreshTimer.Stop();
        foreach (var bar in bars.Values.ToArray()) bar.Shutdown();
        bars.Clear(); Application.Current.Exit();
    }
}
