using System.Text.Json;

namespace Liubai;

internal enum StationKind { File, Folder, Text, Link }
internal sealed record StationInput(StationKind Kind, string Value, string Title = "");
internal sealed record StationItem(Guid Id, StationKind Kind, string Value, string Title, DateTimeOffset Created);
internal sealed record StationDocument(int Version, StationItem[] Items);
internal sealed record StationAddResult(int Added, int Duplicates);

// Only references and text are stored. No operation in this store touches source files.
internal sealed class StationStore
{
    internal const int Capacity = 30, MaximumText = 20000;
    readonly string path;
    readonly SemaphoreSlim gate = new(1, 1);
    StationDocument data = new(1, []);
    bool preserveBackup;
    internal IReadOnlyList<StationItem> Items => Array.AsReadOnly(data.Items);
    internal string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(path)!, "station-files");
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
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("中转站文件过大。");
        var value = JsonSerializer.Deserialize<StationDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("内容为空。");
        if (value.Version != 1) throw new NotSupportedException();
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
        if (document.Items == null || document.Items.Length > Capacity) throw new InvalidDataException("中转站条目无效。");
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
                        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("中转站内容过大。");
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
        var inputs = values.Take(Capacity + 1).Select(Normalize).ToArray();
        if (inputs.Length > Capacity) throw new InvalidDataException("一次最多存入 30 项，请分批拖入。");
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
            if (document.Items.Length + additions.Count > Capacity) throw new InvalidDataException($"中转站最多 30 项，目前还可存 {Capacity - document.Items.Length} 项；本批未存入，请先整理。");
            result = new(additions.Count, duplicates);
            return additions.Count == 0 ? document : document with { Items = [.. document.Items, .. additions] };
        });
        return result;
    }
    internal Task Remove(Guid id) => Change(d => d.Items.Any(i => i.Id == id) ? d with { Items = d.Items.Where(i => i.Id != id).ToArray() } : d);
    internal Task Clear() => Change(d => d.Items.Length == 0 ? d : d with { Items = [] });
    internal async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
