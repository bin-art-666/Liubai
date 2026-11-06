using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Liubai;

public sealed partial class BarWindow
{
    readonly Dictionary<string, Button> toolbarButtons = new(StringComparer.Ordinal);
    Button? draggedToolbarButton;
    string[]? toolbarBeforeDrag;
    Point toolbarPressPoint;
    Border? toolbarMarker;
    Grid? toolbarLayers;
    bool toolbarMoved;
    long suppressToolbarClickUntil;
    bool ToolbarClickBlocked => peekFired || draggedToolbarButton != null || Environment.TickCount64 < suppressToolbarClickUntil
        || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    void InitializeToolbarReorder(Grid layers)
    {
        toolbarLayers = layers;
        Button[] buttons = [screenshot, qrScan, clockButton, todo, quickNote, shelf, guardButton, privacyButton, aiButton, settings];
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i]; toolbarButtons.Add(ToolbarOrder.Defaults[i], button);
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => ToolbarPressed(button, e)), true);
        }
        toolbarMarker = new Border { Width = 2, Height = 18, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        layers.Children.Add(toolbarMarker);
        actions.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (draggedToolbarButton == null) return;
            e.Handled = true; MoveToolbarDrag(e.GetCurrentPoint(actions).Position, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control));
        }), true);
        actions.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (draggedToolbarButton == null) return;
            e.Handled = true;
            var point = e.GetCurrentPoint(actions).Position;
            FinishToolbarDrag(e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) && InsideToolbar(point));
        }), true);
        actions.PointerCanceled += (_, _) => FinishToolbarDrag(false);
        actions.PointerCaptureLost += (_, _) => FinishToolbarDrag(false);
        layers.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        { if (draggedToolbarButton != null && e.Key == VirtualKey.Escape) { e.Handled = true; FinishToolbarDrag(false); } }), true);
        layers.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, e) =>
        { if (draggedToolbarButton != null && e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl) FinishToolbarDrag(false); }), true);
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) FinishToolbarDrag(false); };
        preferences.ToolbarOrderChanged += ToolbarOrderChanged;
        ApplyToolbarOrder(preferences.ToolbarItems);
    }
    void ToolbarPressed(Button button, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        e.Handled = true; suppressToolbarClickUntil = Environment.TickCount64 + 250;
        if (closing || PanelModal || manager?.HasModal == true || draggedToolbarButton != null) return;
        button.ReleasePointerCaptures();
        if (!actions.CapturePointer(e.Pointer)) return;
        BeginToolbarDrag(button, e.GetCurrentPoint(actions).Position);
    }
    void BeginToolbarDrag(Button button, Point point)
    {
        HidePanels(false); manager?.HideOtherPanels(this);
        draggedToolbarButton = button; toolbarBeforeDrag = CurrentToolbarOrder(); toolbarPressPoint = point; toolbarMoved = false;
        button.Opacity = .55; screenshotTip.IsOpen = false; if (qrTip != null) qrTip.IsOpen = false;
        button.Focus(FocusState.Programmatic);
    }
    string[] CurrentToolbarOrder() => actions.Children.OfType<Button>().Select(b => toolbarButtons.First(p => ReferenceEquals(p.Value, b)).Key).ToArray();
    bool InsideToolbar(Point point) => point.X >= 0 && point.X <= actions.ActualWidth && point.Y >= 0 && point.Y <= actions.ActualHeight;
    void MoveToolbarDrag(Point point, bool controlHeld)
    {
        if (draggedToolbarButton is not { } button) return;
        if (!controlHeld) { FinishToolbarDrag(false); return; }
        if (!toolbarMoved && Math.Abs(point.X - toolbarPressPoint.X) < 4 && Math.Abs(point.Y - toolbarPressPoint.Y) < 4) return;
        toolbarMoved = true;
        int index = actions.Children.OfType<Button>().Where(b => !ReferenceEquals(b, button)).Count(b =>
        {
            double left = b.TransformToVisual(actions).TransformPoint(new Point()).X;
            return point.X > left + b.ActualWidth / 2;
        });
        string id = toolbarButtons.First(p => ReferenceEquals(p.Value, button)).Key;
        var current = CurrentToolbarOrder(); var order = ToolbarOrder.Move(current, id, index);
        if (!order.SequenceEqual(current)) { ApplyToolbarOrder(order); actions.UpdateLayout(); button.Focus(FocusState.Programmatic); }
        if (toolbarMarker != null && toolbarLayers != null)
        {
            toolbarMarker.Background = button.Foreground;
            toolbarMarker.Height = Math.Min(18, Math.Max(2, actions.ActualHeight - 4));
            toolbarMarker.Margin = new Thickness(button.TransformToVisual(toolbarLayers).TransformPoint(new Point()).X - 2, 0, 0, 0);
            toolbarMarker.Visibility = Visibility.Visible;
        }
    }
    void FinishToolbarDrag(bool commit)
    {
        if (draggedToolbarButton is not { } button || toolbarBeforeDrag is not { } original) return;
        var result = CurrentToolbarOrder(); bool moved = toolbarMoved;
        draggedToolbarButton = null; toolbarBeforeDrag = null; toolbarMoved = false;
        suppressToolbarClickUntil = Environment.TickCount64 + 250;
        button.Opacity = 1; if (toolbarMarker != null) toolbarMarker.Visibility = Visibility.Collapsed;
        actions.ReleasePointerCaptures();
        if (!commit || !moved) { ApplyToolbarOrder(original); return; }
        if (result.SequenceEqual(original)) return;
        try { preferences.SaveToolbarOrder(result); }
        catch
        {
            ApplyToolbarOrder(original);
            if (!closing) { OpenSettings(); settingsWindow?.ShowSaveError(); }
        }
    }
    void ToolbarOrderChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (closing) return;
        FinishToolbarDrag(false); ApplyToolbarOrder(preferences.ToolbarItems);
    });
    void ApplyToolbarOrder(IEnumerable<string> order)
    {
        var expected = ToolbarOrder.Normalize(order).Select(id => toolbarButtons[id]).ToArray();
        for (int i = 0; i < expected.Length; i++)
        {
            var button = expected[i]; if (ReferenceEquals(actions.Children[i], button)) continue;
            actions.Children.Remove(button); actions.Children.Insert(i, button);
        }
    }
}
