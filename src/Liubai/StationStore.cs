using System.Text.Json;

namespace Liubai;

internal enum StationKind { File, Folder, Text, Link }
internal sealed record StationInput(StationKind Kind, string Value, string Title = "");
internal sealed record StationItem(Guid Id, StationKind Kind, string Value, string Title, DateTimeOffset Created);
internal sealed record StationDocument(int Version, StationItem[] Items, int Capacity = StationStore.DefaultCapacity, string ArchiveDirectory = "", bool RemoveAfterArchive = true, StationActions? Actions = null);
internal sealed record StationAddResult(int Added, int Duplicates);

// Only references and text are stored. No operation in this store touches source files.
internal sealed partial class StationStore
{
    internal const int DefaultCapacity = 200, MaximumCapacity = 2000, MaximumText = 20000;
    const long MaximumDocumentBytes = 256L * 1024 * 1024;
    readonly string path;
    readonly SemaphoreSlim gate = new(1, 1);
    StationDocument data = new(2, []);
    bool preserveBackup;
    internal IReadOnlyList<StationItem> Items => Array.AsReadOnly(data.Items);
    internal int Capacity => data.Capacity;
    internal string ArchiveDirectory => data.ArchiveDirectory;
    internal bool RemoveAfterArchive => data.RemoveAfterArchive;
    internal StationActions Actions => data.Actions ?? new();
    internal Task UpdateActions(StationActions value) { value.Validate(); return Change(d => d with { Actions = value }); }
    internal Task Edit(Guid id, string value, string title) => Change(d => d with { Items = d.Items.Select(i => i.Id == id ? i with { Value = i.Kind == StationKind.Text ? Normalize(new(i.Kind, value)).Value : i.Value, Title = Normalize(new(i.Kind, i.Value, title)).Title } : i).ToArray() });
    internal Task Relocate(Guid id,string path) => Change(d=>d with { Items=d.Items.Select(i=>i.Id==id&&i.Kind is StationKind.File or StationKind.Folder ? i with {Value=Normalize(new(i.Kind,path)).Value} : i).ToArray() });
    internal string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(path)!, "station-files");
    internal string WindowLayoutPath => Path.Combine(Path.GetDirectoryName(path)!, "station-window.json");
    internal bool ReadOnly { get; }
    internal string? LoadWarning { get; }
    internal event Action? Changed;

    internal StationStore(string path)
    {
        this.path = Path.GetFullPath(path);
        if (!File.Exists(this.path) && !File.Exists(this.path + ".bak")) return;
        try { data = Read(this.path); }
        catch (NotSupportedException) { ReadOnly = true; LoadWarning = "中转站数据来自更新版本，已停止写入。"; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            try { data = Read(this.path + ".bak"); preserveBackup = true; LoadWarning = "已从备份恢复中转站。"; }
            catch (Exception backupError) when (backupError is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            { ReadOnly = true; LoadWarning = "中转站数据无法读取，已停止写入以保护原文件。"; }
        }
    }
    static StationDocument Read(string path)
    {
        if (new FileInfo(path).Length > MaximumDocumentBytes) throw new InvalidDataException("中转站文件过大。");
        var value = JsonSerializer.Deserialize<StationDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("内容为空。");
        if (value.Version is not (1 or 2)) throw new NotSupportedException();
        value=value with {Version=2};
        Validate(value); return value;
    }
    internal static Uri? WebUri(string value) => !value.Any(char.IsControl) && value == value.Trim()
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri : null;
    internal static StationInput Text(string text, string title = "") => WebUri(text) is { } uri
        ? new(StationKind.Link, uri.AbsoluteUri, title) : new(StationKind.Text, text);
    static StationInput Normalize(StationInput value)
    {
        if (value == null || value.Value == null || value.Title == null || !Enum.IsDefined(value.Kind)) throw new InvalidDataException("内容格式不支持。");
        string payload = value.Value;
        if (value.Kind is StationKind.File or StationKind.Folder)
        {
            if (payload.Length > 32767 || !Path.IsPathFullyQualified(payload) || payload.Any(char.IsControl)) throw new InvalidDataException("文件没有可保存的本地路径。");
            payload = Path.TrimEndingDirectorySeparator(Path.GetFullPath(payload));
        }
        else if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumText) throw new InvalidDataException("文字或链接不能为空，最多 20,000 字符。");
        if (value.Kind == StationKind.Link) payload = WebUri(payload)?.AbsoluteUri ?? throw new InvalidDataException("仅支持 HTTP/HTTPS 链接。");
        return value with { Value = payload, Title = value.Title.Length > 200 ? value.Title[..200] : value.Title };
    }
    static string Key(StationKind kind, string value) => kind is StationKind.File or StationKind.Folder
        ? "path:" + value.ToUpperInvariant() : kind + ":" + value;
    static void Validate(StationDocument document)
    {
        document.Actions?.Validate();
        if (document.Items == null || document.Items.Length > MaximumCapacity || document.Capacity is < 1 or > MaximumCapacity
            || document.ArchiveDirectory is null || document.ArchiveDirectory.Length > 32767
            || document.ArchiveDirectory.Length > 0 && (!Path.IsPathFullyQualified(document.ArchiveDirectory) || document.ArchiveDirectory.Any(char.IsControl))) throw new InvalidDataException("中转站条目或设置无效。");
        var ids = new HashSet<Guid>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.Items)
        {
            if (item == null || item.Id == Guid.Empty || !ids.Add(item.Id)) throw new InvalidDataException("中转站条目无效。");
            var normalized = Normalize(new(item.Kind, item.Value, item.Title));
            if (normalized.Value != item.Value || normalized.Title != item.Title || !keys.Add(Key(item.Kind, item.Value))) throw new InvalidDataException("中转站条目重复或格式无效。");
        }
    }
    async Task Change(Func<StationDocument, StationDocument> change)
    {
        await gate.WaitAsync(); bool changed = false;
        try
        {
            if (ReadOnly) throw new IOException(LoadWarning);
            var next = change(data); if (ReferenceEquals(next, data)) return;
            Validate(next);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(stream, next);
                        if (stream.Length > MaximumDocumentBytes) throw new InvalidDataException("中转站内容过大。");
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, preserveBackup ? null : path + ".bak");
                    else File.Move(path + ".tmp", path);
                }
                finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            });
            data = next; preserveBackup = false; changed = true;
        }
        finally { gate.Release(); }
        if (changed) Changed?.Invoke();
    }
    internal async Task<StationAddResult> Add(IEnumerable<StationInput> values)
    {
        var inputs = values.Take(MaximumCapacity + 1).Select(Normalize).ToArray();
        if (inputs.Length > MaximumCapacity) throw new InvalidDataException($"一次最多存入 {MaximumCapacity} 项，请分批拖入。");
        StationAddResult result = new(0, 0);
        await Change(document =>
        {
            var keys = document.Items.Select(i => Key(i.Kind, i.Value)).ToHashSet(StringComparer.Ordinal);
            var additions = new List<StationItem>(); int duplicates = 0;
            foreach (var input in inputs)
            {
                if (!keys.Add(Key(input.Kind, input.Value))) { duplicates++; continue; }
                additions.Add(new(Guid.NewGuid(), input.Kind, input.Value, input.Title, DateTimeOffset.Now));
            }
            if (additions.Count > 0 && document.Items.Length + additions.Count > document.Capacity) throw new InvalidDataException($"中转站上限 {document.Capacity} 项，目前还可存 {Math.Max(0, document.Capacity - document.Items.Length)} 项；本批未存入，请先整理或归档。");
            result = new(additions.Count, duplicates);
            return additions.Count == 0 ? document : document with { Items = [.. document.Items, .. additions] };
        });
        return result;
    }
    internal Task Remove(Guid id) => Change(d => d.Items.Any(i => i.Id == id) ? d with { Items = d.Items.Where(i => i.Id != id).ToArray() } : d);
    internal Task Clear() => Change(d => d.Items.Length == 0 ? d : d with { Items = [] });
    internal Task UpdateOptions(int capacity, string archiveDirectory, bool removeAfterArchive)
    {
        if (capacity is < 1 or > MaximumCapacity) throw new ArgumentException($"保存数量应为 1～{MaximumCapacity} 项。");
        if (!string.IsNullOrWhiteSpace(archiveDirectory) && (!Path.IsPathFullyQualified(archiveDirectory) || archiveDirectory.Any(char.IsControl))) throw new ArgumentException("请选择有效的完整归档路径。");
        string directory = string.IsNullOrWhiteSpace(archiveDirectory) ? "" : Path.GetFullPath(archiveDirectory);
        return Change(d => d.Capacity == capacity && d.ArchiveDirectory == directory && d.RemoveAfterArchive == removeAfterArchive
            ? d : d with { Capacity = capacity, ArchiveDirectory = directory, RemoveAfterArchive = removeAfterArchive });
    }
    internal async Task<int> RemoveArchived(IEnumerable<StationItem> archived)
    {
        var verified = archived.ToHashSet(); int removed = 0;
        await Change(d =>
        {
            var remaining = d.Items.Where(i => !verified.Contains(i)).ToArray(); removed = d.Items.Length - remaining.Length;
            return removed == 0 ? d : d with { Items = remaining };
        });
        return removed;
    }
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
