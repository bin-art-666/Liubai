using Liubai;
using System.Text.Json;

string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".build", "station-checks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Reject(Func<Task> action)
{
    bool failed = false;
    try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { failed = true; }
    Check(failed, "Invalid or failed mutation accepted");
}
try
{
    string path = Path.Combine(root, "station.json"); var store = new StationStore(path);
    Check(store.Items.Count == 0 && !File.Exists(path), "Opening empty store should not write");
    Check(store.Capacity == 200 && store.RemoveAfterArchive && store.ArchiveDirectory == "", "Default station options");
    var layout = new StationPanelLayout(store.WindowLayoutPath);
    Check(layout.Data.Width == 430 && layout.Data.Height == 660 && !File.Exists(store.WindowLayoutPath), "Layout defaults do not modify item storage");
    var geometry = new StationPanelOptions(Width: 600, Height: 580, RightOffset: 30, TopOffset: 40, Floating: new(100, 100, 800, 700));
    layout.Save(geometry);
    Check(new StationPanelLayout(store.WindowLayoutPath).Data == geometry && !File.Exists(path), "Dock and floating layouts persist separately from items");
    var rect = StationPanelPolicy.Bounds(geometry, new(0, 24, 1920, 1200), 24, 144);
    Check(rect.Width == 900 && rect.Height == 870 && rect.X + rect.Width == 1875 && rect.Y == 84, "All edges use DPI-aware saved offsets");
    rect = StationPanelPolicy.Bounds(geometry with { RightOffset = 20000, TopOffset = 20000 }, new(-300, 24, 300, 200), 24, 192);
    Check(rect == new StationPanelRect(-300, 24, 300, 200), "Small screen clamps entire station into work area");
    foreach (var invalid in new[] { geometry with { Width = 359 }, geometry with { Height = 1201 }, geometry with { Width = double.NaN }, geometry with { TopOffset = -1 }, geometry with { RightOffset = double.PositiveInfinity }, geometry with { Floating = new(0, 0, 0, 0) } })
    {
        bool rejected = false; try { layout.Save(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && layout.Data == geometry, "Invalid window geometry must preserve saved settings");
    }
    string layoutText = File.ReadAllText(store.WindowLayoutPath);
    using (var locked = new FileStream(store.WindowLayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        bool rejected = false; try { layout.Save(geometry with { Width = 650 }); } catch (IOException) { rejected = true; }
        Check(rejected && layout.Data == geometry, "Failed geometry persistence leaves live state intact");
    }
    Check(File.ReadAllText(store.WindowLayoutPath) == layoutText && !File.Exists(store.WindowLayoutPath + ".tmp"), "Failed save preserves disk and cleans temporary file");
    var zone = TimeZoneInfo.CreateCustomTimeZone("StationUTC8", TimeSpan.FromHours(8), "StationUTC8", "StationUTC8");
    var now = new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.FromHours(8));
    StationItem Item(StationKind kind, string value, int days, string title = "") => new(Guid.NewGuid(), kind, value, title, now.AddDays(-days));
    var queryItems = new[] {
        Item(StationKind.File, Path.Combine(root,"SCREEN.JPG"),0), Item(StationKind.File,Path.Combine(root,"Report.PDF"),1),
        Item(StationKind.Folder,Path.Combine(root,"folder.png"),0), Item(StationKind.Text,"备忘今天",0), Item(StationKind.Text,"备忘上周",7),
        Item(StationKind.Link,"https://example.com/image.png",3,"项目链接"), Item(StationKind.Text,"昨天末尾",0) with { Created = now.AddHours(-2) }, Item(StationKind.Text,"未来",-1) };
    Check(StationQuery.Classify(queryItems[0]) == StationCategory.Images && StationQuery.Classify(queryItems[1]) == StationCategory.Files, "Image classification is case insensitive and excludes normal files");
    Check(StationQuery.Classify(queryItems[2]) == StationCategory.Folders && StationQuery.Classify(queryItems[5]) == StationCategory.Links, "Image-like folders and URLs retain their actual type");
    Check(new StationQuery(StationCategory.Files).Apply(queryItems,now,zone).Length == 1 && new StationQuery(StationCategory.Images).Apply(queryItems,now,zone).Length == 1, "Image and file categories do not overlap");
    Check(new StationQuery(Period:StationPeriod.Today).Apply(queryItems,now,zone).Length == 3, "Today uses local calendar boundaries, excluding yesterday UTC and future dates");
    Check(new StationQuery(Period:StationPeriod.Last7Days).Apply(queryItems,now,zone).Length == 6, "Seven-day filter includes today plus six previous days");
    Check(new StationQuery(StationCategory.Text,StationPeriod.Today,"备忘").Apply(queryItems,now,zone).Length == 1, "Category, date and text search combine");
    Check(new StationQuery(Search:"report").Apply(queryItems,now,zone).Single().Id == queryItems[1].Id, "Filename search is case insensitive");
    Check(new StationQuery(Search:"项目").Apply(queryItems,now,zone).Single().Id == queryItems[5].Id, "Link titles are searchable");
    Check(new StationQuery(Search:"example.com").Apply(queryItems,now,zone).Single().Id == queryItems[5].Id, "Link addresses are searchable");
    Check(StationQuery.Group(queryItems[6].Created,now,zone) == "昨天" && StationQuery.TimeLabel(queryItems[0].Created,now,zone).StartsWith("今天"), "Date grouping and timestamps use local time");
    var ordered = new StationQuery().Apply(queryItems,now,zone); Check(ordered[0].Created == now.AddDays(1), "Default sort is descending stored timestamp");
    var view = new StationQuery(StationCategory.Links,StationPeriod.Last30Days,"项目"); layout.Save(geometry with { View = view });
    Check(new StationPanelLayout(store.WindowLayoutPath).Data.View == view, "Expanded view preferences survive restart");
    var scopeStore = new StationStore(Path.Combine(root,"scope.json"));
    await scopeStore.Add([StationStore.Text("保留文字"),StationStore.Text("https://example.com/scope")]);
    var scoped = new StationQuery(StationCategory.Text).Apply(scopeStore.Items,DateTimeOffset.Now,TimeZoneInfo.Local);
    await scopeStore.Add([StationStore.Text("确认期间新增文字")]);
    await scopeStore.RemoveArchived(scoped);
    Check(scopeStore.Items.Count == 2 && scopeStore.Items.Any(i=>i.Kind==StationKind.Link) && scopeStore.Items.Any(i=>i.Value=="确认期间新增文字"), "Snapshot removal preserves other categories and records added after confirmation began");
    File.WriteAllText(Path.Combine(root, "legacy.json"), "{\"Version\":1,\"Items\":[]}");
    Check(new StationStore(Path.Combine(root, "legacy.json")).Capacity == 200, "Legacy migration default");
    await store.UpdateOptions(30, "", true);
    string file = Path.Combine(root, "sample.txt"), folder = Path.Combine(root, "folder");
    File.WriteAllText(file, "original source content"); Directory.CreateDirectory(folder);
    await store.Add([new(StationKind.File, file), new(StationKind.Folder, folder), StationStore.Text("  原样文字\n第二行  "), StationStore.Text("https://example.com", "Example")]);
    Check(store.Items.Count == 4, "Supported types");
    Check(store.Items.Single(i => i.Kind == StationKind.Text).Value == "  原样文字\n第二行  ", "Text whitespace changed");
    Check(store.Items.Single(i => i.Kind == StationKind.Link).Value == "https://example.com/", "URL normalization");
    Check(new StationStore(path).Items.SequenceEqual(store.Items), "Restart recovery");
    await store.UpdateOptions(2, Path.Combine(root, "archives"), false);
    Check(store.Items.Count == 4 && new StationStore(path).Items.Count == 4 && new StationStore(path).Capacity == 2, "Lowering limit removed records or prevented recovery");
    await Reject(() => store.Add([StationStore.Text("excess")]));
    Check((await store.Add([StationStore.Text("https://example.com/")])).Duplicates == 1, "Duplicates failed above new limit");
    Check(!new StationStore(path).RemoveAfterArchive && new StationStore(path).ArchiveDirectory == Path.Combine(root, "archives"), "Archive options did not persist");
    await Reject(() => store.UpdateOptions(0, "", true)); await Reject(() => store.UpdateOptions(2001, "", true));
    await Reject(() => store.UpdateOptions(200, "relative/path", true));
    using (var lockedOptions = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        await Reject(() => store.UpdateOptions(999, "", true));
    Check(store.Capacity == 2 && !store.RemoveAfterArchive && new StationStore(path).Capacity == 2, "Failed option persistence leaked live settings");
    await store.UpdateOptions(30, "", true);
    var duplicate = await store.Add([new(StationKind.File, file.ToUpperInvariant()), StationStore.Text("https://example.com/"), StationStore.Text("  原样文字\n第二行  ")]);
    Check(duplicate.Added == 0 && duplicate.Duplicates == 3 && store.Items.Count == 4, "Deduplication");
    await store.Remove(store.Items.First(i => i.Kind == StationKind.File).Id);
    Check(File.ReadAllText(file) == "original source content", "Remove changed source file");
    await store.Clear(); Check(store.Items.Count == 0 && Directory.Exists(folder) && File.Exists(file), "Clear deleted sources");
    await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.Add([StationStore.Text("item" + i)])));
    Check(store.Items.Count == 20 && new StationStore(path).Items.Count == 20, "Concurrent writes lost data");
    await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.Add([StationStore.Text("same")])));
    Check(store.Items.Count == 21, "Concurrent duplicate inserts");
    await store.Add(Enumerable.Range(21, 9).Select(i => StationStore.Text("item" + i)));
    Check(store.Items.Count == 30, "Capacity");
    var before = File.ReadAllBytes(path);
    await Reject(() => store.Add([StationStore.Text("overflow"), StationStore.Text("item1")]));
    Check(File.ReadAllBytes(path).SequenceEqual(before) && store.Items.Count == 30, "Capacity failure partially applied batch");
    Check((await store.Add([StationStore.Text("same")])).Duplicates == 1, "Duplicates rejected at capacity");
    await store.Clear();
    await Reject(() => store.Add([StationStore.Text(new string('x', 20001))]));
    await Reject(() => store.Add([StationStore.Text(" ")]));
    await Reject(() => store.Add([new(StationKind.Link, "javascript:alert(1)")]));
    await Reject(() => store.Add([new(StationKind.File, "relative/file.txt")]));
    await Reject(() => store.Add([new((StationKind)99, "invalid")]));
    await Reject(() => store.Add(Enumerable.Range(0, 31).Select(i => StationStore.Text("batch" + i))));
    Check(store.Items.Count == 0, "Invalid batch changed state");
    await store.Add([StationStore.Text("before write failure")]);
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        await Reject(() => store.Add([StationStore.Text("must not appear")]));
    Check(store.Items.Count == 1 && new StationStore(path).Items.Count == 1 && !File.Exists(path + ".tmp"), "Failed persistence leaked live state/temp file");
    await store.Add([StationStore.Text("after failure")]); Check(store.Items.Count == 2, "Cannot retry after failure");
    var backup = File.ReadAllBytes(path + ".bak"); File.WriteAllText(path, "{broken");
    var recovered = new StationStore(path);
    Check(!recovered.ReadOnly && recovered.Items.Count == 1 && recovered.LoadWarning != null, "Backup recovery failed");
    await recovered.Add([StationStore.Text("recovered")]);
    Check(File.ReadAllBytes(path + ".bak").SequenceEqual(backup) && new StationStore(path).Items.Count == 2, "Recovery overwrote good backup");
    File.Delete(path); var missing = new StationStore(path);
    Check(!missing.ReadOnly && missing.Items.Count == 1, "Missing primary recovery");
    await missing.Add([StationStore.Text("restore missing")]); Check(File.Exists(path), "Primary not recreated");
    File.WriteAllText(path, "{broken"); File.WriteAllText(path + ".bak", "{broken"); var broken = new StationStore(path);
    Check(broken.ReadOnly, "Both corrupt must be read-only"); await Reject(() => broken.Clear());
    Check(File.ReadAllText(path) == "{broken", "Corrupt data overwritten");
    File.WriteAllText(path, "{\"Version\":99,\"Items\":[]}"); var future = new StationStore(path);
    Check(future.ReadOnly, "Future format must be read-only"); await Reject(() => future.Add([StationStore.Text("bad")]));
    Check(File.ReadAllText(path).Contains("99"), "Future data overwritten");
    File.Delete(path + ".bak"); File.WriteAllText(path, JsonSerializer.Serialize(new StationDocument(1, [new(Guid.NewGuid(), StationKind.File, "relative", "", DateTimeOffset.Now)])));
    Check(new StationStore(path).ReadOnly, "Invalid persisted item accepted");
    await CheckArchives();
    Console.WriteLine($"PASS: {checks} station persistence, capacity, deduplication, source protection and recovery checks");
}

finally
{
    string expected = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".build")) + Path.DirectorySeparatorChar;
    if (root.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) Directory.Delete(root, true);
}

async Task CheckArchives()
{
    string source = Path.Combine(root, "archive-source"); Directory.CreateDirectory(source);
    string file = Path.Combine(source, "重复.txt"); await File.WriteAllTextAsync(file, "original file 你好");
    string nested = Path.Combine(source, "folder"); Directory.CreateDirectory(Path.Combine(nested, "empty"));
    await File.WriteAllTextAsync(Path.Combine(nested, "nested.txt"), "nested");
    var store = new StationStore(Path.Combine(root, "archive-store.json"));
    await store.Add([new(StationKind.File, file), new(StationKind.Folder, nested), StationStore.Text("原样文字\r\n第二行  ", "重复"), StationStore.Text("https://example.com/a?b=1&c=2", "重复")]);
    var snapshot = store.Items.ToArray();
    var result = await StationArchive.Save(snapshot, Path.Combine(root, "archives"));
    Check(!result.Canceled && result.Failed.Length == 0 && result.Saved.Length == 4, "Archive supported types");
    Check(store.Items.Count == 4 && File.ReadAllText(file) == "original file 你好", "Archive changed source/store before removal");
    Check(File.ReadAllText(Path.Combine(result.Directory, result.Saved.Single(s => s.Item.Kind == StationKind.Text).Path)) == "原样文字\r\n第二行  ", "Archive text changed");
    Check(File.ReadAllText(Path.Combine(result.Directory, result.Saved.Single(s => s.Item.Kind == StationKind.Link).Path)).Contains("URL=https://example.com/a?b=1&c=2"), "URL shortcut wrong");
    string folder = Path.Combine(result.Directory, result.Saved.Single(s => s.Item.Kind == StationKind.Folder).Path);
    Check(Directory.Exists(Path.Combine(folder, "empty")) && File.ReadAllText(Path.Combine(folder, "nested.txt")) == "nested", "Folder archive incomplete");
    Check(File.Exists(Path.Combine(result.Directory, "index.json")) && File.Exists(Path.Combine(result.Directory, "归档目录.txt")), "Missing archive manifest");
    Check(result.Saved.Select(s => s.Path).Distinct().Count() == 4 && result.Saved.All(s => s.Files.All(f => f.Sha256.Length == 64)), "Collision/hash metadata");
    await store.Add([StationStore.Text("added during archive")]);
    Check(await store.RemoveArchived(result.Saved.Select(s => s.Item)) == 4 && store.Items.Single().Value == "added during archive", "Removal touched concurrent new record");
    await store.Add([new(StationKind.File, file), new(StationKind.File, Path.Combine(source, "missing.txt"))]);
    var partial = await StationArchive.Save(store.Items.ToArray(), Path.Combine(root, "archives"));
    Check(partial.Saved.Length == 2 && partial.Failed.Length == 1 && !partial.Canceled, "Partial failure incorrectly succeeded");
    await store.RemoveArchived(partial.Saved.Select(s => s.Item));
    Check(store.Items.Single().Value.EndsWith("missing.txt"), "Failed record removed");
    var recursive = await StationArchive.Save([snapshot.Single(i => i.Kind == StationKind.Folder)], nested);
    Check(recursive.Failed.Length == 1 && recursive.Saved.Length == 0, "Recursive destination allowed");
    using var cancel = new CancellationTokenSource();
    var canceled = await StationArchive.Save(snapshot, Path.Combine(root, "archives"), new CancelArchiveProgress(cancel), cancel.Token);
    Check(canceled.Canceled && Path.GetFileName(canceled.Directory).StartsWith("未完成_") && File.ReadAllText(Path.Combine(canceled.Directory, "index.json")).Contains("Status"), "Canceled archive not marked incomplete");
    string unavailable = Path.Combine(root, "blocked-destination"); File.WriteAllText(unavailable, "not a directory");
    await Reject(() => StationArchive.Save(snapshot, unavailable));
    Check(File.ReadAllText(file) == "original file 你好" && File.Exists(Path.Combine(nested, "nested.txt")), "Failure/cancel changed originals");
    var maximum = new StationStore(Path.Combine(root, "maximum.json")); await maximum.UpdateOptions(2000, "", true);
    await maximum.Add(Enumerable.Range(0, 2000).Select(i => StationStore.Text("entry" + i)));
    Check(new StationStore(Path.Combine(root, "maximum.json")).Items.Count == 2000, "Maximum custom capacity/restart failed");
}

sealed class CancelArchiveProgress(CancellationTokenSource cancel) : IProgress<ArchiveProgress>
{
    public void Report(ArchiveProgress value) { if (value.Completed >= 1) cancel.Cancel(); }
}
