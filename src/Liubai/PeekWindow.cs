using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed class PeekWindow : UtilityPanel
{
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer tick;
    string kind = "";
    internal PeekWindow() : base("快速看一眼")
    {
        Body.IsHitTestVisible = false;
        tick = DispatcherQueue.CreateTimer(); tick.Interval = TimeSpan.FromSeconds(1); tick.Tick += (_, _) => Refresh();
        Closed += (_, _) => tick.Stop();
    }
    internal void Open(string type, DesktopMonitor display, int top)
    {
        kind = type; Refresh(); Present(display, top, 390, activate: false); if (kind == "clock") tick.Start();
    }
    void Refresh()
    {
        Body.Children.Clear();
        if (kind == "clock")
        {
            if (App.Clock.Data.Countdown is { State: ClockState.Running or ClockState.Paused } timer)
            {
                Body.Children.Add(new TextBlock { Text = ClockPolicy.Format(ClockPolicy.Remaining(timer, DateTimeOffset.Now, TimeZoneInfo.Local)), FontSize = 30 });
                Body.Children.Add(Hint((timer.State == ClockState.Paused ? "已暂停 · " : "倒计时 · ") + timer.Title));
            }
            else
            {
                var alarms = App.Clock.Data.Alarms.Where(a => a.State == ClockState.Running).Take(5).ToArray();
                Body.Children.Add(Hint(alarms.Length == 0 ? "没有活动计时或闹钟" : "接下来的闹钟"));
                foreach (var alarm in alarms) Body.Children.Add(new TextBlock { Text = alarm.LocalAlarm?.ToString("HH:mm") + " · " + alarm.Title, TextWrapping = TextWrapping.Wrap });
            }
        }
        else if (kind == "todo")
        {
            var tasks = PlannerQueries.Tasks(App.Planner.Data, TodoView.Today, "", DateOnly.FromDateTime(DateTime.Now)).Take(6).ToArray();
            Body.Children.Add(Hint(tasks.Length == 0 ? "今天没有安排的待办" : "今天 · 含逾期"));
            foreach (var task in tasks) Body.Children.Add(new TextBlock { Text = "○  " + task.Title, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            var notes = PlannerQueries.Notes(App.Planner.Data, "").Take(4).ToArray();
            Body.Children.Add(Hint(notes.Length == 0 ? "还没有随手记" : "最近的随手记"));
            foreach (var note in notes) Body.Children.Add(new TextBlock { Text = note.Text.Length > 100 ? note.Text[..100] + "…" : note.Text.Length == 0 ? "图文记录" : note.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 3 });
        }
        StatusLine.Text = "松开图标即收起 · 点击仍正常打开 · 此处只预览";
    }
    protected override void OnHiding() => tick.Stop();
}
