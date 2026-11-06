namespace Liubai;

internal sealed class WorkHub
{
    internal WorkStore Store { get; }
    readonly PlannerStore planner;
    readonly StationStore station;
    readonly HashSet<WorkRef> known;
    readonly HashSet<Task> pending = [];
    internal string? LastError { get; private set; }
    internal WorkHub(string directory, PlannerStore planner, StationStore station)
    {
        Store = new(Path.Combine(directory, "workspaces.json")); this.planner = planner; this.station = station;
        known = All().ToHashSet(); planner.Changed += Track; station.Changed += Track;
    }
    internal IEnumerable<WorkRef> All() => station.Items.Select(i => new WorkRef(WorkKind.Station, i.Id, new(i.Kind.ToString(), i.Value, i.Title)))
        .Concat(planner.Data.Tasks.Select(i => new WorkRef(WorkKind.Task, i.Id))).Concat(planner.Data.Notes.Select(i => new WorkRef(WorkKind.Note, i.Id)));
    void Track()
    {
        var current = All().ToHashSet(); var added = current.Except(known).ToArray(); known.Clear(); known.UnionWith(current);
        if (added.Length == 0 || Store.Data.Active is not { } scene) return;
        var task = Store.Link(scene, added); pending.Add(task); _ = Finish(task);
    }
    async Task Finish(Task task)
    {
        try { await task; LastError = null; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { LastError = ex.Message; }
        finally { pending.Remove(task); }
    }
    internal async Task Flush() { while (pending.Count > 0) { try { await Task.WhenAll(pending.ToArray()); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } } await Store.Flush(); }
    internal string Title(WorkRef item) => item.Kind switch
    {
        WorkKind.Task => planner.Data.Tasks.FirstOrDefault(t => t.Id == item.Id)?.Title ?? "待办已移除",
        WorkKind.Note => planner.Data.Notes.FirstOrDefault(n => n.Id == item.Id)?.Text.Split('\n')[0] is { Length: > 0 } text ? text : "图文记录",
        _ => item.Asset is { } a ? a.Kind is "File" or "Folder" ? Path.GetFileName(a.Value) : a.Title.Length > 0 ? a.Title : a.Value
            : station.Items.FirstOrDefault(s => s.Id == item.Id) is { } s ? s.Kind is StationKind.File or StationKind.Folder ? Path.GetFileName(s.Value) : s.Title.Length > 0 ? s.Title : s.Value : "暂存项已移除"
    };
}
