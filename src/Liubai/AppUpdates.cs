namespace Liubai;

public partial class App
{
    static UpdateService? updates;
    internal static UpdateService Updates => updates ??= new();
    internal static Task<bool> ApplyMaintenance(UpdatePlan plan) => ((App)Current).manager?.ApplyMaintenance(plan) ?? Task.FromResult(false);
}

internal sealed partial class BarManager
{
    internal async Task<bool> ApplyMaintenance(UpdatePlan plan)
    {
        if (quitting || HasModal || App.Clock.HasPending || App.Privacy.Busy || App.Updates.Busy) return false;
        quitting = true;
        App.Updates.Applying(true);
        bool exited = false;
        try
        {
            if (plannerPanel != null && !await plannerPanel.FlushAsync()) return false;
            foreach (var bar in bars.Values) if (!await bar.FlushToolPanels()) return false;
            App.Clocks?.SetSuspended(true); App.Reminders?.SetSuspended(true);
            if (App.Clocks != null) await App.Clocks.FlushForMaintenance();
            if (App.Reminders != null) await App.Reminders.FlushForMaintenance();
            await ScreenshotService.Flush(); await App.FlushStation(); await App.FlushWork();
            await App.Updates.LaunchWorker(plan);
            await App.StopGuard(); App.StopAi();
            if (App.Clocks != null) await App.Clocks.Stop();
            if (App.Reminders != null) await App.Reminders.Stop();
            App.Updates.Commit(plan);
            refreshTimer.Stop(); plannerPanel?.Destroy();
            foreach (var bar in bars.Values.ToArray()) bar.Shutdown();
            bars.Clear(); exited = true; WindowsCompositionQueue.ExitApplication(); return true;
        }
        finally
        {
            if (!exited) { App.Clocks?.SetSuspended(Suspended); App.Reminders?.SetSuspended(Suspended); }
            if (!exited) App.Updates.Applying(false);
            quitting = false;
        }
    }
}

public sealed partial class BarWindow
{
    internal Task<bool> FlushAllSettings() => settingsWindow?.SaveAllSettings() ?? Task.FromResult(true);
}
