using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Liubai;

internal sealed partial class StationWindow
{
    enum StationEdge { Left, Right, Top, Bottom }
    sealed class StationGrip : Grid
    {
        internal StationGrip(bool vertical) { ProtectedCursor = InputSystemCursor.Create(vertical ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast); }
    }
    readonly List<Grid> stationResizeHosts = [];
    readonly Dictionary<StationEdge, Thumb> stationResizeGrips = [];
    StationPanelOptions dockLayout = new(), resizeBefore = new();
    StationPanelRect resizeStart = new(0, 0, 0, 0);
    StationEdge resizeEdge;
    int resizeCursor;
    bool stationResizing;
    IDisposable? stationResizeLease;
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point point);
    void InitializeStationResize(Grid shell)
    {
        foreach (StationEdge edge in Enum.GetValues<StationEdge>())
        {
            bool vertical = edge is StationEdge.Top or StationEdge.Bottom;
            var host = new StationGrip(vertical) {
                HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : edge == StationEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = vertical ? edge == StationEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom : VerticalAlignment.Stretch };
            if (vertical) { host.Height = 10; host.Margin = new Thickness(10, 0, 10, 0); } else host.Width = 10;
            var grip = new Thumb { Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), IsTabStop = true, IsEnabled = !panelLayout.ReadOnly };
            grip.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid Background=\"Transparent\"><Border Width=\"" + (vertical ? "32" : "2")
                + "\" Height=\"" + (vertical ? "2" : "32") + "\" CornerRadius=\"1\" Background=\"Gray\" Opacity=\"0.35\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Grid></ControlTemplate>");
            string name = edge switch { StationEdge.Left => "从左边缘调整中转站宽度", StationEdge.Right => "从右边缘调整中转站宽度", StationEdge.Top => "从上边缘调整中转站高度", _ => "从下边缘调整中转站高度" };
            AutomationProperties.SetName(grip, name); AutomationProperties.SetHelpText(grip, "拖动调整，松开自动保存；方向键微调，Esc 取消。"); ToolTipService.SetToolTip(grip, name);
            host.Children.Add(grip); shell.Children.Add(host); stationResizeHosts.Add(host); stationResizeGrips.Add(edge, grip);
            grip.DragStarted += (_, _) => { if (GetCursorPos(out var p)) BeginStationResize(vertical ? p.Y : p.X, edge); };
            grip.DragDelta += (_, _) => { if (GetCursorPos(out var p)) MoveStationResize(vertical ? p.Y : p.X); };
            grip.DragCompleted += (_, e) => EndStationResize(e.Canceled);
            grip.KeyDown += (_, e) =>
            {
                if (stationResizing || Detached || panelLayout.ReadOnly || (vertical ? e.Key is not (VirtualKey.Up or VirtualKey.Down) : e.Key is not (VirtualKey.Left or VirtualKey.Right))) return;
                e.Handled = true; BeginStationResize(0, edge);
                int step = (int)Math.Round(20 * display.Dpi / 96d);
                MoveStationResize(e.Key is VirtualKey.Left or VirtualKey.Up ? -step : step); EndStationResize(false);
            };
        }
        shell.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (!stationResizing || e.Key != VirtualKey.Escape) return;
            e.Handled = true; stationResizeGrips[resizeEdge].CancelDrag(); EndStationResize(true);
        }), true);
    }
    void BeginStationResize(int cursor, StationEdge edge)
    {
        if (stationResizing || Detached || destroyed || busy || ModalActive || panelLayout.ReadOnly) return;
        stationResizing = true; stationResizeLease = KeepOpen(); leaveTimer.Stop(); openedForDrop = false;
        resizeBefore = dockLayout; resizeEdge = edge; resizeCursor = cursor;
        resizeStart = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
    }
    void MoveStationResize(int cursor)
    {
        if (!stationResizing) return;
        double scale = display.Dpi / 96d; long delta = (long)cursor - resizeCursor; var work = display.Info.Work;
        if (resizeEdge == StationEdge.Left)
            dockLayout = dockLayout with { Width = Math.Clamp((resizeStart.Width - delta) / scale, StationPanelPolicy.MinimumWidth, StationPanelPolicy.MaximumWidth) };
        else if (resizeEdge == StationEdge.Bottom)
            dockLayout = dockLayout with { Height = Math.Clamp((resizeStart.Height + delta) / scale, StationPanelPolicy.MinimumHeight, StationPanelPolicy.MaximumHeight) };
        else if (resizeEdge == StationEdge.Right)
        {
            long first = resizeStart.X + Math.Min(resizeStart.Width, (long)Math.Round(StationPanelPolicy.MinimumWidth * scale));
            long last = Math.Max(first, Math.Min(work.Right, resizeStart.X + (long)Math.Round(StationPanelPolicy.MaximumWidth * scale)));
            long right = Math.Clamp(resizeStart.X + (long)resizeStart.Width + delta, first, last);
            dockLayout = dockLayout with { Width = Math.Clamp((right - resizeStart.X) / scale, StationPanelPolicy.MinimumWidth, StationPanelPolicy.MaximumWidth), RightOffset = Math.Max(0, (work.Right - right) / scale) };
        }
        else
        {
            long bottom = resizeStart.Y + (long)resizeStart.Height;
            long last = bottom - Math.Min(resizeStart.Height, (long)Math.Round(StationPanelPolicy.MinimumHeight * scale));
            long first = Math.Min(last, Math.Max(Math.Max(dockTop, work.Top), bottom - (long)Math.Round(StationPanelPolicy.MaximumHeight * scale)));
            long top = Math.Clamp(resizeStart.Y + delta, first, last);
            dockLayout = dockLayout with { Height = Math.Clamp((bottom - top) / scale, StationPanelPolicy.MinimumHeight, StationPanelPolicy.MaximumHeight), TopOffset = Math.Max(0, (top - Math.Max(dockTop, work.Top)) / scale) };
        }
        Position(dockTop);
    }
    void EndStationResize(bool cancel)
    {
        if (!stationResizing) return; stationResizing = false;
        try
        {
            if (!cancel) try { panelLayout.Save(dockLayout); } catch { cancel = true; status.Text = "中转站尺寸保存失败，已恢复原尺寸，请重试。"; }
            if (cancel) { dockLayout = resizeBefore; if (!destroyed && !Detached) Position(dockTop); }
        }
        finally { stationResizeLease?.Dispose(); stationResizeLease = null; }
    }
    void SaveFloatingLayout()
    {
        layoutTimer.Stop(); if (!Detached || destroyed) return;
        floatingBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        var value = new StationPanelRect(floatingBounds.Value.X, floatingBounds.Value.Y, floatingBounds.Value.Width, floatingBounds.Value.Height);
        dockLayout = dockLayout with { Floating = value };
        if (!panelLayout.ReadOnly) try { panelLayout.Save(panelLayout.Data with { Floating = value }); } catch { status.Text = "独立窗口尺寸保存失败，请重试。"; }
    }
}
