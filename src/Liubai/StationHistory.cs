namespace Liubai;

internal sealed partial class StationStore
{
    internal void ValidateUndo(StationUndo undo) {if(ReadOnly)throw new IOException(LoadWarning);Validate(data with {Items=undo.RestoreItems(data.Items)});}
    internal Task ApplyUndo(StationUndo undo) => Change(d=>d with {Items=undo.RestoreItems(d.Items)});
}
internal sealed partial class WorkStore
{
    internal Task ApplyUndo(StationUndo undo) => Change(undo.RestoreWork);
}

internal sealed record StationEdit(StationItem? Before, StationItem? After);
internal sealed record SceneEdit(WorkScene? Before, WorkScene? After, int Index);
internal sealed record AssetEdit(WorkTaskAssets? Before, WorkTaskAssets? After);
internal sealed record StationUndo(string Label, StationEdit[] Items, SceneEdit[] Scenes, AssetEdit[] Assets, Guid? ActiveBefore, Guid? ActiveAfter)
{
    internal bool Empty => Items.Length==0 && Scenes.Length==0 && Assets.Length==0;
    internal static bool Same(WorkScene a,WorkScene b) => a.Name==b.Name&&a.Description==b.Description&&a.Pinned==b.Pinned&&a.Items.SequenceEqual(b.Items);
    internal static StationUndo Between(string label,StationItem[] before,StationItem[] after,WorkDocument old,WorkDocument next)
    {
        var b=before.ToDictionary(i=>i.Id);var a=after.ToDictionary(i=>i.Id);
        var items=b.Keys.Union(a.Keys).Select(id=>new StationEdit(b.GetValueOrDefault(id),a.GetValueOrDefault(id))).Where(e=>e.Before!=e.After).ToArray();
        var sb=old.Scenes.ToDictionary(s=>s.Id);var sa=next.Scenes.ToDictionary(s=>s.Id);
        var scenes=sb.Keys.Union(sa.Keys).Select(id=>new SceneEdit(sb.GetValueOrDefault(id),sa.GetValueOrDefault(id),Array.FindIndex(old.Scenes,s=>s.Id==id)))
            .Where(e=>e.Before==null||e.After==null||!Same(e.Before,e.After)).ToArray();
        var ab=(old.TaskAssets??[]).ToDictionary(l=>l.TaskId);var aa=(next.TaskAssets??[]).ToDictionary(l=>l.TaskId);
        var assets=ab.Keys.Union(aa.Keys).Select(id=>new AssetEdit(ab.GetValueOrDefault(id),aa.GetValueOrDefault(id)))
            .Where(e=>!(e.Before?.StationIds??[]).SequenceEqual(e.After?.StationIds??[])).ToArray();
        return new(label,items,scenes,assets,old.Active,next.Active);
    }
    static InvalidOperationException Conflict() => new("内容已变化，未覆盖后续修改。请检查当前记录后重试。");
    internal StationItem[] RestoreItems(StationItem[] current)
    {
        var items=current.ToList();
        foreach(var edit in Items) {
            Guid id=(edit.Before??edit.After)!.Id;var value=items.FirstOrDefault(i=>i.Id==id);
            if(value==edit.Before)continue;
            if(value!=edit.After)throw Conflict();
            items.RemoveAll(i=>i.Id==id);if(edit.Before!=null)items.Add(edit.Before);
        }
        return items.ToArray();
    }
    internal WorkDocument RestoreWork(WorkDocument current)
    {
        var scenes=current.Scenes.ToList();
        foreach(var edit in Scenes) {
            Guid id=(edit.Before??edit.After)!.Id;var scene=scenes.FirstOrDefault(s=>s.Id==id);
            if(edit.Before==null) {
                if(scene==null)continue;
                if(edit.After==null||!Same(scene,edit.After))throw Conflict();
                scenes.Remove(scene);continue;
            }
            if(edit.After==null) {
                if(scene!=null) {if(!Same(scene,edit.Before))throw Conflict();continue;}
                scenes.Insert(Math.Clamp(edit.Index,0,scenes.Count),edit.Before);continue;
            }
            if(scene==null)throw Conflict();
            string Restore(string now,string before,string after) {if(before==after)return now;if(now!=after&&now!=before)throw Conflict();return before;}
            if(edit.Before.Pinned!=edit.After.Pinned&&scene.Pinned!=edit.After.Pinned&&scene.Pinned!=edit.Before.Pinned)throw Conflict();
            var refs=scene.Items.ToList();
            var before=edit.Before.Items.ToDictionary(r=>(r.Kind,r.Id));var after=edit.After.Items.ToDictionary(r=>(r.Kind,r.Id));
            foreach(var key in before.Keys.Union(after.Keys)) {
                var old=before.GetValueOrDefault(key);var expected=after.GetValueOrDefault(key);if(old==expected)continue;
                var now=refs.FirstOrDefault(r=>(r.Kind,r.Id)==key);if(now==old)continue;if(now!=expected)throw Conflict();
                refs.RemoveAll(r=>(r.Kind,r.Id)==key);
                if(old!=null)refs.Insert(Math.Clamp(Array.FindIndex(edit.Before.Items,r=>(r.Kind,r.Id)==key),0,refs.Count),old);
            }
            scenes[scenes.IndexOf(scene)]=scene with {Name=Restore(scene.Name,edit.Before.Name,edit.After.Name),Description=Restore(scene.Description,edit.Before.Description,edit.After.Description),
                Pinned=edit.Before.Pinned==edit.After.Pinned?scene.Pinned:edit.Before.Pinned,Items=refs.ToArray(),Updated=DateTimeOffset.Now};
        }
        var assets=(current.TaskAssets??[]).ToList();
        foreach(var edit in Assets) {
            Guid id=(edit.Before??edit.After)!.TaskId;var old=(edit.Before?.StationIds??[]).ToHashSet();var next=(edit.After?.StationIds??[]).ToHashSet();
            var now=assets.FirstOrDefault(l=>l.TaskId==id)?.StationIds.ToHashSet()??[];
            now.ExceptWith(next.Except(old));now.UnionWith(old.Except(next));assets.RemoveAll(l=>l.TaskId==id);if(now.Count>0)assets.Add(new(id,now.ToArray()));
        }
        Guid? active=current.Active==ActiveAfter?ActiveBefore:current.Active;
        if(active!=null&&!scenes.Any(s=>s.Id==active))active=null;
        return current with {Scenes=scenes.ToArray(),TaskAssets=assets.ToArray(),Active=active};
    }
    internal StationUndo Reverse() => this with { Items=Items.Select(e=>new StationEdit(e.After,e.Before)).ToArray(), Scenes=Scenes.Select(e=>new SceneEdit(e.After,e.Before,e.Index)).ToArray(),
        Assets=Assets.Select(e=>new AssetEdit(e.After,e.Before)).ToArray(),ActiveBefore=ActiveAfter,ActiveAfter=ActiveBefore };
    internal int Bytes {
        get {
            static long Item(StationItem? i)=>i==null?0:256L+2L*(i.Value.Length+i.Title.Length);
            static long Scene(WorkScene? s)=>s==null?0:512L+2L*(s.Name.Length+s.Description.Length)+s.Items.Sum(r=>128L+(r.Asset is {} a?2L*(a.Value.Length+a.Title.Length+a.Kind.Length):0));
            long total=Items.Sum(e=>Item(e.Before)+Item(e.After))+Scenes.Sum(e=>Scene(e.Before)+Scene(e.After))+Assets.Sum(e=>128L+32L*((e.Before?.StationIds.Length??0)+(e.After?.StationIds.Length??0)));
            return (int)Math.Min(int.MaxValue,total);
        }
    }
}

// Session-only inverse operations. Never snapshots or modifies physical source files.
internal sealed class StationHistory(StationStore station,WorkHub hub)
{
    readonly SemaphoreSlim gate=new(1,1);
    readonly List<(StationUndo Undo,int Bytes)> entries=[];
    internal bool Busy {get;private set;}
    internal int Version {get;private set;}
    internal bool CanUndo=>!Busy&&entries.Count>0;
    internal string Label=>entries.LastOrDefault().Undo?.Label??"";
    internal string? Notice {get;private set;}
    internal string Feedback {get;private set;}="";
    internal event Action? Changed;
    internal async Task Execute(string label,Func<Task> action)
    {
        await gate.WaitAsync();Busy=true;
        try {
            await hub.Flush();var before=station.Items.ToArray();var work=hub.Store.Data;
            try {await action();}
            finally {
                await hub.Flush();
                var delta=StationUndo.Between(label,before,station.Items.ToArray(),work,hub.Store.Data);
                if(!delta.Empty) {
                    int size=delta.Bytes;Notice=null;Feedback=label;
                    if(size>16*1024*1024){entries.Clear();Notice="本次内容较大，未保留会话撤销记录；原文件未改动。";}
                    else {entries.Add((delta,size));while(entries.Count>10||entries.Sum(e=>e.Bytes)>32*1024*1024)entries.RemoveAt(0);}
                    Version++;
                }
            }
        }
        finally {Busy=false;gate.Release();Changed?.Invoke();}
    }
    internal async Task Undo()
    {
        await gate.WaitAsync();Busy=true;
        try {
            if(entries.Count==0)return;
            await hub.Flush();var undo=entries[^1].Undo;
            // Validate both stores first; apply with concurrency checks and roll back the first write if necessary.
            bool sourceChanged=undo.Items.Length>0,workChanged=undo.Scenes.Length>0||undo.Assets.Length>0||undo.ActiveBefore!=undo.ActiveAfter;
            if(sourceChanged)station.ValidateUndo(undo);
            if(workChanged){if(hub.Store.ReadOnly)throw new IOException(hub.Store.Warning);WorkStore.Validate(undo.RestoreWork(hub.Store.Data));}
            using var pause=hub.SuspendCollection();
            if(sourceChanged)await station.ApplyUndo(undo);
            try {if(workChanged)await hub.Store.ApplyUndo(undo);}
            catch {if(sourceChanged)await station.ApplyUndo(undo.Reverse());throw;}
            entries.RemoveAt(entries.Count-1);Notice=null;Feedback="已撤销 · "+undo.Label;Version++;
        }
        finally {Busy=false;gate.Release();Changed?.Invoke();}
    }
}
