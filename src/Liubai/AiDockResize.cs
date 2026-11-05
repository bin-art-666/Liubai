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

internal sealed partial class AiWindow
{
    enum DockEdge { Left, Top, Bottom }
    sealed class ResizeGrip : Grid
    {
        internal ResizeGrip(bool vertical) { ProtectedCursor = InputSystemCursor.Create(vertical ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast); }
    }
    readonly Thumb dockGrip = new(), dockTopGrip = new(), dockBottomGrip = new();
    readonly Grid dockGripHost = new ResizeGrip(false), dockTopGripHost = new ResizeGrip(true), dockBottomGripHost = new ResizeGrip(true);
    bool dockResizing;
    DockEdge dockEdge;
    double dockWidth = AiPolicy.DefaultDockWidth, dockHeight = AiPolicy.DefaultDockHeight, dockOffset;
    double dockWidthBefore, dockHeightBefore, dockOffsetBefore;
    AiBounds dockStartBounds = new(0, 0, 0, 0);
    int dockStartCursor;
    IDisposable? resizeLease;
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point position);
    void InitializeDockResize(Grid shell)
    {
        ConfigureGrip(shell, dockGripHost, dockGrip, DockEdge.Left, "调整 AI 面板宽度");
        ConfigureGrip(shell, dockTopGripHost, dockTopGrip, DockEdge.Top, "从上边缘调整 AI 面板高度");
        ConfigureGrip(shell, dockBottomGripHost, dockBottomGrip, DockEdge.Bottom, "从下边缘调整 AI 面板高度");
        shell.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (!dockResizing || e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            (dockEdge == DockEdge.Left ? dockGrip : dockEdge == DockEdge.Top ? dockTopGrip : dockBottomGrip).CancelDrag();
            EndDockResize(true);
        }), true);
    }
    void ConfigureGrip(Grid shell, Grid host, Thumb grip, DockEdge edge, string name)
    {
        bool vertical = edge != DockEdge.Left;
        host.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        host.VerticalAlignment = vertical ? edge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        if (vertical) { host.Height = 10; host.Margin = new Thickness(10, 0, 10, 0); } else host.Width = 10;
        grip.HorizontalAlignment = HorizontalAlignment.Stretch; grip.VerticalAlignment = VerticalAlignment.Stretch;
        grip.Background = new SolidColorBrush(Colors.Transparent); grip.BorderThickness = new Thickness(0); grip.IsTabStop = true;
        grip.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid Background=\"Transparent\"><Border Width=\"" + (vertical ? "32" : "2")
            + "\" Height=\"" + (vertical ? "2" : "32") + "\" CornerRadius=\"1\" Background=\"Gray\" Opacity=\"0.35\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Grid></ControlTemplate>");
        AutomationProperties.SetName(grip, name); ToolTipService.SetToolTip(grip, name + "（Esc 取消）");
        AutomationProperties.SetHelpText(grip, "拖动调整，松开自动保存；方向键微调，Esc 取消本次拖动。");
        host.Children.Add(grip); shell.Children.Add(host);
        grip.DragStarted += (_, _) => { if (GetCursorPos(out var point)) BeginDockResize(vertical ? point.Y : point.X, edge); };
        grip.DragDelta += (_, _) => { if (GetCursorPos(out var point)) MoveDockResize(vertical ? point.Y : point.X); };
        grip.DragCompleted += (_, e) => EndDockResize(e.Canceled);
        grip.KeyDown += (_, e) =>
        {
            if (dockResizing || Detached || options.ReadOnly || (vertical ? e.Key is not (VirtualKey.Up or VirtualKey.Down) : e.Key is not (VirtualKey.Left or VirtualKey.Right))) return;
            e.Handled = true; BeginDockResize(0, edge);
            int step = (int)Math.Round(20 * display.Dpi / 96d);
            MoveDockResize(e.Key is VirtualKey.Left or VirtualKey.Up ? -step : step); EndDockResize(false);
        };
    }
    void BeginDockResize(int cursor, DockEdge edge = DockEdge.Left)
    {
        if (Detached || destroyed || dockResizing || options.ReadOnly || ModalActive) return;
        dockResizing = true; resizeLease = KeepOpen(); dockEdge = edge;
        dockWidthBefore = dockWidth; dockHeightBefore = dockHeight; dockOffsetBefore = dockOffset;
        dockStartBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height); dockStartCursor = cursor;
    }
    void MoveDockResize(int cursor)
    {
        if (!dockResizing) return;
        double scale = display.Dpi / 96d;
        long delta = (long)cursor - dockStartCursor;
        if (dockEdge == DockEdge.Left)
            dockWidth = Math.Clamp((dockStartBounds.Width - delta) / scale, AiPolicy.MinimumDockWidth, AiPolicy.MaximumDockWidth);
        else if (dockEdge == DockEdge.Bottom)
            dockHeight = Math.Clamp((dockStartBounds.Height + delta) / scale, AiPolicy.MinimumDockHeight, AiPolicy.MaximumDockHeight);
        else
        {
            var work = display.Info.Work;
            long bottom = (long)dockStartBounds.Y + dockStartBounds.Height;
            long anchor = Math.Max(dockTop, work.Top);
            long latest = bottom - Math.Min((long)Math.Round(AiPolicy.MinimumDockHeight * scale), dockStartBounds.Height);
            long earliest = Math.Min(latest, Math.Max(anchor, bottom - (long)Math.Round(AiPolicy.MaximumDockHeight * scale)));
            long top = Math.Clamp((long)dockStartBounds.Y + delta, earliest, latest);
            dockHeight = Math.Clamp((bottom - top) / scale, AiPolicy.MinimumDockHeight, AiPolicy.MaximumDockHeight);
            dockOffset = Math.Max(0, (top - anchor) / scale);
        }
        Position();
    }
    void EndDockResize(bool cancel)
    {
        if (!dockResizing) return;
        dockResizing = false;
        try
        {
            if (!cancel && (Math.Abs(dockWidth - dockWidthBefore) > .01 || Math.Abs(dockHeight - dockHeightBefore) > .01 || Math.Abs(dockOffset - dockOffsetBefore) > .01))
            {
                try { options.Save(options.Data with { DockWidth = dockWidth, DockHeight = dockHeight, DockTopOffset = dockOffset }); }
                catch { cancel = true; SetStatus("面板尺寸保存失败，已恢复原尺寸，请重试。"); }
            }
            if (cancel)
            {
                dockWidth = dockWidthBefore; dockHeight = dockHeightBefore; dockOffset = dockOffsetBefore;
                if (!destroyed && !Detached) Position();
            }
        }
        finally { resizeLease?.Dispose(); resizeLease = null; }
    }
}
