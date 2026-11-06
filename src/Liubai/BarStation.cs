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
        shelf.Click += (_, _) =>
        {
            if (ToolbarClickBlocked || PanelModal || manager?.HasModal == true || manager?.Suspended == true || closing || openingPlanner) return;
            stationHoverTimer.Stop();
            if (stationWindow?.IsOpen == true) stationWindow.Hide(); else OpenStation();
        };
        shelf.DragEnter += StationHover; shelf.DragOver += StationHover;
        shelf.DragLeave += (_, _) => { stationHoverTimer.Stop(); stationWindow?.ScheduleDragDismiss(); };
        shelf.Drop += async (_, e) =>
        {
            stationHoverTimer.Stop(); e.Handled = true; var deferral = e.GetDeferral(); bool saved = false;
            try
            {
                if (!OpenStation(true)) { e.AcceptedOperation = DataPackageOperation.None; return; }
                saved = await stationWindow!.Receive(e.DataView);
                e.AcceptedOperation = saved ? DataPackageOperation.Copy : DataPackageOperation.None;
            }
            finally { deferral.Complete(); }
            if (saved) stationWindow?.ConfirmDrop();
        };
    }
    void StationHover(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool accepted = !closing && !openingPlanner && !PanelModal && manager?.HasModal != true && !App.Station.ReadOnly && StationTransfer.Supported(e.DataView);
        e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (!accepted) { stationHoverTimer?.Stop(); return; }
        e.DragUIOverride.Caption = "存入中转站"; e.DragUIOverride.IsCaptionVisible = true;
        if (stationWindow?.IsOpen != true && stationHoverTimer?.IsRunning != true) stationHoverTimer?.Start();
    }
    bool OpenStation(bool forDrop = false)
    {
        if (closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true || !Native.MonitorExists(Display.Handle)) return false;
        clockWindow?.Hide();
        App.Ai.Hide(false);
        settingsWindow?.Hide(); plannerWindow?.Hide(); qrResults?.Hide(); manager?.HideOtherPanels(this);
        stationWindow ??= new StationWindow(App.Station, Display);
        stationWindow.Open(Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top, forDrop);
        return true;
    }
}
