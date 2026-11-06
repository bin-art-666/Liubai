using Liubai;
using System.IO.Compression;
using System.Text.Json;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Reject(Action action, string name)
{ try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException or FormatException or OperationCanceledException) { checks++; return; } throw new Exception(name); }
string Release(string tag = "v0.7.0", bool draft = false, bool pre = false, string? digest = null, string? download = null, long size = 100) => JsonSerializer.Serialize(new {
    tag_name = tag, draft, prerelease = pre, html_url = UpdatePolicy.Repository + "/releases/tag/" + tag, body = "更新说明",
    assets = new[] { new { name = $"Liubai-{tag}-win-x64.zip", browser_download_url = download ?? UpdatePolicy.Repository + $"/releases/download/{tag}/Liubai-{tag}-win-x64.zip", size, digest = digest ?? "sha256:" + new string('a',64) } }
});
Check(UpdatePolicy.VersionOf("v0.7.0") > UpdatePolicy.VersionOf("v0.6.9"), "Version order");
Check(UpdatePolicy.VersionOf("v1.0.0") > UpdatePolicy.VersionOf("0.99.99"), "Major order");
foreach (string tag in new[] { "v0.7.0-beta", "v01.2.3", "v1.2", "1.2.3.4", "../v1.2.3", "V1.2.3", "v-1.2.3" }) Reject(() => UpdatePolicy.VersionOf(tag), "Bad version accepted");
var release = UpdatePolicy.Parse(Release());
Check(release.Notes == "更新说明" && release.Sha256.Length == 64 && release.Version == new Version(0,7,0), "Release fields");
Reject(() => UpdatePolicy.Parse(Release(draft:true)), "Draft");
Reject(() => UpdatePolicy.Parse(Release(pre:true)), "Prerelease");
Reject(() => UpdatePolicy.Parse(Release(digest:"")), "Missing hash");
Reject(() => UpdatePolicy.Parse(Release(digest:"sha256:abc")), "Bad hash");
Reject(() => UpdatePolicy.Parse(Release(download:"https://example.com/a.zip")), "Foreign release asset");
Reject(() => UpdatePolicy.Parse(Release().Replace("releases/tag/v0.7.0","releases/tag/v0.6.0")), "Foreign release page");
Reject(() => UpdatePolicy.Parse(Release(size:0)), "Empty package");
Reject(() => UpdatePolicy.Parse(Release(size:UpdatePolicy.MaximumDownload+1)), "Oversize package");
foreach (string url in new[]{"http://github.com/a","https://evil.example/a","https://github.com.evil.example/a","https://user@github.com/a","https://github.com:444/a"}) Check(!UpdatePolicy.DownloadHost(new(url)), "Unsafe redirect");
Check(UpdatePolicy.DownloadHost(new("https://release-assets.githubusercontent.com/a")), "GitHub asset redirect");
string root = Path.Combine(Path.GetFullPath(".build"), "update-checks-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string MakeZip(string name, IEnumerable<string> names, bool symlink = false)
    {
        string path = Path.Combine(root,name+".zip");
        using var zip = ZipFile.Open(path,ZipArchiveMode.Create);
        foreach(string entryName in names) {var entry=zip.CreateEntry(entryName);if(symlink)entry.ExternalAttributes=unchecked((int)0xA1FF0000);using var writer=new StreamWriter(entry.Open());writer.Write("fixture "+entryName);}
        return path;
    }
    string valid = MakeZip("valid",UpdatePolicy.Required);
    var actual = release with {Size=new FileInfo(valid).Length,Sha256=UpdatePolicy.Hash(valid)};
    UpdatePolicy.VerifyPackage(valid,actual); Check(true,"Valid digest");
    Reject(()=>UpdatePolicy.VerifyPackage(valid,actual with{Sha256=new string('b',64)}),"Hash mismatch");
    Reject(()=>UpdatePolicy.VerifyPackage(valid,actual with{Size=actual.Size+1}),"Length mismatch");
    string unpacked=Path.Combine(root,"runtime");UpdatePolicy.Extract(valid,unpacked,default);
    Check(UpdatePolicy.Required.All(n=>File.Exists(Path.Combine(unpacked,n))),"Root archive extraction");
    string wrapped=MakeZip("wrapped",UpdatePolicy.Required.Select(n=>"Liubai/"+n));
    string wrappedRoot=Path.Combine(root,"wrapped");UpdatePolicy.Extract(wrapped,wrappedRoot,default);
    Check(File.Exists(Path.Combine(wrappedRoot,"Liubai.exe")),"Single wrapped directory");
    int index=0;
    foreach(string bad in new[]{"../escape.txt","..\\escape.txt","/absolute.txt","C:/escape.txt","folder/../../escape.txt","folder/a:ads","CON.txt","folder/a.","folder/a ","folder//a","folder/./a"})
    {
        string zip=MakeZip("bad"+(index++),UpdatePolicy.Required.Append(bad));
        Reject(()=>UpdatePolicy.Extract(zip,Path.Combine(root,"bad-out-"+index),default),"Unsafe ZIP entry");
    }
    Check(!File.Exists(Path.Combine(root,"escape.txt")),"No traversal output");
    string duplicate=MakeZip("duplicate",UpdatePolicy.Required.Append("liubai.EXE"));
    Reject(()=>UpdatePolicy.Extract(duplicate,Path.Combine(root,"duplicate-out"),default),"Case collision");
    string symbolic=MakeZip("link",UpdatePolicy.Required,true);
    Reject(()=>UpdatePolicy.Extract(symbolic,Path.Combine(root,"link-out"),default),"ZIP symlink");
    string missing=MakeZip("missing",UpdatePolicy.Required.Skip(1));
    Reject(()=>UpdatePolicy.Extract(missing,Path.Combine(root,"missing-out"),default),"Missing runtime");
    Reject(()=>UpdatePolicy.Extract(valid,unpacked,default),"Overwrite existing extraction");
    using var cancel=new CancellationTokenSource();cancel.Cancel();string canceled=Path.Combine(root,"canceled");
    Reject(()=>UpdatePolicy.Extract(valid,canceled,cancel.Token),"Cancellation");
    Check(!Directory.Exists(canceled),"Canceled extraction removed");
    var layout=new UpdateLayout(unpacked);layout.EnsureOwned();layout.EnsureOwned();
    var driveRoot=new UpdateLayout(Path.GetPathRoot(root)!);
    Check(!driveRoot.Supported,"Disk root keeps settings usable but disables in-place updates");
    Reject(()=>driveRoot.EnsureOwned(),"Root-directory installs cannot create update state");
    Check(Directory.Exists(layout.Root) && Path.GetDirectoryName(layout.Root)==root,"Update folder ownership/idempotency");
    File.WriteAllText(Path.Combine(layout.Root,"owner.json"),JsonSerializer.Serialize("foreign"));
    Reject(()=>layout.EnsureOwned(),"Foreign owner");
    string data=Path.Combine(root,"data");Directory.CreateDirectory(data);Directory.CreateDirectory(Path.Combine(data,"browser"));
    File.WriteAllText(Path.Combine(data,"planner.json"),"records");File.WriteAllText(Path.Combine(data,"planner.json.bak"),"backup");
    File.WriteAllText(Path.Combine(data,"browser","cookie.json"),"secret");File.WriteAllText(Path.Combine(data,"other.txt"),"not settings");
    string snapshot=Path.Combine(root,"snapshot");UpdateLayout.Snapshot(data,snapshot);
    Check(File.ReadAllText(Path.Combine(snapshot,"planner.json"))=="records" && File.Exists(Path.Combine(snapshot,"planner.json.bak")),"Snapshot settings and backup");
    Check(Directory.GetFiles(snapshot).Length==2 && !Directory.Exists(Path.Combine(snapshot,"browser")),"No browser profile or external files");
    Reject(()=>UpdateLayout.Snapshot(data,snapshot),"Snapshot never overwrites");
    Check(File.ReadAllText(Path.Combine(data,"planner.json"))=="records","Original records preserved");
    App.DataDirectory=data;
    string networkRelease=Release(digest:"sha256:"+actual.Sha256,size:actual.Size);
    await ServiceChecks.Run(root,networkRelease,valid,Check);
    Console.WriteLine($"PASS: {checks} update policy, network, cancellation, package, hash, extraction and backup checks");
}
finally {Directory.Delete(root,true);}
