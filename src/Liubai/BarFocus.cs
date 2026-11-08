using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    readonly TextBlock focusTitle = new() { MaxWidth = 200, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    readonly Button focusEntry = new() { Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 0, 10, 0), MinHeight = 0, MinWidth = 0, Visibility = Visibility.Collapsed };
    readonly Border focusProgress = new() { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    bool focusReady;
    void InitializeFocusBar(Grid layers)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        surface.Child = null; focusEntry.Content = focusTitle; Grid.SetColumn(focusEntry, 1); row.Children.Add(focusEntry); Grid.SetColumn(actions, 2); row.Children.Add(actions); surface.Child = row;
        focusEntry.SetBinding(Control.ForegroundProperty, new Binding { Source = clockButton, Path = new PropertyPath("Foreground") });
        focusProgress.SetBinding(Border.BackgroundProperty, new Binding { Source = clockButton, Path = new PropertyPath("Foreground") }); layers.Children.Add(focusProgress);
        focusEntry.Click += (_, _) => { if (!ToolbarClickBlocked && App.WorkStore.Data.Focus is { } focus) OpenFocus(focus.TaskId, Display.Device); };
        surface.SizeChanged += (_, _) => RefreshFocusBar();
        App.FocusRequested += OpenFocus; App.WorkspaceRequested += OpenWorkspace; App.WorkStore.Changed += RefreshFocusBar;
        focusReady = true; RefreshFocusBar();
    }
    void OpenFocus(Guid task, string device)
    {
        if (device != Display.Device || closing || PanelModal || manager?.HasModal == true) return;
        HidePanels(false); manager?.HideOtherPanels(this); focusWindow ??= new FocusWindow();
        focusWindow.Open(task, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    void OpenWorkspace(Guid group,string device)
    {
        if(device!=Display.Device || closing || PanelModal || manager?.HasModal==true) return;
        if(OpenStation()) stationWindow!.OpenWorkspace(group);
    }
    void RefreshFocusBar()
    {
        if (!focusReady || closing) return;
        var focus = App.WorkStore.Data.Focus; var clock = App.Clock.Data.Countdown;
        bool visible = focus != null && clock?.Id == focus.ClockId && clock.State is ClockState.Running or ClockState.Paused or ClockState.Finished;
        focusEntry.Visibility = visible && surface.ActualHeight >= 18 ? Visibility.Visible : Visibility.Collapsed;
        focusProgress.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        focusEntry.RequestedTheme = clockButton.RequestedTheme;
        focusTitle.Text = (clock!.State == ClockState.Finished ? "已结束 · " : clock.State == ClockState.Paused ? "暂停 · " : "") + focus!.Title;
        ToolTipService.SetToolTip(focusEntry, focus.Title + "\n点击记录进展 / 暂停或继续");
        focusProgress.Height = Math.Min(2, surface.ActualHeight);
        double remaining = ClockPolicy.Remaining(clock, DateTimeOffset.Now, TimeZoneInfo.Local).TotalSeconds;
        double progress = clock.State == ClockState.Finished ? 1 : Math.Clamp(1 - remaining / clock.DurationSeconds, 0, 1);
        focusProgress.Width = Math.Max(0, surface.ActualWidth * progress);
    }
    void StopFocusBar()
    {
        App.FocusRequested -= OpenFocus; App.WorkspaceRequested -= OpenWorkspace; App.WorkStore.Changed -= RefreshFocusBar;
    }
}
