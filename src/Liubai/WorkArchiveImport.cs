using System.Security.Cryptography;
namespace Liubai;
internal static class WorkArchiveImport
{
    internal static async Task<Guid> Import(string rootDirectory, WorkArchivePackage package, StationStore station, PlannerStore planner, WorkStore work)
    {
        var scratch = Path.Combine(station.AttachmentDirectory,"import-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        var importedItems = new List<StationInput>(); var localByArchive = new Dictionary<string,string>();
        bool committed=false, rollbackSafe=true; var newStation = new List<StationItem>(); var tasks = new List<Guid>(); var notes = new List<Guid>();
        try
        {
            foreach (var entry in package.Files)
            {
                string target = Path.Combine(scratch,entry.Item.Id.ToString("N"),Path.GetFileName(entry.Path));
                if (entry.Item.Kind==StationKind.Folder) Directory.CreateDirectory(target); else Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                foreach(string relative in entry.Directories??[]) {
                    string nested=Path.GetFullPath(Path.Combine(target,relative));
                    if(!nested.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("目录越界。");
                    Directory.CreateDirectory(nested);
                }
                foreach (var hash in entry.Files)
                {
                    string source = WorkArchive.Resolve(rootDirectory,hash.Path.Length==0?entry.Path:Path.Combine(entry.Path,hash.Path));
                    string output = hash.Path.Length==0?target:Path.GetFullPath(Path.Combine(target,hash.Path));
                    if (output!=target&&!output.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("归档路径越界。");
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await using (var input=File.OpenRead(source)) await using (var destination=File.Create(output)) await input.CopyToAsync(destination);
                    await using var verify = File.OpenRead(output);
                    if (verify.Length!=hash.Size || !Convert.ToHexString(await SHA256.HashDataAsync(verify)).Equals(hash.Sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("复制校验失败。");
                }
                localByArchive.Add(entry.Path,target);
            }
            foreach (var item in package.Items) importedItems.Add(new(item.Kind,item.Kind is StationKind.File or StationKind.Folder ? localByArchive[item.Value] : item.Value,item.Title));
            var taskIds=package.Tasks.ToDictionary(t=>t.Id,_=>Guid.NewGuid()); var noteIds=package.Notes.ToDictionary(n=>n.Id,_=>Guid.NewGuid());
            var importedTasks=package.Tasks.Select(t=>t with { Id=taskIds[t.Id], SourceNote=t.SourceNote is { } n&&noteIds.TryGetValue(n,out var mapped)?mapped:null, RemindBefore=null, NotifiedAt=null, SnoozeUntil=null, ReminderToken=Guid.NewGuid() }).ToArray();
            var importedNotes=package.Notes.Select(n=>n with { Id=noteIds[n.Id] }).ToArray();
            // Scratch validation does not dispatch reminders or attach to the app's tracking hub.
            var validateStation=new StationStore(Path.Combine(scratch,"validate-station.json")); await validateStation.UpdateOptions(2000,"",false); await validateStation.Add(importedItems);
            var validatePlanner=new PlannerStore(Path.Combine(scratch,"validate-planner.json")); foreach(var task in importedTasks) await validatePlanner.RestoreTask(task); foreach(var note in importedNotes) await validatePlanner.RestoreNote(note);
            var resumes=package.Resumes.Select(r=>r with { TaskId=taskIds[r.TaskId] }).ToArray(); var sessions=package.Sessions.Select(s=>s with { TaskId=taskIds[s.TaskId] }).ToArray();
            var images=package.Images.Select(i=>new NoteImage(noteIds[i.NoteId],localByArchive[i.Path])).ToArray();
            var validateWork=new WorkStore(Path.Combine(scratch,"validate-work.json")); await validateWork.RestoreProgress(resumes,sessions,images);
            string name=package.Name; int suffix=2;
            while(work.Data.Scenes.Any(s=>s.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) name=package.Name[..Math.Min(65,package.Name.Length)]+$"（导入 {suffix++}）";
            var before=station.Items.Select(i=>i.Id).ToHashSet();
            try
            {
                await station.Add(importedItems); newStation.AddRange(station.Items.Where(i=>!before.Contains(i.Id)));
                foreach(var task in importedTasks) { await planner.RestoreTask(task); tasks.Add(task.Id); }
                foreach(var note in importedNotes) { await planner.RestoreNote(note); notes.Add(note.Id); }
                var refs=importedItems.Select(input=>station.Items.First(i=>i.Kind==input.Kind&&i.Value==input.Value)).Select(i=>new WorkRef(WorkKind.Station,i.Id))
                    .Concat(tasks.Select(id=>new WorkRef(WorkKind.Task,id))).Concat(notes.Select(id=>new WorkRef(WorkKind.Note,id))).ToArray();
                var stationIds=package.Items.Select((item,index)=>(item.Id,NewId:station.Items.First(i=>i.Kind==importedItems[index].Kind&&i.Value==importedItems[index].Value).Id)).ToDictionary(i=>i.Id,i=>i.NewId);
                var assets=(package.TaskAssets??[]).Select(l=>new WorkTaskAssets(taskIds[l.TaskId],l.StationIds.Select(id=>stationIds[id]).ToArray())).ToArray();
                Guid scene=await work.ImportScene(name,package.Description,refs,resumes,sessions,images,assets);
                committed=true; return scene;
            }
            catch
            {
                try { await station.RemoveArchived(newStation); foreach(var id in tasks) await planner.DeleteTask(id); foreach(var id in notes) await planner.DeleteNote(id); }
                catch { rollbackSafe=false; throw new IOException("导入未完成，已保留导入目录供恢复。原有记录未覆盖。"); }
                throw;
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(scratch,"validate-*.json*")) File.Delete(file);
            if(!committed && rollbackSafe) Directory.Delete(scratch,true);
        }
    }
}
