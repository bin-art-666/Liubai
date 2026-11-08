namespace Liubai;

internal sealed class WorkHub
{
    internal WorkStore Store { get; }
    readonly Func<PlannerStore> getPlanner;
    readonly Func<StationStore> getStation;
    PlannerStore? plannerSource;
    StationStore? stationSource;
    PlannerStore planner => getPlanner();
    StationStore station => getStation();
    readonly HashSet<WorkRef> known = [];
    readonly HashSet<Task> pending = [];
    int collectionSuspended;
    internal IDisposable SuspendCollection() { collectionSuspended++; return new Suspension(this); }
    sealed class Suspension(WorkHub owner) : IDisposable { bool disposed; public void Dispose() { if(!disposed) { disposed=true; owner.collectionSuspended--; } } }
    internal string? LastError { get; private set; }
    internal WorkHub(string directory, PlannerStore planner, StationStore station)
        : this(new WorkStore(Path.Combine(directory, "workspaces.json")), () => planner, () => station)
    {
        Attach(planner); Attach(station);
    }
    internal WorkHub(WorkStore store, Func<PlannerStore> planner, Func<StationStore> station)
    { Store = store; getPlanner = planner; getStation = station; }
    internal void Attach(PlannerStore source)
    {
        if (plannerSource != null) return;
        plannerSource = source; known.UnionWith(Loaded()); source.Changed += Track;
    }
    internal void Attach(StationStore source)
    {
        if (stationSource != null) return;
        stationSource = source; known.UnionWith(Loaded()); source.Changed += Track;
    }
    IEnumerable<WorkRef> Loaded() => (stationSource?.Items.Select(i => new WorkRef(WorkKind.Station, i.Id, new(i.Kind.ToString(), i.Value, i.Title))) ?? [])
        .Concat(plannerSource?.Data.Tasks.Select(i => new WorkRef(WorkKind.Task, i.Id)) ?? [])
        .Concat(plannerSource?.Data.Notes.Select(i => new WorkRef(WorkKind.Note, i.Id)) ?? []);
    internal IEnumerable<WorkRef> All() => station.Items.Select(i => new WorkRef(WorkKind.Station, i.Id, new(i.Kind.ToString(), i.Value, i.Title)))
        .Concat(planner.Data.Tasks.Select(i => new WorkRef(WorkKind.Task, i.Id))).Concat(planner.Data.Notes.Select(i => new WorkRef(WorkKind.Note, i.Id)));
    void Track()
    {
        WorkRef[] added; Guid[] removed;
        lock(known) {
            var current = Loaded().ToHashSet(); var currentIds=current.Select(i=>(i.Kind,i.Id)).ToHashSet(); var knownIds = known.Select(i => (i.Kind,i.Id)).ToHashSet(); added = current.Where(i => !knownIds.Contains((i.Kind,i.Id))).ToArray(); removed=known.Where(i=>i.Kind==WorkKind.Station&&!currentIds.Contains((i.Kind,i.Id))).Select(i=>i.Id).ToArray(); known.Clear(); known.UnionWith(current);
        }
        if(collectionSuspended>0)return;
        if(removed.Length>0) Queue(Store.ForgetStation(removed));
        if (added.Length == 0 || Store.Data.Active is not { } scene) return;
        Queue(Store.Link(scene, added));
    }
    void Queue(Task task) {lock(pending) pending.Add(task);_=Finish(task);}
    async Task Finish(Task task)
    {
        try { await task; LastError = null; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { LastError = ex.Message; }
        finally { lock(pending) pending.Remove(task); }
    }
    internal async Task Flush()
    {
        while(true) {
            Task[] snapshot; lock(pending) snapshot=pending.ToArray(); if(snapshot.Length==0)break;
            try { await Task.WhenAll(snapshot); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { LastError=ex.Message; }
            finally { lock(pending) foreach(var task in snapshot) if(task.IsCompleted)pending.Remove(task); }
        }
        await Store.Flush();
    }
    internal string Title(WorkRef item) => item.Kind switch
    {
        WorkKind.Task => planner.Data.Tasks.FirstOrDefault(t => t.Id == item.Id)?.Title ?? "待办已移除",
        WorkKind.Note => planner.Data.Notes.FirstOrDefault(n => n.Id == item.Id)?.Text.Split('\n')[0] is { Length: > 0 } text ? text : "图文记录",
        _ => item.Asset is { } a ? a.Kind is "File" or "Folder" ? Path.GetFileName(a.Value) : a.Title.Length > 0 ? a.Title : a.Value
            : station.Items.FirstOrDefault(s => s.Id == item.Id) is { } s ? s.Kind is StationKind.File or StationKind.Folder ? Path.GetFileName(s.Value) : s.Title.Length > 0 ? s.Title : s.Value : "暂存项已移除"
    };
}
