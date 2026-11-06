using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed class FocusWindow : UtilityPanel
{
    Guid taskId;
    readonly NumberBox minutes = new() { Header = "专注时长（分钟）", Minimum = 1, Maximum = 240, Value = 25, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    readonly TextBox resume = new() { PlaceholderText = "做到哪里了？下次从哪一步继续？", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxLength = 2000 };
    readonly Button start = new() { Content = "开始专注", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button pause = new() { Content = "暂停 / 继续", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button finish = new() { Content = "结束并保存接续记录", HorizontalAlignment = HorizontalAlignment.Stretch };
    bool operating;
    internal FocusWindow() : base("专注与接续")
    {
        start.Click += async (_, _) => await Operate(async () =>
        {
            var task = App.Planner.Data.Tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new IOException("待办已移除。");
            if (!double.IsFinite(minutes.Value) || minutes.Value < 1 || minutes.Value > 240) throw new IOException("时长为 1～240 分钟。");
            await App.Clock.StartCountdown((int)Math.Round(minutes.Value * 60), task.Title[..Math.Min(120, task.Title.Length)], DateTimeOffset.Now);
            var clock = App.Clock.Data.Countdown!;
            try { await App.Work.Store.SetFocus(new(task.Id, clock.Id, task.Title)); }
            catch { if (App.Clock.Data.Countdown?.Id == clock.Id) await App.Clock.CancelCountdown(); throw; }
            StatusLine.Text = "专注已开始，收起面板后继续计时。";
        });
        pause.Click += async (_, _) => await Operate(async () =>
        {
            if (!OwnCountdown()) throw new IOException("当前计时已变化，请重新打开面板。");
            if (App.Clock.Data.Countdown!.State == ClockState.Paused) await App.Clock.ResumeCountdown(DateTimeOffset.Now); else await App.Clock.PauseCountdown(DateTimeOffset.Now);
        });
        finish.Click += async (_, _) => await Operate(async () =>
        {
            if (OwnCountdown()) await App.Clock.CancelCountdown();
            await App.Work.Store.Resume(taskId, resume.Text); resume.Text = "";
            StatusLine.Text = "进展已保存在本机，待办状态未改变。";
        });
        App.Clock.Changed += Refresh; App.Work.Store.Changed += Refresh;
        Closed += (_, _) => { App.Clock.Changed -= Refresh; App.Work.Store.Changed -= Refresh; };
    }
    internal void Open(Guid id, DesktopMonitor display, int top)
    {
        taskId = id; Body.Children.Clear(); resume.Text = "";
        string title = App.Planner.Data.Tasks.FirstOrDefault(t => t.Id == id)?.Title ?? App.Work.Store.Data.Focus?.Title ?? "接续记录";
        Body.Children.Add(new TextBlock { Text = title, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        if (App.Work.Store.Data.Resumes.FirstOrDefault(r => r.TaskId == id) is { } previous)
            Body.Children.Add(Hint("上次进展 · " + previous.Updated.LocalDateTime.ToString("M/d HH:mm") + "\n" + previous.Text));
        Body.Children.Add(minutes); Body.Children.Add(start); Body.Children.Add(pause); Body.Children.Add(resume); Body.Children.Add(finish);
        Body.Children.Add(Hint("复用现有倒计时和提醒，已有倒计时时不会覆盖。顶部细线显示进度，点击任务标题可回来记录进展。")); Refresh(); Present(display, top, 620);
    }
    bool OwnCountdown() => App.Work.Store.Data.Focus is { } f && f.TaskId == taskId && App.Clock.Data.Countdown?.Id == f.ClockId;
    void Refresh()
    {
        start.IsEnabled = !operating && App.Clock.Data.Countdown?.State is not (ClockState.Running or ClockState.Paused);
        pause.IsEnabled = !operating && OwnCountdown() && App.Clock.Data.Countdown?.State is ClockState.Running or ClockState.Paused;
        pause.Content = App.Clock.Data.Countdown?.State == ClockState.Paused ? "继续专注" : "暂停专注"; finish.IsEnabled = !operating;
    }
    async Task Operate(Func<Task> action)
    {
        if (operating) return; operating = true; Refresh();
        try { await Guard(action); } finally { operating = false; Refresh(); }
    }
}
