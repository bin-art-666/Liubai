using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

public sealed partial class BarWindow
{
    StationWindow? stationWindow;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? stationHoverTimer;
    void InitializeStationToolbar()
    {
        shelf.AllowDrop = true; ToolTipService.SetToolTip(shelf, "收纳盒 · 中转站（支持拖入内容）");
        stationHoverTimer = DispatcherQueue.CreateTimer(); stationHoverTimer.IsRepeating = false; stationHoverTimer.Interval = TimeSpan.FromMilliseconds(350);
        stationHoverTimer.Tick += (_, _) => OpenStation(true);
        shelf.DragEnter += StationHover; shelf.DragOver += StationHover;
        shelf.DragLeave += (_, _) => { stationHoverTimer.Stop(); stationWindow?.ScheduleDragDismiss(); };
        shelf.Drop += async (_, e) =>
        {
            stationHoverTimer.Stop(); e.Handled = true; var deferral = e.GetDeferral(); bool saved = false;
            try
            {
                saved = await tools!.Drop("station", e.DataView);
                e.AcceptedOperation = saved ? DataPackageOperation.Copy : DataPackageOperation.None;
            }
            finally { deferral.Complete(); }
        };
    }
    void StationHover(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool accepted = tools?.Accepts("station", e.DataView) == true;
        e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (!accepted) { stationHoverTimer?.Stop(); return; }
        e.DragUIOverride.Caption = "存入中转站"; e.DragUIOverride.IsCaptionVisible = true;
        if (stationWindow?.IsOpen != true && stationHoverTimer?.IsRunning != true) stationHoverTimer?.Start();
    }
    bool OpenStation(bool forDrop = false)
    {
        if (closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true || !Native.MonitorExists(Display.Handle)) return false;
        HideUtilityPanels();
        clockWindow?.Hide();
        App.HideAi(false);
        settingsWindow?.Hide(); plannerWindow?.Hide(); qrResults?.Hide(); manager?.HideOtherPanels(this);
        if (stationWindow is null) { stationWindow = new StationWindow(App.Station, Display); stationWindow.WorkRequested += OpenWorkScene; }
        stationWindow.Open(Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top, forDrop);
        return true;
    }
}
