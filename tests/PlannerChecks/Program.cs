using Liubai;
using System.Text.Json;

string directory = Path.Combine(Path.GetTempPath(), "Liubai-PlannerChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
async Task Reject(Func<Task> action)
{
    bool rejected = false;
    try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { rejected = true; }
    Check(rejected, "Invalid/failed writes must be rejected.");
}
try
{
    string path = Path.Combine(directory, "planner.json");
    var store = new PlannerStore(path);
    Check(!File.Exists(path) && store.Data.Tasks.Length == 0 && !store.ReadOnly, "Opening an empty store must not create a file.");
    var today = new DateOnly(2026, 11, 4);
    await store.SaveDraft(new("中文草稿", "草稿备注", today));
    Check(new PlannerStore(path).Data.Draft == store.Data.Draft, "Task draft and date must survive restart.");
    await store.AddTask("  今日任务  ", "搜索KEYword", today);
    await store.AddTask("逾期", "", today.AddDays(-1));
    await store.AddTask("明天", "", today.AddDays(1));
    await store.AddTask("未安排", "", null);
    Check(store.Data.Tasks.Length == 4 && store.Data.Draft == new TodoDraft(), "Adding a task clears only the task draft.");
    Check(PlannerQueries.Tasks(store.Data, TodoView.Today, "", today).Select(t => t.Title).SequenceEqual(new[] { "逾期", "今日任务" }), "Today includes overdue tasks and sorts by date.");
    Check(PlannerQueries.Tasks(store.Data, TodoView.Planned, "", today).Single().Title == "明天", "Planned excludes today, overdue and undated tasks.");
    Check(PlannerQueries.Tasks(store.Data, TodoView.All, "keyword", today).Single().Title == "今日任务", "Search includes details and ignores case.");
    var first = store.Data.Tasks.First();
    await store.EditTask(first.Id, "修改", "新备注", null);
    Check(store.Data.Tasks.First().Date == null && store.Data.Tasks.First().Created == first.Created, "Editing clears dates while preserving task identity.");
    await store.CompleteTask(first.Id, true);
    Check(PlannerQueries.Tasks(store.Data, TodoView.Completed, "", today).Single().Id == first.Id
        && !PlannerQueries.Tasks(store.Data, TodoView.All, "", today).Any(t => t.Id == first.Id), "Completed and open views must be disjoint.");
    await store.CompleteTask(first.Id, false);
    Check(store.Data.Tasks.First().Completed == false, "Completion must be reversible.");
    var deleted = store.Data.Tasks.First();
    await store.DeleteTask(deleted.Id); await store.RestoreTask(deleted); await store.RestoreTask(deleted);
    Check(store.Data.Tasks.Count(t => t.Id == deleted.Id) == 1, "Undo restores a task exactly once.");
    Guid noteId = Guid.NewGuid();
    await store.SaveNote(noteId, "灵感第一行\n中文第二行");
    var original = store.Data.Notes.Single();
    await store.PinNote(noteId, true); await store.SaveNote(noteId, "修改记录\n详细内容");
    Check(store.Data.Notes.Single().Pinned && store.Data.Notes.Single().Created == original.Created, "Autosave must preserve pinning and identity.");
    Check(PlannerQueries.Notes(store.Data, "详细").Single().Id == noteId, "Note search covers the entire text.");
    Guid converted = await store.ConvertNote(noteId);
    Check(await store.ConvertNote(noteId) == converted && store.Data.Tasks.Count(t => t.SourceNote == noteId) == 1, "Repeated conversion must not duplicate a linked task.");
    Check(store.Data.Notes.Single().Text == "修改记录\n详细内容" && store.Data.Tasks.Single(t => t.Id == converted).Detail.Contains("详细内容"), "Conversion preserves original notes and full content.");
    var note = store.Data.Notes.Single();
    await store.DeleteNote(noteId);
    Check(store.Data.ActiveNote == null && store.Data.Tasks.Any(t => t.Id == converted), "Deleting a note clears its active editor but preserves converted tasks.");
    await store.RestoreNote(note); await store.RestoreNote(note);
    Check(store.Data.Notes.Length == 1, "Note undo must not duplicate records.");
    await store.SaveNote(noteId, "");
    Check(new PlannerStore(path).Data.Notes.Single().Text == "", "Clearing a note must persist as an editable empty draft.");
    await Reject(() => store.ConvertNote(noteId));
    int before = store.Data.Tasks.Length;
    await Reject(() => store.AddTask(" ", "", null));
    await Reject(() => store.AddTask(new string('x', 201), "", null));
    Check(store.Data.Tasks.Length == before, "Rejected changes must not affect live state.");
    await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.AddTask("并发 " + i, "", null)));
    Check(store.Data.Tasks.Length == before + 20 && new PlannerStore(path).Data.Tasks.Length == before + 20, "Serialized concurrent writes must not lose records.");
    await store.Flush();
    Check(!File.Exists(path + ".tmp"), "Successful commits must leave no temporary file.");
    var recoveredData = new PlannerStore(path + ".bak").Data;
    File.WriteAllText(path, "broken JSON");
    var recovered = new PlannerStore(path);
    Check(!recovered.ReadOnly && recovered.LoadWarning != null && recovered.Data.Tasks.Length == recoveredData.Tasks.Length, "Corrupt primary must recover from valid backup.");
    await recovered.SaveDraft(new("恢复后草稿"));
    Check(new PlannerStore(path).Data.Draft.Title == "恢复后草稿" && new PlannerStore(path + ".bak").Data.Tasks.Length == recoveredData.Tasks.Length, "First recovery write must preserve the known-good backup.");
    File.WriteAllText(path, JsonSerializer.Serialize(recovered.Data with { Version = 999 }));
    var future = new PlannerStore(path);
    Check(future.ReadOnly, "Future versions must not silently fall back and overwrite new data.");
    await Reject(() => future.SaveDraft(new("覆盖")));
    File.WriteAllText(path, "corrupt"); File.WriteAllText(path + ".bak", "corrupt backup");
    var corrupt = new PlannerStore(path);
    await Reject(() => corrupt.AddTask("覆盖", "", null));
    Check(File.ReadAllText(path) == "corrupt" && File.ReadAllText(path + ".bak") == "corrupt backup", "Unrecoverable files must remain untouched.");
    string blockedPath = Path.Combine(directory, "blocked"); Directory.CreateDirectory(blockedPath);
    var blocked = new PlannerStore(blockedPath);
    await Reject(() => blocked.SaveNote(Guid.NewGuid(), "不能丢失"));
    Check(blocked.Data.Notes.Length == 0, "Disk failure must not report a live commit.");
    string legacyPath = Path.Combine(directory, "legacy.json");
    File.WriteAllText(legacyPath, "{\"Version\":1,\"Tasks\":[],\"Notes\":[],\"Draft\":{\"Title\":\"旧草稿\",\"Detail\":\"\"}}");
    var legacy = new PlannerStore(legacyPath);
    Check(legacy.Data.Version == 2 && legacy.Data.Draft.Title == "旧草稿" && !legacy.ReadOnly, "V1 data must migrate without losing drafts.");
    await legacy.SaveDraft(legacy.Data.Draft);
    Check(new PlannerStore(legacyPath).Data.Version == 2, "Migrated data must persist in the protected new format.");
    var reminders = new PlannerStore(Path.Combine(directory, "reminders.json"));
    var clock = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    await reminders.AddTask("准点 <>&提醒", "备注 & <xml>", today, new(10, 30), 30);
    var scheduled = reminders.Data.Tasks.Single();
    Check(ReminderPolicy.Due(scheduled, TimeZoneInfo.Utc) == clock, "Advance notice must resolve to the intended instant.");
    Check(ReminderPolicy.Due(scheduled with { Date = today.AddDays(1), Time = new(0, 5), RemindBefore = 15 }, TimeZoneInfo.Utc) == new DateTimeOffset(2026, 11, 4, 23, 50, 0, TimeSpan.Zero), "Advance notice must cross midnight correctly.");
    var xml = System.Xml.Linq.XDocument.Parse(ReminderPolicy.Payload(scheduled, clock, TimeZoneInfo.Utc));
    Check(xml.Descendants("text").First().Value == scheduled.Title && xml.Descendants("action").Count() == 3,
        "Notification payload must escape content and include complete/snooze/open actions.");
    Check(ReminderPolicy.TryParse(ReminderPolicy.Arguments(scheduled, ReminderActionKind.Snooze), out var parsed)
        && parsed.Id == scheduled.Id && parsed.Kind == ReminderActionKind.Snooze, "Notification actions must round-trip.");
    foreach (string bad in new[] { "", "action=100", ReminderPolicy.Arguments(scheduled, ReminderActionKind.Open) + "&action=complete", "action=open&id=invalid&token=invalid" })
        Check(!ReminderPolicy.TryParse(bad, out _), "Malformed notification actions must be rejected.");
    var fake = new FakeNotifications(); Guid? opened = null; bool allowed = true;
    var coordinator = new ReminderCoordinator(reminders, fake, id => opened = id, () => allowed);
    await coordinator.Tick(clock.AddSeconds(-1), TimeZoneInfo.Utc);
    Check(fake.Shown == 0, "A reminder must never arrive early.");
    allowed = false; await coordinator.Tick(clock, TimeZoneInfo.Utc);
    Check(fake.Shown == 0 && reminders.Data.Tasks.Single().NotifiedAt == null, "Suspended detection must not consume reminders.");
    allowed = true; fake.Accept = false; await coordinator.Tick(clock, TimeZoneInfo.Utc);
    Check(reminders.Data.Tasks.Single().NotifiedAt == null, "Rejected Windows notifications must remain pending.");
    fake.Accept = true; await coordinator.Tick(clock, TimeZoneInfo.Utc); await coordinator.Tick(clock.AddMinutes(1), TimeZoneInfo.Utc);
    Check(fake.Shown == 1 && reminders.Data.Tasks.Single().NotifiedAt == clock, "Delivery stamps must suppress repeated polling.");
    var restarted = new ReminderCoordinator(new PlannerStore(Path.Combine(directory, "reminders.json")), fake, _ => { });
    await restarted.Tick(clock.AddMinutes(2), TimeZoneInfo.Utc);
    Check(fake.Shown == 1, "Restart must not resend an already delivered reminder.");
    Check(await coordinator.Handle(new(scheduled.Id, scheduled.ReminderToken, ReminderActionKind.Open), clock) && opened == scheduled.Id, "Open action must target the exact task.");
    Check(await coordinator.Handle(parsed, clock), "Snooze action must persist.");
    Check(!await coordinator.Handle(parsed, clock.AddMinutes(1)), "Repeated snooze clicks on an old notification must be ignored.");
    await coordinator.Tick(clock.AddMinutes(9), TimeZoneInfo.Utc);
    Check(fake.Shown == 1, "Snooze must wait the full ten minutes.");
    await coordinator.Tick(clock.AddMinutes(10), TimeZoneInfo.Utc);
    Check(fake.Shown == 2, "Snoozed reminder must arrive at the new time.");
    var snoozed = reminders.Data.Tasks.Single();
    Check(await coordinator.Handle(new(snoozed.Id, snoozed.ReminderToken, ReminderActionKind.Complete), clock.AddMinutes(10))
        && reminders.Data.Tasks.Single().Completed, "Complete action must update the persisted task.");
    await coordinator.Tick(clock.AddHours(1), TimeZoneInfo.Utc);
    Check(fake.Active.Count == 0 && fake.Shown == 2, "Completed tasks must lose old notifications and stop reminders.");
    await reminders.AddTask("之后修改", "", today, new(10, 0), 0);
    var edited = reminders.Data.Tasks.Last();
    await coordinator.Tick(clock, TimeZoneInfo.Utc);
    await reminders.EditTask(edited.Id, "改标题", "", today, new(10, 0), 0);
    int delivered = fake.Shown;
    await coordinator.Tick(clock.AddMinutes(1), TimeZoneInfo.Utc);
    Check(fake.Shown == delivered && !fake.Active.ContainsKey(edited.Id), "Title-only changes must invalidate stale actions without reminding again.");
    await reminders.EditTask(edited.Id, "改期", "", today, new(12, 0), 0);
    Check(!await coordinator.Handle(new(edited.Id, edited.ReminderToken, ReminderActionKind.Complete), clock), "Stale notification actions must not mutate rescheduled tasks.");
    await coordinator.Tick(clock.AddHours(2), TimeZoneInfo.Utc);
    Check(fake.Shown == delivered + 1, "Changing the schedule must arm a fresh reminder.");
    await reminders.DeleteTask(edited.Id); await coordinator.Tick(clock.AddHours(3), TimeZoneInfo.Utc);
    Check(!fake.Active.ContainsKey(edited.Id), "Deleting a task must clear its notification.");
    await reminders.AddTask("并发补提醒", "", today, new(9, 0), 0);
    int beforeCatchup = fake.Shown;
    await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => coordinator.Tick(clock.AddHours(4), TimeZoneInfo.Utc)));
    Check(fake.Shown == beforeCatchup + 1, "Concurrent resume events must send only one catch-up reminder.");
    await Reject(() => reminders.AddTask("缺少日期", "", null, new(10, 0), 0));
    await Reject(() => reminders.SaveDraft(new("缺少时间", "", today, null, 0)));
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    var dst = scheduled with { Date = new(2026, 3, 8), Time = new(2, 30), RemindBefore = 0 };
    Check(ReminderPolicy.Due(dst, zone) == new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), "DST gaps must advance to a valid time rather than drop the reminder.");
    string failurePath = Path.Combine(directory, "delivery-failure.json");
    var failureStore = new PlannerStore(failurePath); var failureFake = new FakeNotifications();
    await failureStore.AddTask("保存失败时不重复弹出", "", today, new(9, 0), 0);
    var failureCoordinator = new ReminderCoordinator(failureStore, failureFake, _ => { });
    File.Delete(failurePath); Directory.CreateDirectory(failurePath);
    await Reject(() => failureCoordinator.Tick(clock, TimeZoneInfo.Utc));
    await Reject(() => failureCoordinator.Tick(clock, TimeZoneInfo.Utc));
    Check(failureFake.Shown == 1, "A failed delivery-stamp write must not raise repeated notifications.");
    Directory.Delete(failurePath); File.WriteAllText(failurePath, JsonSerializer.Serialize(failureStore.Data));
    await failureCoordinator.Tick(clock, TimeZoneInfo.Utc);
    Check(failureFake.Shown == 1 && failureStore.Data.Tasks.Single().NotifiedAt != null, "Recovery retries the delivery stamp without resending.");
    Console.WriteLine($"PASS: {count} planner and reminder persistence, recovery, action and timing checks");
}
finally { Directory.Delete(directory, true); }

sealed class FakeNotifications : IReminderNotifications
{
    internal bool Accept = true;
    internal int Shown;
    internal Dictionary<Guid, Guid> Active = new();
    public Task Reconcile(IReadOnlyList<TodoItem> tasks)
    {
        foreach (var id in Active.Keys.ToArray())
            if (!tasks.Any(t => t.Id == id && !t.Completed && t.RemindBefore != null && t.ReminderToken == Active[id])) Active.Remove(id);
        return Task.CompletedTask;
    }
    public Task<bool> Show(TodoItem item, DateTimeOffset now)
    {
        if (Accept) { Shown++; Active[item.Id] = item.ReminderToken; }
        return Task.FromResult(Accept);
    }
}
