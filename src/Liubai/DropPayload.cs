using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Liubai;

internal sealed class DropPayload : IDisposable
{
    StationTransfer.Import? import;
    readonly List<DropPayload> children = [];
    readonly List<string> generated = [];
    bool committed;
    internal string Text = "";
    internal string[] Files = [];
    internal byte[]? Bitmap;
    internal static bool Supported(DataPackageView view) => StationTransfer.Supported(view) || view.Contains(StandardDataFormats.Bitmap);
    internal static async Task<string> ReadText(DataPackageView view)
    {
        string text = "";
        if (view.Contains(StandardDataFormats.WebLink))
            try { text = (await view.GetWebLinkAsync()).AbsoluteUri; } catch (System.Runtime.InteropServices.COMException) { }
        if (text.Length == 0 && view.Contains(StandardDataFormats.Text)) text = await view.GetTextAsync();
        if (view.Contains(StandardDataFormats.StorageItems) && StationStore.WebUri(text.Trim()) == null) throw new IOException("待办入口接收文字；文件请放到中转站。");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) throw new IOException("请拖入 1～20000 字的文字或链接。");
        return text;
    }
    internal static async Task<DropPayload> Read(DataPackageView view)
    {
        var result = new DropPayload();
        try
        {
            if (view.Contains(StandardDataFormats.Bitmap) && !view.Contains(StandardDataFormats.StorageItems))
            {
                using var stream = await (await view.GetBitmapAsync()).OpenReadAsync();
                if (stream.Size > 32 * 1024 * 1024) throw new IOException("图片超过 32 MB，请先缩小图片。");
                using var reader = new DataReader(stream); await reader.LoadAsync((uint)stream.Size);
                result.Bitmap = new byte[(int)stream.Size]; reader.ReadBytes(result.Bitmap); return result;
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            result.import = await StationTransfer.Read(view, Path.Combine(App.DataDirectory, "drop-files"), cancellation.Token);
            if (result.import.Inputs.Length > 12) throw new IOException("一次最多拖入 12 项。");
            if (result.import.Inputs.Any(i => i.Kind == StationKind.Folder)) throw new IOException("此入口接收文字和文件；文件夹可放进中转站。");
            result.Text = string.Join("\n", result.import.Inputs.Where(i => i.Kind is StationKind.Text or StationKind.Link).Select(i => i.Value));
            if (result.Text.Length > 20000) throw new IOException("文字最多 20000 字，请分段拖入。");
            result.Files = result.import.Inputs.Where(i => i.Kind == StationKind.File).Select(i => i.Value).ToArray();
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    internal void OwnFile(string path) => generated.Add(path);
    internal bool CanAppend(DropPayload next) => Files.Concat(next.Files).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 12
        && Text.Length + next.Text.Length + (Text.Length > 0 && next.Text.Length > 0 ? 1 : 0) <= 20000;
    internal void Append(DropPayload next)
    {
        if (!CanAppend(next)) throw new IOException("合并后超过 12 个文件或 20000 字，请替换或取消。");
        Files = Files.Concat(next.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Text = Text.Length == 0 ? next.Text : next.Text.Length == 0 ? Text : Text + "\n" + next.Text;
        children.Add(next);
    }
    internal void Commit()
    {
        committed = true; import?.Commit(); foreach (var child in children) child.Commit();
    }
    public void Dispose()
    {
        import?.Dispose(); import = null; Bitmap = null;
        foreach (var child in children) child.Dispose(); children.Clear();
        if (!committed) foreach (string path in generated) try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        generated.Clear();
    }
}
