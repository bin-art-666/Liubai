using System.Text.Json;

namespace Liubai;

internal enum WorkKind { Station, Task, Note }
internal sealed record WorkAsset(string Kind, string Value, string Title);
internal sealed record WorkRef(WorkKind Kind, Guid Id, WorkAsset? Asset = null);
internal sealed record WorkScene(Guid Id, string Name, WorkRef[] Items, DateTimeOffset Updated, string Description = "", bool Pinned = false);
internal sealed record NoteImage(Guid NoteId, string Path);
internal sealed record WorkResume(Guid TaskId, string Text, DateTimeOffset Updated);
internal sealed record WorkFocus(Guid TaskId, Guid ClockId, string Title, DateTimeOffset? Started = null);
internal sealed record WorkSession(Guid TaskId, string Title, DateTimeOffset Ended, int Seconds);
internal sealed record WorkTaskAssets(Guid TaskId, Guid[] StationIds);
internal sealed record WorkDocument(int Version, WorkScene[] Scenes, Guid? Active, NoteImage[] Images, WorkResume[] Resumes, WorkFocus? Focus = null, WorkSession[]? Sessions = null, WorkTaskAssets[]? TaskAssets = null)
{ internal static WorkDocument Empty => new(2, [], null, [], []); }

// Additive companion data: existing planner/station files and original documents stay independent.
internal sealed partial class WorkStore
{
    readonly string path;
    readonly SemaphoreSlim gate = new(1, 1);
    WorkDocument data = WorkDocument.Empty;
    bool preserveBackup;
    internal bool ReadOnly { get; }
    internal string? Warning { get; }
    internal WorkDocument Data => data;
    internal event Action? Changed;
    internal WorkStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
        try { data = Read(path); }
        catch (NotSupportedException) { ReadOnly = true; Warning = "工作现场来自更新版本，已停止写入。"; }
        catch (Exception ex) when (ReadError(ex))
        {
            try { data = Read(path + ".bak"); preserveBackup = true; Warning = "已从备份恢复工作现场。"; }
            catch (Exception backup) when (ReadError(backup) || backup is NotSupportedException)
            { ReadOnly = true; Warning = "工作现场无法读取，已保留原文件并停止写入。"; }
        }
    }
    static bool ReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    static WorkDocument Read(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("工作现场数据过大。");
        var value = JsonSerializer.Deserialize<WorkDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        if (value.Version is not (1 or 2)) throw new NotSupportedException();
        value=value with {Version=2};
        Validate(value); return value;
    }
    internal static void Validate(WorkDocument d)
    {
        if (d.TaskAssets is {} links && (links.Length>10000 || links.Any(l=>l==null||l.TaskId==Guid.Empty||l.StationIds==null||l.StationIds.Length>2000||l.StationIds.Any(id=>id==Guid.Empty)||l.StationIds.Distinct().Count()!=l.StationIds.Length)||links.Select(l=>l.TaskId).Distinct().Count()!=links.Length)) throw new InvalidDataException("任务资料关联无效。");
        if (d.Scenes?.Any(s => s != null && (s.Description == null || s.Description.Length > 2000)) == true
            || d.Sessions is { } sessions && (sessions.Length > 2000 || sessions.Any(s => s == null || s.TaskId == Guid.Empty || s.Title == null || s.Title.Length > 200 || s.Seconds is < 0 or > 86400)))
            throw new InvalidDataException("分组说明或专注记录无效。");
        if (d.Version != 2 || d.Scenes is null || d.Images is null || d.Resumes is null || d.Scenes.Length > 100 || d.Images.Length > 10000 || d.Resumes.Length > 500
            || d.Scenes.Any(s => s is null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 80 || s.Name.Any(char.IsControl)
                || s.Items is null || s.Items.Length > 2000 || s.Items.Any(i => i is null || i.Id == Guid.Empty || !Enum.IsDefined(i.Kind)
                    || i.Asset is { } asset && (i.Kind != WorkKind.Station || asset.Kind is not ("File" or "Folder" or "Text" or "Link") || string.IsNullOrWhiteSpace(asset.Value)
                        || asset.Value.Length > 20000 || asset.Title is null || asset.Title.Length > 1000
                        || asset.Kind is "File" or "Folder" && !Path.IsPathFullyQualified(asset.Value)
                        || asset.Kind == "Link" && (!Uri.TryCreate(asset.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))))
                    || s.Items.Select(i => (i.Kind, i.Id)).Distinct().Count() != s.Items.Length)
            || d.Scenes.Select(s => s.Id).Distinct().Count() != d.Scenes.Length || d.Scenes.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Scenes.Length
            || d.Active is { } a && !d.Scenes.Any(s => s.Id == a)
            || d.Images.Any(i => i is null || i.NoteId == Guid.Empty || string.IsNullOrWhiteSpace(i.Path) || !Path.IsPathFullyQualified(i.Path))
            || d.Images.GroupBy(i => i.NoteId).Any(g => g.Count() > 12)
            || d.Resumes.Any(r => r is null || r.TaskId == Guid.Empty || r.Text is null || r.Text.Length > 2000)
            || d.Focus is { } f && (f.TaskId == Guid.Empty || f.ClockId == Guid.Empty || string.IsNullOrWhiteSpace(f.Title) || f.Title.Length > 200))
            throw new InvalidDataException("工作现场内容或容量无效。");
    }
    async Task Change(Func<WorkDocument, WorkDocument> update)
    {
        await gate.WaitAsync(); bool changed = false;
        try
        {
            if (ReadOnly) throw new IOException(Warning);
            var next = update(data); if (ReferenceEquals(next, data) || next == data) return; Validate(next);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await Task.Run(() =>
            {
                try
                {
                    using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                    { JsonSerializer.Serialize(stream, next); if (stream.Length > 8 * 1024 * 1024) throw new IOException("工作现场数据已达上限。"); stream.Flush(true); }
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak"); else File.Move(path + ".tmp", path);
                }
                finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            });
            data = next; preserveBackup = false; changed = true;
        }
        finally { gate.Release(); }
        if (changed) Changed?.Invoke();
    }
    internal async Task<Guid> Create(string name, bool activate = true)
    {
        var id = Guid.NewGuid(); await Change(d => d with { Scenes = [.. d.Scenes, new(id, name.Trim(), [], DateTimeOffset.Now)], Active = activate ? id : d.Active }); return id;
    }
    internal Task Activate(Guid? id) => Change(d => d with { Active = id });
    internal Task Rename(Guid id, string name) => Change(d => d with { Scenes = d.Scenes.Select(s => s.Id == id ? s with { Name = name.Trim() } : s).ToArray() });
    internal Task Describe(Guid id, string text, bool pinned) => Change(d => d with { Scenes = d.Scenes.Select(s => s.Id == id ? s with { Description = text, Pinned = pinned, Updated = DateTimeOffset.Now } : s).ToArray() });
    internal Task Reorder(Guid id, int direction) => Change(d => {
        var scenes = d.Scenes.ToArray(); int index = Array.FindIndex(scenes, s => s.Id == id), target = index + direction;
        if (index < 0 || target < 0 || target >= scenes.Length) return d;
        (scenes[index], scenes[target]) = (scenes[target], scenes[index]); return d with { Scenes = scenes };
    });
    internal Task Remove(Guid id) => Change(d => d with { Scenes = d.Scenes.Where(s => s.Id != id).ToArray(), Active = d.Active == id ? null : d.Active });
    internal Task Link(Guid? scene, IEnumerable<WorkRef> refs)
    {
        var snapshot = refs.ToArray();
        return Change(d => scene is null || !d.Scenes.Any(s => s.Id == scene) ? d : d with
        { Scenes = d.Scenes.Select(s => s.Id == scene ? s with { Items = s.Items.Concat(snapshot).DistinctBy(i => (i.Kind, i.Id)).ToArray(), Updated = DateTimeOffset.Now } : s).ToArray() });
    }
    internal Task Unlink(Guid scene, WorkRef item) => UnlinkMany(scene, [item]);
    internal Task UnlinkMany(Guid scene, IEnumerable<WorkRef> refs)
    { var keys = refs.Select(i => (i.Kind,i.Id)).ToHashSet(); return Change(d => d with { Scenes = d.Scenes.Select(s => s.Id == scene ? s with { Items = s.Items.Where(i => !keys.Contains((i.Kind,i.Id))).ToArray() } : s).ToArray() }); }
    internal Task FinishFocus(Guid task, string text, int seconds) => Change(d => d with {
        Resumes = [new(task, text.Trim(), DateTimeOffset.Now), .. d.Resumes.Take(499)],
        Sessions = d.Focus is { } f && f.TaskId == task ? [new(task, f.Title, DateTimeOffset.Now, Math.Clamp(seconds,0,86400)), .. (d.Sessions ?? []).Take(1999)] : d.Sessions,
        Focus = d.Focus?.TaskId == task ? null : d.Focus });
    internal Task AddImage(Guid note, string path) => AddImages(note, [path]);
    internal Task AddImages(Guid note, IEnumerable<string> paths)
    {
        var images = paths.Select(p => new NoteImage(note, p)).ToArray();
        return Change(d => images.All(d.Images.Contains) ? d : d with { Images = d.Images.Concat(images).Distinct().ToArray() });
    }
    internal Task SetFocus(WorkFocus? focus) => Change(d => d with { Focus = focus });
    internal Task LinkTaskAssets(Guid task, IEnumerable<Guid> ids)
    { var values=ids.Distinct().ToArray(); return Change(d=>d with { TaskAssets=[..(d.TaskAssets??[]).Where(l=>l.TaskId!=task),new(task,values)] }); }
    internal Task ReplaceStationReference(Guid previous,Guid current) => Change(d=>d with {
        Scenes=d.Scenes.Select(s=>s with { Items=s.Items.Select(r=>r.Kind==WorkKind.Station&&r.Id==previous?new WorkRef(WorkKind.Station,current):r).DistinctBy(r=>(r.Kind,r.Id)).ToArray() }).ToArray(),
        TaskAssets=(d.TaskAssets??[]).Select(l=>l with { StationIds=l.StationIds.Select(id=>id==previous?current:id).Distinct().ToArray() }).ToArray() });
    internal Task ForgetStation(IEnumerable<Guid> ids)
    {var keys=ids.ToHashSet();return Change(d=>d with {Scenes=d.Scenes.Select(s=>s with {Items=s.Items.Where(r=>r.Kind!=WorkKind.Station||!keys.Contains(r.Id)).ToArray()}).ToArray(),TaskAssets=(d.TaskAssets??[]).Select(l=>l with {StationIds=l.StationIds.Where(id=>!keys.Contains(id)).ToArray()}).ToArray()});}
    internal Task RestoreProgress(IEnumerable<WorkResume> resumes, IEnumerable<WorkSession> sessions, IEnumerable<NoteImage> images)
    { var r = resumes.ToArray(); var s = sessions.ToArray(); var i = images.ToArray(); return Change(d => d with { Resumes = r.Concat(d.Resumes).Take(500).ToArray(), Sessions = s.Concat(d.Sessions??[]).Take(2000).ToArray(), Images = d.Images.Concat(i).ToArray() }); }
    internal async Task<Guid> ImportScene(string name, string description, WorkRef[] refs, WorkResume[] resumes, WorkSession[] sessions, NoteImage[] images, WorkTaskAssets[]? taskAssets = null)
    {
        var id=Guid.NewGuid();
        await Change(d=>d with { Scenes=[..d.Scenes,new WorkScene(id,name,refs,DateTimeOffset.Now,description)],Active=id,
            Resumes=resumes.Concat(d.Resumes).Take(500).ToArray(),Sessions=sessions.Concat(d.Sessions??[]).Take(2000).ToArray(),Images=d.Images.Concat(images).ToArray(), TaskAssets=(d.TaskAssets??[]).Concat(taskAssets??[]).ToArray() });
        return id;
    }
    internal Task Resume(Guid task, string text) => Change(d => d with { Resumes = [new(task, text.Trim(), DateTimeOffset.Now), .. d.Resumes.Take(499)], Focus = d.Focus?.TaskId == task ? null : d.Focus });
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
