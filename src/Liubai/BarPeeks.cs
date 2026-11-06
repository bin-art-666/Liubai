using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Liubai;

public sealed partial class BarWindow
{
    PeekWindow? peekWindow;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? holdTimer;
    Button? heldButton;
    Point heldPoint;
    bool peekFired;
    void InitializePeeks()
    {
        holdTimer = DispatcherQueue.CreateTimer(); holdTimer.IsRepeating = false; holdTimer.Interval = TimeSpan.FromMilliseconds(650);
        holdTimer.Tick += (_, _) => ShowHeldPreview();
        foreach (var button in ToolCatalog.BuiltIn.Items.Where(tool => tool.Has(ToolCapabilities.Peek)).Select(tool => toolbarButtons[tool.Id]))
        {
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                if (!ReferenceEquals(button.Parent, actions) || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) || ToolbarClickBlocked || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                FinishPeek(); heldButton = button; heldPoint = e.GetCurrentPoint(button).Position; holdTimer.Stop(); holdTimer.Start();
            }), true);
            button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
            {
                if (heldButton != button || peekFired) return; var point = e.GetCurrentPoint(button).Position;
                if (Math.Abs(point.X - heldPoint.X) > 8 || Math.Abs(point.Y - heldPoint.Y) > 8) FinishPeek();
            }), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => { if (heldButton == button) FinishPeek(); }), true);
            button.PointerCanceled += (_, _) => { if (heldButton == button) FinishPeek(); };
            button.PointerCaptureLost += (_, _) => { if (heldButton == button) FinishPeek(); };
        }
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated) FinishPeek(); };
    }
    void ShowHeldPreview()
    {
        if (heldButton == null || closing || PanelModal || manager?.HasModal == true || ToolbarClickBlocked) { FinishPeek(); return; }
        peekFired = tools?.Peek(toolIds[heldButton]) == true;
    }
    void FinishPeek()
    {
        holdTimer?.Stop(); heldButton = null;
        if (peekFired) suppressToolbarClickUntil = Environment.TickCount64 + 250;
        peekFired = false; peekWindow?.Hide();
    }
}
