using System.Text.Json;

namespace Liubai;

internal sealed record TodoItem(Guid Id, string Title, string Detail, DateOnly? Date, bool Completed,
    DateTimeOffset Created, DateTimeOffset Updated, Guid? SourceNote = null, TimeOnly? Time = null, int? RemindBefore = null,
    Guid ReminderToken = default, DateTimeOffset? NotifiedAt = null, DateTimeOffset? SnoozeUntil = null);
internal sealed record QuickNote(Guid Id, string Text, bool Pinned, DateTimeOffset Created, DateTimeOffset Updated);
internal sealed record TodoDraft(string Title = "", string Detail = "", DateOnly? Date = null, TimeOnly? Time = null, int? RemindBefore = null);
internal sealed record PlannerDocument(int Version, TodoItem[] Tasks, QuickNote[] Notes, TodoDraft Draft, Guid? ActiveNote = null)
{
    internal static PlannerDocument Empty => new(2, [], [], new());
}
internal enum TodoView { Today, Planned, All, Completed }

internal static class PlannerQueries
{
    internal static IEnumerable<TodoItem> Tasks(PlannerDocument document, TodoView view, string search, DateOnly today)
    {
        var items = document.Tasks.Where(t => view switch
        {
            TodoView.Today => !t.Completed && t.Date is { } date && date <= today,
            TodoView.Planned => !t.Completed && t.Date > today,
            TodoView.Completed => t.Completed,
            _ => !t.Completed
        }).Where(t => string.IsNullOrWhiteSpace(search) || (t.Title + "\n" + t.Detail).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        return view == TodoView.Completed ? items.OrderByDescending(t => t.Updated)
            : items.OrderBy(t => t.Date == null).ThenBy(t => t.Date).ThenBy(t => t.Time).ThenBy(t => t.Created);
    }
    internal static IEnumerable<QuickNote> Notes(PlannerDocument document, string search) => document.Notes
        .Where(n => string.IsNullOrWhiteSpace(search) || n.Text.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated);
}

// One shared store serializes writes from every display. Live state changes only
// after atomic disk replacement succeeds; appearance reset/import never touches it.
internal sealed class PlannerStore
{
    readonly string path;
    readonly SemaphoreSlim gate = new(1, 1);
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };
    PlannerDocument data = PlannerDocument.Empty;
    bool preserveBackup;
    internal PlannerDocument Data => data;
    internal string? LoadWarning { get; }
    internal bool ReadOnly { get; }
    internal event Action? Changed;

    internal PlannerStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { data = Read(path); }
        catch (NotSupportedException)
        {
            ReadOnly = true; LoadWarning = "此数据由更新版本创建，已停止写入。请使用对应版本打开。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            try
            {
                data = Read(path + ".bak"); preserveBackup = true;
                LoadWarning = "已从备份恢复内容。";
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
            {
                ReadOnly = true;
                LoadWarning = "数据无法读取，已停止写入以保护原文件。请保留 planner.json 和备份后再恢复。";
            }
        }
    }
    static PlannerDocument Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("数据文件过大。");
        var document = JsonSerializer.Deserialize<PlannerDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("数据为空。");
        if (document.Version == 1) document = document with { Version = 2 };
        if (document.Version != 2) throw new NotSupportedException("不支持的数据版本。");
        Validate(document); return document;
    }
    static void Validate(PlannerDocument value)
    {
        if (value.Version != 2 || value.Tasks == null || value.Notes == null || value.Draft == null
            || value.Tasks.Length > 10000 || value.Notes.Length > 10000
            || value.Tasks.Any(t => t == null || t.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Title)
                || t.Title.Length > 200 || t.Detail == null || t.Detail.Length > 20000
                || !ReminderPolicy.ValidSchedule(t.Date, t.Time, t.RemindBefore)
                || t.RemindBefore != null && t.ReminderToken == Guid.Empty)
            || value.Notes.Any(n => n == null || n.Id == Guid.Empty || n.Text == null || n.Text.Length > 20000)
            || value.Tasks.Select(t => t.Id).Distinct().Count() != value.Tasks.Length
            || value.Notes.Select(n => n.Id).Distinct().Count() != value.Notes.Length
            || value.Draft.Title == null || value.Draft.Title.Length > 200 || value.Draft.Detail == null || value.Draft.Detail.Length > 20000
            || !ReminderPolicy.ValidSchedule(value.Draft.Date, value.Draft.Time, value.Draft.RemindBefore))
            throw new InvalidDataException("任务或记录数据无效。");
    }
    async Task Change(Func<PlannerDocument, PlannerDocument> change)
    {
        await gate.WaitAsync();
        bool committed = false;
        try
        {
            if (ReadOnly) throw new IOException(LoadWarning);
            var next = change(data); Validate(next);
            if (ReferenceEquals(next, data)) return;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, next, json);
                    if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("内容已达到当前容量上限，请先备份整理。");
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
                else File.Move(path + ".tmp", path);
            });
            preserveBackup = false; data = next; committed = true;
        }
        finally { gate.Release(); }
        if (committed) Changed?.Invoke();
    }
    internal Task SaveDraft(TodoDraft draft) => Change(d => d with { Draft = draft });
    internal Task AddTask(string title, string detail, DateOnly? date, TimeOnly? time = null, int? remindBefore = null) => Change(d =>
    {
        var now = DateTimeOffset.Now;
        return d with { Tasks = [.. d.Tasks, new(Guid.NewGuid(), title.Trim(), detail.Trim(), date, false, now, now,
            Time: time, RemindBefore: remindBefore, ReminderToken: Guid.NewGuid())], Draft = new() };
    });
    internal Task EditTask(Guid id, string title, string detail, DateOnly? date, TimeOnly? time = null, int? remindBefore = null) => Change(d =>
    {
        if (!d.Tasks.Any(t => t.Id == id)) throw new InvalidDataException("任务已被删除。");
        return d with { Tasks = d.Tasks.Select(t => t.Id == id ? t with { Title = title.Trim(), Detail = detail.Trim(), Date = date,
            Time = time, RemindBefore = remindBefore, ReminderToken = Guid.NewGuid(),
            NotifiedAt = t.Date == date && t.Time == time && t.RemindBefore == remindBefore ? t.NotifiedAt : null,
            SnoozeUntil = t.Date == date && t.Time == time && t.RemindBefore == remindBefore ? t.SnoozeUntil : null,
            Updated = DateTimeOffset.Now } : t).ToArray() };
    });
    internal Task CompleteTask(Guid id, bool completed) => Change(d => d with
    { Tasks = d.Tasks.Select(t => t.Id == id ? t with { Completed = completed, ReminderToken = Guid.NewGuid(),
        NotifiedAt = null, SnoozeUntil = null, Updated = DateTimeOffset.Now } : t).ToArray() });
    internal Task DeleteTask(Guid id) => Change(d => d with { Tasks = d.Tasks.Where(t => t.Id != id).ToArray() });
    internal Task RestoreTask(TodoItem item) => Change(d => d.Tasks.Any(t => t.Id == item.Id) ? d : d with
        { Tasks = [.. d.Tasks, item with { ReminderToken = Guid.NewGuid() }] });
    internal Task SaveNote(Guid id, string text) => Change(d =>
    {
        var current = d.Notes.FirstOrDefault(n => n.Id == id);
        var now = DateTimeOffset.Now;
        var note = current == null ? new QuickNote(id, text, false, now, now) : current with { Text = text, Updated = now };
        return d with { Notes = [.. d.Notes.Where(n => n.Id != id), note], ActiveNote = id };
    });
    internal Task ActivateNote(Guid? id) => Change(d => d with { ActiveNote = id });
    internal Task PinNote(Guid id, bool pinned) => Change(d => d with
    { Notes = d.Notes.Select(n => n.Id == id ? n with { Pinned = pinned } : n).ToArray() });
    internal Task DeleteNote(Guid id) => Change(d => d with { Notes = d.Notes.Where(n => n.Id != id).ToArray(), ActiveNote = d.ActiveNote == id ? null : d.ActiveNote });
    internal Task RestoreNote(QuickNote note) => Change(d => d.Notes.Any(n => n.Id == note.Id) ? d : d with { Notes = [.. d.Notes, note] });
    internal async Task<Guid> ConvertNote(Guid id)
    {
        Guid taskId = Guid.Empty;
        await Change(d =>
        {
            var note = d.Notes.FirstOrDefault(n => n.Id == id) ?? throw new InvalidDataException("记录已被删除。");
            if (string.IsNullOrWhiteSpace(note.Text)) throw new InvalidDataException("空白记录不能转为待办。");
            var existing = d.Tasks.FirstOrDefault(t => t.SourceNote == id);
            if (existing != null) { taskId = existing.Id; return d; }
            taskId = Guid.NewGuid(); var now = DateTimeOffset.Now;
            string title = note.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First().Trim();
            if (title.Length > 200) title = title[..200];
            return d with { Tasks = [.. d.Tasks, new(taskId, title, note.Text, null, false, now, now, id)] };
        });
        return taskId;
    }
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }

    internal Task MarkNotified(Guid id, Guid token, DateTimeOffset now) => Change(d =>
    {
        var item = d.Tasks.FirstOrDefault(t => t.Id == id);
        if (item == null || item.Completed || item.ReminderToken != token || item.RemindBefore == null || item.NotifiedAt != null) return d;
        return d with { Tasks = d.Tasks.Select(t => t.Id == id ? t with { NotifiedAt = now } : t).ToArray() };
    });
    internal async Task<bool> ApplyReminderAction(ReminderAction action, DateTimeOffset now)
    {
        bool accepted = false;
        await Change(d =>
        {
            var item = d.Tasks.FirstOrDefault(t => t.Id == action.Id);
            if (item == null || item.Completed || item.RemindBefore == null || item.ReminderToken != action.Token) return d;
            accepted = true;
            if (action.Kind == ReminderActionKind.Open) return d;
            var next = action.Kind == ReminderActionKind.Complete
                ? item with { Completed = true, ReminderToken = Guid.NewGuid(), NotifiedAt = null, SnoozeUntil = null, Updated = now }
                : item with { ReminderToken = Guid.NewGuid(), NotifiedAt = null, SnoozeUntil = now.AddMinutes(10), Updated = now };
            return d with { Tasks = d.Tasks.Select(t => t.Id == item.Id ? next : t).ToArray() };
        });
        return accepted;
    }
}
