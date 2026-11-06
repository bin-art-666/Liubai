using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

public sealed partial class BarWindow
{
    Button moreButton = null!;
    MoreWindow? moreWindow;
    ToolbarPlan toolbarPlan = new([], [], false);
    string[] toolbarOrder = ToolbarOrder.Normalize(null);
    bool toolbarLayoutQueued, layingOutToolbar;
    ElementTheme toolbarTheme;
    SolidColorBrush? toolbarInk;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? moreHover;

    void InitializeToolbarLayout()
    {
        moreButton = CreateToolbarButton("\uE712", "更多工具"); moreButton.Click += (_, _) => { if (!ToolbarClickBlocked) ToggleMore(); };
        foreach (var button in toolbarButtons.Values)
        {
            button.Click += (_, _) => moreWindow?.Hide();
            button.SizeChanged += (_, _) => QueueToolbarLayout();
        }
        surface.SizeChanged += (_, _) => { moreWindow?.Hide(); QueueToolbarLayout(); };
        focusEntry.SizeChanged += (_, _) => QueueToolbarLayout();
        App.ToolbarLayout.Changed += ToolbarLayoutChanged;
        moreHover = DispatcherQueue.CreateTimer(); moreHover.IsRepeating = false; moreHover.Interval = TimeSpan.FromMilliseconds(350);
        moreHover.Tick += (_, _) => { if (moreWindow?.IsOpen != true) OpenMore(false); };
        moreButton.AllowDrop = true;
        moreButton.DragOver += (_, e) =>
        {
            e.Handled = true; e.AcceptedOperation = DataPackageOperation.None;
            if (!closing && !PanelModal && DropPayload.Supported(e.DataView) && moreWindow?.IsOpen != true && moreHover?.IsRunning != true) moreHover?.Start();
            e.DragUIOverride.Caption = "悬停展开，再拖到目标工具"; e.DragUIOverride.IsCaptionVisible = true;
        };
        moreButton.DragLeave += (_, _) => moreHover?.Stop();
        QueueToolbarLayout();
    }
    void ToolbarLayoutChanged()
    {
        void Refresh() { if (closing) return; moreWindow?.Hide(); FinishToolbarDrag(false); ReflowToolbar(); }
        if (DispatcherQueue.HasThreadAccess) Refresh(); else DispatcherQueue.TryEnqueue(Refresh);
    }
    void QueueToolbarLayout()
    {
        if (closing || moreButton == null || toolbarLayoutQueued || moreWindow?.IsOpen == true) return;
        toolbarLayoutQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { toolbarLayoutQueued = false; ReflowToolbar(); });
    }
    void ReflowToolbar(double? widthOverride = null)
    {
        if (closing || moreButton == null || layingOutToolbar || moreWindow?.IsOpen == true) return;
        layingOutToolbar = true;
        try
        {
            var options = App.ToolbarLayout.Current;
            var widths = toolbarButtons.ToDictionary(p => p.Key, p => double.IsNaN(p.Value.Width)
                ? p.Key == "clock" && clockRemaining?.Visibility == Visibility.Visible ? 20 + 5 + clockRemaining.Width + 13 : 34 : p.Value.Width);
            double available = widthOverride ?? (surface.ActualWidth > 0 ? surface.ActualWidth : (Display.Info.Monitor.Right - Display.Info.Monitor.Left) / (Display.Dpi / 96d));
            available -= 16 + (focusEntry.Visibility == Visibility.Visible ? Math.Min(200, focusEntry.ActualWidth) : 0);
            if (preferences.BarHeight < 24) available = 34;
            toolbarPlan = ToolbarLayoutPolicy.Plan(toolbarOrder, options, widths, available);
            actions.Spacing = options.Spacing;
            foreach (string id in toolbarOrder)
            {
                var button = toolbarButtons[id];
                if (!actions.Children.Contains(button)) actions.Children.Add(button);
                button.Visibility = toolbarPlan.Visible.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
                button.Height = Math.Min(28, preferences.BarHeight);
                button.Margin = new Thickness(button.Visibility == Visibility.Visible && toolbarPlan.Visible.FirstOrDefault() != id && options.Entry(id).GroupStart ? options.GroupGap : 0, 0, 0, 0);
            }
            var expected = toolbarOrder.Select(id => toolbarButtons[id]).ToList();
            expected.Insert(expected.IndexOf(settings), moreButton);
            if (!actions.Children.Contains(moreButton)) actions.Children.Add(moreButton);
            for (int i = 0; i < expected.Count; i++) if (!ReferenceEquals(actions.Children[i], expected[i])) { actions.Children.Remove(expected[i]); actions.Children.Insert(i, expected[i]); }
            moreButton.Height = Math.Min(28, preferences.BarHeight); moreButton.Visibility = toolbarPlan.ShowMore ? Visibility.Visible : Visibility.Collapsed;
            SetToolbarColors(toolbarTheme, toolbarInk);
        }
        finally { layingOutToolbar = false; }
    }
    void ToggleMore() { if (moreWindow?.IsOpen == true) moreWindow.Hide(); else OpenMore(); }
    void OpenMore(bool activate = true)
    {
        if (closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true) return;
        HidePanels(false); manager?.HideOtherPanels(this); ReflowToolbar();
        EnsureMoreWindow().Open(toolbarPlan.More.Select(id => (id, toolbarButtons[id], App.ToolbarLayout.Current.Entry(id).GroupStart)), Display,
            windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top, activate);
    }
    MoreWindow EnsureMoreWindow() => moreWindow ??= new MoreWindow(button =>
        {
            button.Visibility = Visibility.Collapsed;
            actions.Children.Add(button);
            button.Height = Math.Min(28, preferences.BarHeight);
            QueueToolbarLayout();
        }, () => { UpdateClockButton(); QueueToolbarLayout(); }, button => toolMenus.TryGetValue(button, out var create) ? create() : null);
    void StopToolbarLayout()
    {
        App.ToolbarLayout.Changed -= ToolbarLayoutChanged; moreHover?.Stop();
    }
}
