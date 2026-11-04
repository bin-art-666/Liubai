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
    string file = Path.Combine(root, "sample.txt"), folder = Path.Combine(root, "folder");
    File.WriteAllText(file, "original source content"); Directory.CreateDirectory(folder);
    await store.Add([new(StationKind.File, file), new(StationKind.Folder, folder), StationStore.Text("  原样文字\n第二行  "), StationStore.Text("https://example.com", "Example")]);
    Check(store.Items.Count == 4, "Supported types");
    Check(store.Items.Single(i => i.Kind == StationKind.Text).Value == "  原样文字\n第二行  ", "Text whitespace changed");
    Check(store.Items.Single(i => i.Kind == StationKind.Link).Value == "https://example.com/", "URL normalization");
    Check(new StationStore(path).Items.SequenceEqual(store.Items), "Restart recovery");
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
    Console.WriteLine($"PASS: {checks} station persistence, capacity, deduplication, source protection and recovery checks");
}
finally
{
    string expected = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".build")) + Path.DirectorySeparatorChar;
    if (root.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) Directory.Delete(root, true);
}
