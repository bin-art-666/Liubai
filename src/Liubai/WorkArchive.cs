using System.Security.Cryptography;
using System.Text.Json;

namespace Liubai;

internal sealed record WorkArchivePackage(int Version, string Name, string Description, StationItem[] Items, TodoItem[] Tasks, QuickNote[] Notes,
    WorkResume[] Resumes, WorkSession[] Sessions, ArchivedItem[] Files, NoteImage[] Images, WorkTaskAssets[]? TaskAssets = null);

internal static class WorkArchive
{
    internal static async Task<string> Save(WorkScene scene, StationStore station, PlannerStore planner, WorkStore work, string destination,
        IProgress<ArchiveProgress>? progress = null, CancellationToken token = default)
    {
        var refs = scene.Items.Select(i=>(i.Kind,i.Id)).ToHashSet();
        var taskAssets=(work.Data.TaskAssets??[]).Where(l=>refs.Contains((WorkKind.Task,l.TaskId))).ToArray();
        var materialIds=taskAssets.SelectMany(l=>l.StationIds).ToHashSet();
        var items = station.Items.Where(i=>refs.Contains((WorkKind.Station,i.Id))||materialIds.Contains(i.Id)).ToArray();
        // Missing references must not silently turn a partial package into a complete backup.
        if (scene.Items.Any(r=>r.Kind == WorkKind.Station && items.All(i=>i.Id!=r.Id))) throw new IOException("分组包含失效的资料引用，请先整理。");
        var tasks = planner.Data.Tasks.Where(t=>refs.Contains((WorkKind.Task,t.Id))).ToArray();
        var notes = planner.Data.Notes.Where(n=>refs.Contains((WorkKind.Note,n.Id))).ToArray();
        if (tasks.Length+notes.Length != scene.Items.Count(r=>r.Kind!=WorkKind.Station)) throw new IOException("分组包含已移除的任务或笔记，请先解除关联。");
        var images = work.Data.Images.Where(i=>notes.Any(n=>n.Id==i.NoteId)).ToArray();
        var imageItems = images.Select(i=>new StationItem(Guid.NewGuid(),StationKind.File,i.Path,"笔记图片",DateTimeOffset.Now)).ToArray();
        var all = items.Concat(imageItems).ToArray();
        string root;
        ArchivedItem[] saved;
        if (all.Length > 0)
        {
            var result = await StationArchive.Save(all,destination,progress,token,finalize:false); root = result.Directory;
            if (result.Canceled) throw new OperationCanceledException(token);
            if (result.Failed.Length > 0) throw new IOException("部分资料未能归档，分组未标记为完成："+root);
            saved = result.Saved;
            // Keep group packages visibly incomplete until all companion records are durable.
            string incomplete = Path.Combine(Path.GetDirectoryName(root)!,"未完成_工作分组_"+Path.GetFileName(root));
            Directory.Move(root,incomplete); root = incomplete;
        }
        else { root = Path.Combine(destination,"未完成_工作分组_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); saved = []; }
        token.ThrowIfCancellationRequested();
        var relocated = items.Select(i=>i.Kind is StationKind.File or StationKind.Folder ? i with { Value = saved.Single(s=>s.Item.Id==i.Id).Path } : i).ToArray();
        var relocatedImages = images.Select((image,index)=>image with { Path = saved.Single(s=>s.Item.Id==imageItems[index].Id).Path }).ToArray();
        var package = new WorkArchivePackage(1,scene.Name,scene.Description,relocated,tasks,notes,
            work.Data.Resumes.Where(r=>tasks.Any(t=>t.Id==r.TaskId)).ToArray(),(work.Data.Sessions??[]).Where(s=>tasks.Any(t=>t.Id==s.TaskId)).ToArray(),saved,relocatedImages,taskAssets.Select(l=>l with { StationIds=l.StationIds.Where(id=>items.Any(i=>i.Id==id)).ToArray() }).ToArray());
        string manifest = Path.Combine(root,"workspace.json");
        string json=JsonSerializer.Serialize(package);
        if(System.Text.Encoding.UTF8.GetByteCount(json)>16*1024*1024)throw new InvalidDataException("分组归档清单过大，请拆分分组后归档。");
        await File.WriteAllTextAsync(manifest+".tmp",json,token); token.ThrowIfCancellationRequested();
        File.Move(manifest+".tmp",manifest);
        string complete = Path.Combine(Path.GetDirectoryName(root)!,"工作分组_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]);
        Directory.Move(root,complete); return complete;
    }
    internal static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("归档路径无效。");
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root,relative));
        if (!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || relative.Contains(':')) throw new InvalidDataException("归档路径越界。");
        string? current = full;
        while (current != null && (current+Path.DirectorySeparatorChar).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
        { if ((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("归档包含链接，未跟随。"); current=Path.GetDirectoryName(current); }
        if ((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("归档目录不能是链接。");
        return full;
    }
    internal static async Task<WorkArchivePackage> Read(string root, CancellationToken token = default)
    {
        string file = Resolve(root,"workspace.json");
        if (new FileInfo(file).Length > 16*1024*1024) throw new InvalidDataException("分组清单过大。");
        var p = JsonSerializer.Deserialize<WorkArchivePackage>(await File.ReadAllTextAsync(file,token)) ?? throw new InvalidDataException();
        if (p.Version != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>80 || p.Description == null || p.Description.Length>2000
            || p.Items == null || p.Tasks == null || p.Notes == null || p.Resumes == null || p.Sessions == null || p.Files == null || p.Images == null
            || p.Items.Length>2000 || p.Tasks.Length>2000 || p.Notes.Length>2000 || p.Files.Length>12000 || p.Images.Length>10000
            || p.Resumes.Length>500 || p.Sessions.Length>2000 || p.Items.Any(i=>i==null||!Enum.IsDefined(i.Kind))||p.Tasks.Any(t=>t==null)||p.Notes.Any(n=>n==null)||p.Images.Any(i=>i==null)) throw new InvalidDataException("分组格式无效。");
        foreach (var saved in p.Files)
        {
            if (saved == null || saved.Files == null || saved.Files.Length>100000) throw new InvalidDataException();
            string entry = Resolve(root,saved.Path);
            if(saved.Directories is {} dirs) {
                if(dirs.Length>100000||saved.Item.Kind!=StationKind.Folder&&dirs.Length>0)throw new InvalidDataException();
                foreach(string dir in dirs) { if(Path.IsPathRooted(dir)||!Directory.Exists(Resolve(root,Path.Combine(saved.Path,dir))))throw new InvalidDataException("归档目录无效。"); }
            }
            foreach (var hash in saved.Files)
            {
                token.ThrowIfCancellationRequested();
                string path = hash.Path.Length==0 ? entry : Resolve(root,Path.Combine(saved.Path,hash.Path));
                await using var stream = File.OpenRead(path);
                if (stream.Length!=hash.Size || !Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(hash.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("归档文件校验失败。");
            }
        }
        foreach (var item in p.Items.Where(i=>i.Kind is StationKind.File or StationKind.Folder))
            if (!p.Files.Any(f=>f.Item.Id==item.Id&&f.Path==item.Value)) throw new InvalidDataException("资料没有归档校验记录。");
        foreach (var image in p.Images) if (!p.Files.Any(f=>f.Path==image.Path)) throw new InvalidDataException("图片没有归档校验记录。");
        if(p.Items.Select(i=>i.Id).Distinct().Count()!=p.Items.Length || p.Files.Select(i=>i.Item.Id).Distinct().Count()!=p.Files.Length
            || (p.TaskAssets??[]).Any(l=>l==null||l.StationIds==null||!p.Tasks.Any(t=>t.Id==l.TaskId)||l.StationIds.Any(id=>!p.Items.Any(i=>i.Id==id)))) throw new InvalidDataException("归档关联无效。");
        return p;
    }
}
