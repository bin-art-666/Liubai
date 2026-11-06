using System.Text.Json;
using Liubai;

string root = Path.GetFullPath(Path.Combine(".build", "work-checks-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Reject(Func<Task> action) { bool rejected = false; try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; } Check(rejected, "Invalid state accepted"); }
try
{
    string path = Path.Combine(root, "workspaces.json"); var store = new WorkStore(path);
    Check(!store.ReadOnly && store.Data.Version == 1 && store.Data.Scenes.Length == 0 && store.Data.Images.Length == 0, "Empty store");
    Guid first = await store.Create("网络第二章"); Check(store.Data.Active == first && store.Data.Scenes.Single().Name == "网络第二章", "Create and activate");
    var reference = new WorkRef(WorkKind.Task, Guid.NewGuid()); await store.Link(first, [reference, reference]);
    Check(store.Data.Scenes.Single().Items.Length == 1, "Reference deduplication");
    await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.Link(first, [new(WorkKind.Note, Guid.NewGuid())])));
    Check(store.Data.Scenes.Single().Items.Length == 13, "Concurrent links retain all entries");
    var restored = new WorkStore(path); Check(restored.Data.Active == first && restored.Data.Scenes.Single().Items.Length == 13, "Roundtrip");
    Guid second = await store.Create("另一个现场"); await store.Link(first, [new(WorkKind.Station, Guid.NewGuid())]);
    Check(store.Data.Scenes.Single(s => s.Id == second).Items.Length == 0, "Explicit captured scene unaffected by active changes");
    await store.Activate(null); await store.Link(null, [reference]); Check(store.Data.Active == null && store.Data.Scenes.Single(s => s.Id == first).Items.Length == 14, "Unassigned imports do not change groups");
    await store.Rename(first, "第二章复习"); Check(store.Data.Scenes.Single(s => s.Id == first).Name == "第二章复习", "Rename");
    await Reject(() => store.Create("第二章复习")); await Reject(() => store.Rename(first, " ")); await Reject(() => store.Activate(Guid.NewGuid()));
    await Reject(() => store.Link(first, [new((WorkKind)99, Guid.NewGuid())]));
    await Reject(() => store.Link(first, [new(WorkKind.Note, Guid.Empty)]));
    await Reject(() => store.Link(first, [new(WorkKind.Station, Guid.NewGuid(), new("Link", "javascript:alert(1)", ""))]));
    var asset = new WorkRef(WorkKind.Station, Guid.NewGuid(), new("Text", "独立保留的现场文字", "资料"));
    await store.Link(second, [asset]); Check(new WorkStore(path).Data.Scenes.Single(s => s.Id == second).Items.Single().Asset?.Value == asset.Asset!.Value, "Resource snapshots survive independently of station records");
    await Reject(() => store.Link(first, Enumerable.Range(0, 2001).Select(_ => new WorkRef(WorkKind.Note, Guid.NewGuid()))));
    var note = Guid.NewGuid(); string image = Path.Combine(root, "image.png"); await store.AddImage(note, image); await store.AddImage(note, image);
    Check(store.Data.Images.Length == 1, "Image deduplication");
    await Reject(() => store.AddImage(note, "relative.png"));
    var focus = new WorkFocus(reference.Id, Guid.NewGuid(), "复习对称加密"); await store.SetFocus(focus);
    Check(new WorkStore(path).Data.Focus == focus, "Focus link survives restart");
    await store.Resume(reference.Id, "继续看 CBC"); Check(store.Data.Focus == null && store.Data.Resumes[0].Text == "继续看 CBC", "Continuation clears only owned focus");
    await store.SetFocus(focus); await store.Resume(Guid.NewGuid(), "其他任务"); Check(store.Data.Focus == focus, "Other task continuation does not clear active focus");
    await store.Unlink(first, reference); Check(!store.Data.Scenes.Single(s => s.Id == first).Items.Contains(reference), "Unlink");
    await store.Activate(first); await store.Remove(first); Check(store.Data.Active == null && store.Data.Images.Length == 1 && store.Data.Resumes.Length == 2, "Removing group preserves attachments and progress");
    string valid = File.ReadAllText(path); File.WriteAllText(path, "{broken"); var recovered = new WorkStore(path);
    Check(!recovered.ReadOnly && recovered.Warning != null, "Valid backup recovery");
    File.WriteAllText(path + ".bak", "{broken"); var readOnly = new WorkStore(path); await Reject(() => readOnly.Create("blocked"));
    Check(readOnly.ReadOnly && File.ReadAllText(path) == "{broken", "Corrupt data preserved");
    File.WriteAllText(path, JsonSerializer.Serialize(WorkDocument.Empty with { Version = 99 })); var future = new WorkStore(path);
    Check(future.ReadOnly, "Future version protected");
    Check(!Directory.GetFiles(root, "*.tmp").Any(), "No temporary write residue");
    Console.WriteLine($"PASS: {checks} workspace persistence, linking, isolation, focus and recovery checks");
}
finally { Directory.Delete(root, true); }
