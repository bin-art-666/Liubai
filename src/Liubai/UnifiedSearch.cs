namespace Liubai;

internal enum SearchKind { Item, Group, Task, Note }
internal sealed record SearchHit(SearchKind Kind,Guid Id,string Title,string Detail,string Groups,DateTimeOffset Updated,int Score);
internal static class UnifiedSearch
{
    internal static SearchHit[] Find(string query,IReadOnlyList<StationItem> station,PlannerDocument planner,WorkDocument work,CancellationToken cancellation=default)
    {
        query=query.Trim();if(query.Length==0)return [];
        if(query.Length>200)query=query[..200];var terms=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Take(10).ToArray();
        var groups=new Dictionary<(WorkKind,Guid),List<string>>();
        foreach(var scene in work.Scenes)foreach(var item in scene.Items){cancellation.ThrowIfCancellationRequested();var key=(item.Kind,item.Id);if(!groups.TryGetValue(key,out var names))groups[key]=names=[];names.Add(scene.Name);}
        var hits=new List<SearchHit>();
        void Add(SearchKind kind,Guid id,string title,string content,DateTimeOffset updated,WorkKind? source=null)
        {
            cancellation.ThrowIfCancellationRequested();
            string context=source is {} type&&groups.TryGetValue((type,id),out var names)?string.Join("、",names.Distinct()):"";
            if(!terms.All(t=>title.Contains(t,StringComparison.OrdinalIgnoreCase)||content.Contains(t,StringComparison.OrdinalIgnoreCase)||context.Contains(t,StringComparison.OrdinalIgnoreCase)))return;
            int score=title.Equals(query,StringComparison.OrdinalIgnoreCase)?100:title.StartsWith(query,StringComparison.OrdinalIgnoreCase)?80:title.Contains(query,StringComparison.OrdinalIgnoreCase)?60:20;
            string snippet=content.Replace('\r',' ').Replace('\n',' ');int position=snippet.IndexOf(terms[0],StringComparison.OrdinalIgnoreCase);if(position>30)snippet="…"+snippet[Math.Max(0,position-25)..];
            if(snippet.Length>150)snippet=snippet[..150]+"…";
            hits.Add(new(kind,id,title.Length>120?title[..120]+"…":title,snippet,context,updated,score));
        }
        foreach(var item in station)Add(SearchKind.Item,item.Id,item.Title.Length>0?item.Title:item.Kind is StationKind.File or StationKind.Folder?Path.GetFileName(item.Value):item.Kind==StationKind.Link?new Uri(item.Value).IdnHost:item.Value.Split('\n')[0],item.Value,item.Created,WorkKind.Station);
        foreach(var scene in work.Scenes)Add(SearchKind.Group,scene.Id,scene.Name,scene.Description,scene.Updated);
        foreach(var task in planner.Tasks)Add(SearchKind.Task,task.Id,task.Title,(task.Completed?"已完成 · ":"待办 · ")+task.Detail+(task.Date is {} date?" · "+date.ToString("yyyy-MM-dd"):""),task.Updated,WorkKind.Task);
        foreach(var note in planner.Notes)Add(SearchKind.Note,note.Id,note.Text.Split('\n')[0] is {Length:>0} title?title:"图文记录",note.Text,note.Updated,WorkKind.Note);
        return hits.OrderByDescending(h=>h.Score).ThenByDescending(h=>h.Updated).ThenBy(h=>h.Kind).ThenBy(h=>h.Id).Take(100).ToArray();
    }
    internal static string Label(SearchKind kind)=>kind switch {SearchKind.Item=>"资料",SearchKind.Group=>"分组",SearchKind.Task=>"待办",_=>"笔记"};
}
