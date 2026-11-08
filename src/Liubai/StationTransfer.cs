using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Liubai;

internal static class StationTransfer
{
    static readonly string idsFormat="Liubai.Station."+Guid.NewGuid().ToString("N");
    internal static async Task<StationItem[]?> InternalItems(DataPackageView view, StationStore store)
    {
        if(!view.Contains(idsFormat)) return null;
        if(await view.GetDataAsync(idsFormat) is not string json || json.Length>100000) throw new InvalidDataException("内部拖放格式无效。");
        var ids=System.Text.Json.JsonSerializer.Deserialize<Guid[]>(json)??throw new InvalidDataException();
        if(ids.Length==0||ids.Length>StationStore.MaximumCapacity||ids.Distinct().Count()!=ids.Length) throw new InvalidDataException("内部拖放数量无效。");
        return ids.Select(id=>store.Items.FirstOrDefault(i=>i.Id==id)??throw new IOException("拖放项目已移除。")).ToArray();
    }
    static void Identify(DataPackage package, IEnumerable<StationItem> items) => package.SetData(idsFormat,System.Text.Json.JsonSerializer.Serialize(items.Select(i=>i.Id).ToArray()));
    internal static bool Supported(DataPackageView view) => view.Contains(StandardDataFormats.StorageItems)
        || view.Contains(StandardDataFormats.WebLink) || view.Contains(StandardDataFormats.Text);
    // The source owns a drag only until its deferral completes. Materialize pathless
    // files before returning; rollback only files created by this specific import.
    internal sealed class Import : IDisposable
    {
        internal StationInput[] Inputs = [];
        internal readonly List<string> CreatedFiles = [];
        bool committed;
        internal void Commit() => committed = true;
        public void Dispose()
        {
            if (committed) return;
            foreach (string file in CreatedFiles) try { File.Delete(file); Directory.Delete(Path.GetDirectoryName(file)!); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    internal static async Task<Import> Read(DataPackageView view, string attachmentDirectory, CancellationToken cancellation = default)
    {
        var result = new Import();
        try
        {
            // Browsers may expose a virtual .url file alongside their actual URL.
            // Prefer the explicit link; a failing optional format must not mask another.
            if (view.Contains(StandardDataFormats.WebLink))
                try
                {
                    var uri = await view.GetWebLinkAsync().AsTask(cancellation);
                    if (StationStore.WebUri(uri.AbsoluteUri) is not null)
                    { result.Inputs = [new(StationKind.Link, uri.AbsoluteUri, view.Properties.Title ?? "")]; return result; }
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) { }
            string? text = null;
            if (view.Contains(StandardDataFormats.Text))
                try { text = await view.GetTextAsync().AsTask(cancellation); }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) { }
            if (text is not null && StationStore.WebUri(text.Trim()) is { } web)
            { result.Inputs = [new(StationKind.Link, web.AbsoluteUri, view.Properties.Title ?? "")]; return result; }
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync().AsTask(cancellation);
                if (items.Count > StationStore.MaximumCapacity) throw new InvalidDataException($"一次最多存入 {StationStore.MaximumCapacity} 项，请分批拖入。");
                var inputs = new List<StationInput>();
                foreach (var item in items)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(item.Path))
                        inputs.Add(new(item.IsOfType(StorageItemTypes.Folder) ? StationKind.Folder : StationKind.File, item.Path));
                    else if (item is IStorageFile file)
                    {
                        string name = Path.GetFileName(file.Name);
                        name = string.Concat(name.Select(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
                        if (name.Length == 0) name = "附件";
                        if (name.Length > 160) name = name[..160];
                        string directory = Path.Combine(attachmentDirectory, Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(directory);
                        // Prefix also makes DOS reserved filenames safe.
                        string local = Path.Combine(directory, "附件_" + name);
                        result.CreatedFiles.Add(local);
                        using var source = await file.OpenReadAsync().AsTask(cancellation);
                        using var stream = source.AsStreamForRead();
                        await using var target = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                        await stream.CopyToAsync(target, cancellation);
                        await target.FlushAsync(cancellation);
                        inputs.Add(new(StationKind.File, local));
                    }
                    else throw new InvalidDataException("暂不支持没有本地路径的虚拟文件夹，请先保存到本地。");
                }
                result.Inputs = inputs.ToArray(); return result;
            }
            if (text is not null) { result.Inputs = [StationStore.Text(text)]; return result; }
            throw new InvalidDataException("支持文件、文件夹、文字和网页链接。");
        }
        catch { result.Dispose(); throw; }
    }
    internal static async Task<IStorageItem> Storage(StationItem item)
    {
        if (item.Kind == StationKind.Folder) return await StorageFolder.GetFolderFromPathAsync(item.Value).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        return await StorageFile.GetFileFromPathAsync(item.Value).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal static async Task Fill(DataPackage package, StationItem item)
    {
        Identify(package,[item]);
        package.RequestedOperation = DataPackageOperation.Copy;
        if (item.Kind is StationKind.File or StationKind.Folder) package.SetStorageItems(new[] { await Storage(item) }, true);
        else
        {
            package.SetText(item.Value);
            if (item.Kind == StationKind.Link) package.SetWebLink(new Uri(item.Value));
        }
    }
    internal static async Task FillMany(DataPackage package, IReadOnlyList<StationItem> items, string directory)
    {
        if (items.Count == 0) throw new InvalidDataException("没有选中项目。");
        if (items.Count == 1) { await Fill(package, items[0]); return; }
        var files = new List<IStorageItem>();
        string? exports = null;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Kind is StationKind.File or StationKind.Folder) files.Add(await Storage(item));
            else
            {
                exports ??= Path.Combine(directory, "transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(exports);
                string label = string.Concat((item.Title.Length>0?item.Title:item.Kind==StationKind.Text?item.Value:"链接").Take(40).Select(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');
                string file = Path.Combine(exports, $"{i+1:D4}_"+label + (item.Kind == StationKind.Link ? ".url" : ".txt"));
                await File.WriteAllTextAsync(file, item.Kind == StationKind.Link ? "[InternetShortcut]\r\nURL=" + item.Value + "\r\n" : item.Value);
                if(item.Kind==StationKind.Link)
                    files.Add(await StorageFile.CreateStreamedFileAsync(Path.GetFileName(file),async request=> {
                        try { using var source=File.OpenRead(file); using var target=request.AsStreamForWrite(); await source.CopyToAsync(target); }
                        catch { try { request.FailAndClose(StreamedFileFailureMode.Incomplete); } catch { } }
                    },null));
                else files.Add(await StorageFile.GetFileFromPathAsync(file));
            }
        }
        Identify(package,items);
        package.RequestedOperation = DataPackageOperation.Copy; package.SetStorageItems(files, true);
        package.SetText(string.Join(Environment.NewLine, items.Select(i => i.Value)));
    }
}
