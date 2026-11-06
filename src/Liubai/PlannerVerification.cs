using Microsoft.UI.Xaml;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyPlanner()
    {
        string result = "PASS", error = "";
        var checks = new List<string>();
        try
        {
            await Task.Delay(400);
            plannerWindow = new PlannerWindow(App.Planner, Display);
            await plannerWindow.Open(PlannerPage.Tasks, Display, AppWindow.Position.Y + AppWindow.Size.Height);
            await Task.Delay(250);
            await plannerWindow.VerifyEditors(checks);
            Activate();
            await Task.Delay(350);
            if (plannerWindow.IsOpen) throw new InvalidOperationException("Planner did not dismiss when focus moved outside.");
            await plannerWindow.FlushAsync();
            await plannerWindow.VerifyReleasedView();
            checks.Add("hidden planner releases rendered lists and attachment images after saving");
            checks.Add("outside-focus-dismiss-and-save");
            await plannerWindow.Open(PlannerPage.Notes, Display, AppWindow.Position.Y + AppWindow.Size.Height);
            OpenSettings(); await Task.Delay(200);
            if (plannerWindow.IsOpen || settingsWindow?.IsOpen != true) throw new InvalidOperationException("Settings and planner were open together.");
            settingsWindow.Hide(); checks.Add("one-panel-at-a-time");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            if (plannerWindow != null) await plannerWindow.FlushAsync();
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "planner-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Quit();
        }
    }
}

internal sealed partial class PlannerWindow
{
    internal async Task VerifyEditors(List<string> checks)
    {
        static async Task Until(Func<bool> predicate)
        {
            long deadline = Environment.TickCount64 + 3000;
            while (!predicate() && Environment.TickCount64 < deadline) await Task.Delay(40);
        }
        taskTitle.Text = "完成第一版待办"; taskDetail.Text = "中文备注与日期";
        taskDate.Date = CalendarDate(DateOnly.FromDateTime(DateTime.Now));
        taskSchedule.Time.SelectedTime = new TimeSpan(15, 30, 0); taskSchedule.Reminder.SelectedIndex = 3;
        await Until(() => store.Data.Draft.Title == taskTitle.Text && store.Data.Draft.Date != null && store.Data.Draft.Time == new TimeOnly(15, 30));
        var restored = new PlannerStore(Path.Combine(App.DataDirectory, "planner.json"));
        if (restored.Data.Draft.Title != taskTitle.Text || restored.Data.Draft.Date == null
            || restored.Data.Draft.Time != new TimeOnly(15, 30) || restored.Data.Draft.RemindBefore != 10)
            throw new InvalidOperationException("Task draft autosave failed.");
        checks.Add("task-draft-autosave-and-reload");
        await AddTask();
        if (store.Data.Tasks.Length != 1 || taskTitle.Text.Length != 0 || store.Data.Draft.Title.Length != 0)
            throw new InvalidOperationException("Task composer did not commit and clear.");
        if (store.Data.Tasks[0].Time != new TimeOnly(15, 30) || store.Data.Tasks[0].RemindBefore != 10 || taskSchedule.Time.SelectedTime != null)
            throw new InvalidOperationException("Reminder fields did not commit and clear with the task.");
        checks.Add("task-add-time-reminder-and-clear");
        await SwitchPage(PlannerPage.Notes);
        noteEditor.Text = "记录第一行\n下一步可以转为任务";
        await Until(() => store.Data.Notes.Length == 1 && store.Data.Notes[0].Text == noteEditor.Text);
        if (store.Data.Notes.Single().Text != noteEditor.Text) throw new InvalidOperationException("Note autosave failed.");
        Guid id = noteId!.Value;
        await store.PinNote(id, true);
        noteEditor.Text += "\n置顶后继续编辑";
        if (!await FlushAsync() || !store.Data.Notes.Single().Pinned || !store.Data.Notes.Single().Text.Contains("置顶后继续编辑"))
            throw new InvalidOperationException("Immediate flush lost the latest edit or pin state.");
        checks.Add("note-autosave-and-pin-preservation");
        var note = store.Data.Notes.Single(); await store.DeleteNote(id); LoadNote(null);
        SetUndo(() => store.RestoreNote(note)); await undo!(); undo = null; undoButton.Visibility = Visibility.Collapsed;
        if (store.Data.Notes.Length != 1) throw new InvalidOperationException("Undo did not restore the note.");
        LoadNote(id);
        Guid task = await store.ConvertNote(id);
        if (await store.ConvertNote(id) != task || store.Data.Notes.Length != 1) throw new InvalidOperationException("Note conversion was destructive or duplicated.");
        checks.Add("note-delete-undo-and-linked-task");
        taskSearch.Text = "不存在的关键字"; noteSearch.Text = "不存在的关键字";
        await Task.Delay(100); // TextChanged is dispatched after the XAML property update.
        if (taskList.Children.Count != 1 || noteList.Children.Count != 1) throw new InvalidOperationException("Search did not filter rendered lists.");
        taskSearch.Text = noteSearch.Text = "";
        checks.Add("search-rendering");
        noteEditor.Text += "\n收起前还没停笔的内容";
    }
}
