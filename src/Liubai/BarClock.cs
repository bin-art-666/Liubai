using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    ClockWindow? clockWindow;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? clockBadgeTimer;
    Microsoft.UI.Xaml.Shapes.Ellipse? clockDot;
    TextBlock? clockRemaining;
    string? clockTipText;
    void InitializeClockToolbar()
    {
        var icon = new Grid { Width = 20, Height = 20, IsHitTestVisible = false };
        icon.Children.Add(CreateVectorIcon(clockButton,
            "M 21 12 C 21 17 17 21 12 21 C 7 21 3 17 3 12 C 3 7 7 3 12 3 C 17 3 21 7 21 12 Z M 12 7 L 12 12 L 15.5 14", "1.4"));
        clockDot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
        clockDot.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.FillProperty, new Microsoft.UI.Xaml.Data.Binding { Source = clockButton, Path = new PropertyPath("Foreground") });
        icon.Children.Add(clockDot);
        // Center the visible numerals rather than the font's ascender/descender space.
        clockRemaining = new TextBlock { FontSize = preferences.ClockFontSize, TextLineBounds = TextLineBounds.Tight,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        clockRemaining.SetBinding(TextBlock.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding { Source = clockButton, Path = new PropertyPath("Foreground") });
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(clockRemaining, FontNumeralAlignment.Tabular);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, IsHitTestVisible = false };
        content.Children.Add(icon); content.Children.Add(clockRemaining); clockButton.Content = content;
        clockBadgeTimer = DispatcherQueue.CreateTimer(); clockBadgeTimer.Interval = TimeSpan.FromSeconds(1); clockBadgeTimer.Tick += (_, _) => UpdateClockButton();
        App.Clock.Changed += ClockChanged;
        preferences.ClockFontSizeChanged += ClockChanged;
        clockButton.Click += (_, _) =>
        {
            if (ToolbarClickBlocked || closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true) return;
            if (clockWindow?.IsOpen == true) clockWindow.Hide(); else OpenClock();
        };
        UpdateClockButton();
    }
    void ClockChanged() => DispatcherQueue.TryEnqueue(() => { if (!closing) UpdateClockButton(); });
    void UpdateClockButton()
    {
        if (closing || clockDot is null) return;
        var data = App.Clock.Data; bool active = App.Clock.HasPending;
        string label = ClockPolicy.ToolbarText(data, DateTimeOffset.Now, TimeZoneInfo.Local);
        bool showLabel = label.Length > 0;
        if (clockRemaining != null)
        {
            clockRemaining.Text = label;
            clockRemaining.FontSize = preferences.ClockFontSize;
            // Keep the width stable when seconds change, pause, or cross an hour.
            clockRemaining.Width = Math.Ceiling(preferences.ClockFontSize / 22d * (data.Countdown?.DurationSeconds >= 3600 ? 96 : 64));
            clockRemaining.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        }
        clockButton.Width = showLabel ? double.NaN : 34; clockButton.MinWidth = 34;
        clockButton.Padding = showLabel ? new Thickness(7, 0, 6, 0) : new Thickness(0);
        clockDot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        string text = "时钟 · 闹钟与倒计时";
        if (data.Countdown is { State: ClockState.Running or ClockState.Paused } item)
            text = (item.State == ClockState.Paused ? "计时已暂停 · " : "计时剩余 · ") + ClockPolicy.Format(ClockPolicy.Remaining(item, DateTimeOffset.Now, TimeZoneInfo.Local));
        else if (data.Alarms.Where(e => e.State == ClockState.Running).OrderBy(e => ClockPolicy.Due(e, TimeZoneInfo.Local)).FirstOrDefault() is { } alarm)
            text = "下个闹钟 · " + ClockPolicy.When(TimeZoneInfo.ConvertTime(ClockPolicy.Due(alarm, TimeZoneInfo.Local)!.Value, TimeZoneInfo.Local).DateTime, DateTimeOffset.Now, TimeZoneInfo.Local);
        if (text != clockTipText) { clockTipText = text; ToolTipService.SetToolTip(clockButton, text); Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(clockButton, text); }
        if (active && clockBadgeTimer?.IsRunning != true) clockBadgeTimer?.Start(); else if (!active) clockBadgeTimer?.Stop();
    }
    internal bool RequestClock(Guid id) => OpenClock(id);
    bool OpenClock(Guid? id = null)
    {
        if (closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true || !Native.MonitorExists(Display.Handle)) return false;
        App.Ai.Hide(false);
        settingsWindow?.Hide(); plannerWindow?.Hide(); qrResults?.Hide(); stationWindow?.HideDocked(); stationHoverTimer?.Stop(); manager?.HideOtherPanels(this);
        clockWindow ??= new ClockWindow(App.Clock, Display);
        clockWindow.Open(Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top, id);
        return true;
    }
    internal async Task<bool> ConfirmClockQuit()
    {
        if (!App.Clock.HasPending) return true;
        if (!OpenClock()) return false;
        return await clockWindow!.ConfirmQuit();
    }
}
