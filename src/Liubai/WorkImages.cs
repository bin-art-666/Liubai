using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Liubai;

internal static class WorkImages
{
    internal static string DirectoryPath => Path.Combine(App.DataDirectory, "note-images");
    internal static bool Managed(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static async Task<string> Save(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        return await Save(stream);
    }
    internal static async Task<string> SaveFile(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync(); return await Save(stream);
    }
    static async Task<string> Save(IRandomAccessStream input)
    {
        if (input.Size > 32 * 1024 * 1024) throw new IOException("图片超过 32 MB。");
        input.Seek(0); var decoder = await BitmapDecoder.CreateAsync(input);
        if ((ulong)decoder.PixelWidth * decoder.PixelHeight > 64000000) throw new IOException("图片像素过大，请先缩小图片。");
        double scale = Math.Min(1, 2048d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)), ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        Directory.CreateDirectory(DirectoryPath); string path = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (File.Create(path + ".tmp")) { }
            var outputFile = await StorageFile.GetFileFromPathAsync(path + ".tmp");
            using (var output = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            { var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output); encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync(); }
            File.Move(path + ".tmp", path); return path;
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
    }
    internal static async Task<Guid> CreateNote(string text, IEnumerable<string> files, byte[]? bitmap = null)
    {
        if (App.Work.Store.ReadOnly || App.Planner.ReadOnly) throw new IOException("记录暂不可写，请先保留数据并处理读取提示。");
        if (App.Planner.Data.Notes.Length >= 10000 || text.Length > 20000) throw new IOException("随手记已达容量上限，或文字超过 20000 字，请先整理或分段保存。");
        var images = new List<string>(); Guid note = Guid.NewGuid();
        try
        {
            foreach (string path in files) images.Add(await SaveFile(path));
            if (bitmap != null) images.Add(await Save(bitmap));
            if (images.Count > 12) throw new IOException("一条记录最多 12 张图片。");
            if (App.Work.Store.Data.Images.Length + images.Count > 10000) throw new IOException("图片附件数量已达上限，请先备份整理。");
            await App.Planner.SaveNote(note, text);
            await App.Work.Store.AddImages(note, images);
            return note;
        }
        catch
        {
            foreach (string path in images.Where(p => !App.Work.Store.Data.Images.Any(i => i.Path == p))) try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }
}
