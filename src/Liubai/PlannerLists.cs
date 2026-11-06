using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class PlannerWindow
{
    void RefreshLists()
    {
        if (destroyed) return;
        taskList.Children.Clear(); noteList.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var view = taskFilter.SelectedIndex >= 0 ? (TodoView)taskFilter.SelectedIndex : TodoView.Today;
        var tasks = PlannerQueries.Tasks(store.Data, view, taskSearch.Text, today).ToArray();
        var notes = PlannerQueries.Notes(store.Data, noteSearch.Text).ToArray();
        taskList.Children.Add(Hint(tasks.Length == 0 ? (taskSearch.Text.Length > 0 ? "没有匹配的待办" : view switch
        { TodoView.Today => "今天没有安排。可以给任务设置日期，或到全部待办查看。", TodoView.Planned => "之后的日子还没有安排", TodoView.Completed => "完成的任务会出现在这里", _ => "还没有待办，先添加一件小事吧" }) : $"{tasks.Length} 项"));
        string? group = null;
        foreach (var item in tasks.Take(taskLimit))
        {
            string label = item.Date is { } date ? date < today && !item.Completed ? "逾期 · " + date.ToString("M月d日")
                : date == today ? "今天" : date.ToString("yyyy年M月d日") : "未安排日期";
            if (view != TodoView.Completed && label != group) { taskList.Children.Add(Hint(label)); group = label; }
            var row = Row();
            var summary = new StackPanel { Spacing = 3 };
            summary.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, Opacity = item.Completed ? .6 : 1 });
            if (item.Time is { } time)
            {
                string reminder = item.RemindBefore is { } minutes ? minutes == 0 ? " · 准时提醒" : $" · 提前 {minutes} 分钟提醒" : "";
                if (item.SnoozeUntil is { } snooze && item.NotifiedAt == null) reminder = " · 稍后提醒 " + snooze.LocalDateTime.ToString("M/d HH:mm");
                else if (item.NotifiedAt != null) reminder += " · 已提醒";
                summary.Children.Add(Hint(time.ToString("HH:mm") + reminder));
            }
            if (!string.IsNullOrWhiteSpace(item.Detail)) summary.Children.Add(Hint(Preview(item.Detail, 65)));
            if (item.SourceNote != null) summary.Children.Add(Hint("来自随手记"));
            var done = new CheckBox { Content = summary, IsChecked = item.Completed, HorizontalAlignment = HorizontalAlignment.Stretch };
            Name(done, (item.Completed ? "恢复待办：" : "完成待办：") + item.Title);
            done.Click += async (_, _) => await Run(async () => { await store.CompleteTask(item.Id, done.IsChecked == true); status.Text = done.IsChecked == true ? "已完成" : "已恢复为待办"; });
            row.Children.Add(done);
            var focus = IconButton("\uE916", "开始专注：" + item.Title); focus.IsEnabled = !item.Completed;
            focus.Click += (_, _) => App.RequestFocus(item.Id, display.Device); Grid.SetColumn(focus, 1); row.Children.Add(focus);
            var edit = IconButton("\uE70F", "编辑待办：" + item.Title); Grid.SetColumn(edit, 2); edit.Click += async (_, _) => await Run(() => EditTask(item)); row.Children.Add(edit);
            var delete = IconButton("\uE74D", "删除待办：" + item.Title); Grid.SetColumn(delete, 3);
            delete.Click += async (_, _) => await Run(async () => { await store.DeleteTask(item.Id); SetUndo(() => store.RestoreTask(item)); }); row.Children.Add(delete);
            taskList.Children.Add(Card(row));
        }
        if (tasks.Length > taskLimit)
        {
            var more = new Button { Content = "加载更多待办", HorizontalAlignment = HorizontalAlignment.Stretch };
            more.Click += (_, _) => { taskLimit += 50; RefreshLists(); }; taskList.Children.Add(more);
        }
        noteList.Children.Add(Hint(notes.Length == 0 ? (noteSearch.Text.Length > 0 ? "没有匹配的记录" : "写下的内容会自动出现在这里") : $"{notes.Length} 条记录"));
        foreach (var note in notes.Take(noteLimit))
        {
            var row = Row();
            var summary = new StackPanel { Spacing = 4 };
            int imageCount = App.Work.Store.Data.Images.Count(i => i.NoteId == note.Id);
            summary.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(note.Text) ? imageCount > 0 ? "图文记录" : "空白记录" : Preview(note.Text, 90), TextWrapping = TextWrapping.Wrap });
            if (imageCount > 0) summary.Children.Add(Hint($"{imageCount} 张图片 · 已保存在本机"));
            summary.Children.Add(Hint((note.Pinned ? "已置顶 · " : "") + note.Updated.LocalDateTime.ToString("M月d日 HH:mm")));
            var select = new Button { Content = summary, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
            Name(select, "打开记录：" + Preview(note.Text, 35));
            select.Click += async (_, _) => await Run(async () =>
            {
                if (!await FlushAsync()) return;
                await store.ActivateNote(note.Id); LoadNote(note.Id); status.Text = "正在编辑记录 · 修改会自动保存";
                noteEditor.Focus(FocusState.Programmatic);
            }); row.Children.Add(select);
            var pin = IconButton(note.Pinned ? "\uE77A" : "\uE718", note.Pinned ? "取消置顶记录" : "置顶记录"); Grid.SetColumn(pin, 1);
            pin.Click += async (_, _) => await Run(async () => { await store.PinNote(note.Id, !note.Pinned); status.Text = note.Pinned ? "已取消置顶" : "已置顶"; }); row.Children.Add(pin);
            var delete = IconButton("\uE74D", "删除记录：" + Preview(note.Text, 30)); Grid.SetColumn(delete, 2);
            delete.Click += async (_, _) => await Run(async () =>
            {
                if (!await FlushAsync()) return;
                var latest = store.Data.Notes.Single(n => n.Id == note.Id);
                await store.DeleteNote(note.Id);
                if (noteId == note.Id) LoadNote(null);
                SetUndo(() => store.RestoreNote(latest));
            }); row.Children.Add(delete);
            noteList.Children.Add(Card(row));
        }
        if (notes.Length > noteLimit)
        {
            var more = new Button { Content = "加载更多记录", HorizontalAlignment = HorizontalAlignment.Stretch };
            more.Click += (_, _) => { noteLimit += 50; RefreshLists(); }; noteList.Children.Add(more);
        }
    }
    static string Preview(string value, int length)
    { string text = value.Replace('\r', ' ').Replace('\n', ' ').Trim(); return text.Length <= length ? text : text[..length] + "…"; }
    static Grid Row()
    {
        var row = new Grid { ColumnSpacing = 3 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); return row;
    }
    static Border Card(UIElement content) => PanelTheme.Card(content);
}
