using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Liubai;

internal sealed class TaskScheduleEditor
{
    internal StackPanel Root { get; } = new() { Spacing = 8 };
    internal CalendarDatePicker Date { get; } = new() { Header = "日期（可选）", PlaceholderText = "不安排日期", HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TimePicker Time { get; } = new() { Header = "时间（可选）", ClockIdentifier = "24HourClock", SelectedTime = null, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal ComboBox Reminder { get; } = new() { Header = "提醒", HorizontalAlignment = HorizontalAlignment.Stretch };
    bool syncing;
    internal event Action? Changed;
    internal DateOnly? DateValue => Date.Date is { } date ? DateOnly.FromDateTime(date.Date) : null;
    internal TimeOnly? TimeValue => DateValue != null && Time.SelectedTime is { } time ? TimeOnly.FromTimeSpan(time) : null;
    internal int? LeadValue => TimeValue != null && Reminder.SelectedIndex >= 0 ? ReminderPolicy.Leads[Reminder.SelectedIndex] : null;
    internal TaskScheduleEditor()
    {
        AutomationProperties.SetName(Date, "待办日期"); AutomationProperties.SetName(Time, "待办时间"); AutomationProperties.SetName(Reminder, "提醒时间");
        foreach (string label in new[] { "不提醒", "准时提醒", "提前 5 分钟", "提前 10 分钟", "提前 15 分钟", "提前 30 分钟", "提前 1 小时", "提前 1 天" }) Reminder.Items.Add(label);
        Reminder.SelectedIndex = 0; Reminder.IsEnabled = false;
        Root.Children.Add(Date); Root.Children.Add(Time); Root.Children.Add(Reminder);
        var clear = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var clearDate = new Button { Content = "清除日期" }; clearDate.Click += (_, _) => Date.Date = null;
        var clearTime = new Button { Content = "不设时间" }; clearTime.Click += (_, _) => Time.SelectedTime = null;
        clear.Children.Add(clearDate); clear.Children.Add(clearTime); Root.Children.Add(clear);
        Root.Children.Add(new TextBlock { Text = "提醒需保持留白运行。退出或睡眠期间错过的提醒，会在下次启动或唤醒后补提示。", FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        Date.DateChanged += (_, _) => Update(false);
        Time.SelectedTimeChanged += (_, _) => Update(true);
        Reminder.SelectionChanged += (_, _) => { if (!syncing) Changed?.Invoke(); };
    }
    void Update(bool fromTime)
    {
        if (syncing) return; syncing = true;
        if (fromTime && Time.SelectedTime != null && Date.Date == null) Date.Date = DateTimeOffset.Now.Date;
        if (Date.Date == null) Time.SelectedTime = null;
        Reminder.IsEnabled = Date.Date != null && Time.SelectedTime != null;
        if (!Reminder.IsEnabled) Reminder.SelectedIndex = 0;
        syncing = false; Changed?.Invoke();
    }
    internal void Load(DateOnly? date, TimeOnly? time, int? lead)
    {
        syncing = true;
        Date.Date = date is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue)) : null;
        Time.SelectedTime = time?.ToTimeSpan(); Reminder.SelectedIndex = Array.IndexOf(ReminderPolicy.Leads, lead);
        Reminder.IsEnabled = date != null && time != null; syncing = false;
    }
}
