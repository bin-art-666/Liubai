using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Liubai;

internal sealed record ScreenshotResult(bool Copied = false, string? SavedPath = null, string? CaptureError = null,
    string? CopyError = null, string? SaveError = null, bool Busy = false)
{
    internal string Message => CaptureError ?? (Copied
        ? SavedPath != null ? "已复制并保存截图\n" + SavedPath : SaveError != null ? "已复制截图；保存文件失败，请检查目录权限" : "已复制截图"
        : SavedPath != null ? "截图已保存；复制失败，请重试\n" + SavedPath : "未能复制截图" + (SaveError != null ? "，保存文件也失败了" : "，请重试"));
}

internal static class ScreenshotService
{
    static readonly SemaphoreSlim gate = new(1, 1);
    static long lastStarted = long.MinValue / 2;
    internal static async Task<ScreenshotResult> Capture(RectInt32 bounds, ScreenshotOptions options, bool copy = true)
    {
        if (!await gate.WaitAsync(0)) return new(Busy: true);
        try
        {
            long now = Environment.TickCount64;
            if (now - lastStarted < 500) return new(Busy: true);
            lastStarted = now;
            byte[]? pixels = await Task.Run(() => BarAppearanceTransition.Capture(bounds));
            if (pixels == null) return new(CaptureError: "未能截取屏幕，请重试");
            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)bounds.Width, (uint)bounds.Height, 96, 96, pixels);
            await encoder.FlushAsync(); png.Seek(0);
            bool copied = false; string? copyError = null, saveError = null, saved = null;
            if (copy)
            {
                for (int attempt = 0; attempt < 3 && !copied; attempt++)
                {
                    try
                    {
                        png.Seek(0);
                        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(png));
                        Clipboard.SetContent(package); Clipboard.Flush(); copied = true;
                    }
                    catch (Exception ex) when (ex is COMException or InvalidOperationException)
                    { copyError = ex.Message; if (attempt < 2) await Task.Delay(80 * (attempt + 1)); }
                }
            }
            if (options.SaveFiles)
            {
                try
                {
                    png.Seek(0);
                    byte[] bytes = new byte[checked((int)png.Size)];
                    using (var reader = new DataReader(png.GetInputStreamAt(0)))
                    { await reader.LoadAsync((uint)bytes.Length); reader.ReadBytes(bytes); }
                    saved = await Task.Run(async () =>
                    {
                        string directory = Path.GetFullPath(options.SaveDirectory);
                        Directory.CreateDirectory(directory);
                        string name = $"留白截图_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}_{Guid.NewGuid().ToString("N")[..6]}.png";
                        string destination = Path.Combine(directory, name), temporary = destination + ".tmp";
                        try
                        {
                            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                            { await stream.WriteAsync(bytes); await stream.FlushAsync(); stream.Flush(true); }
                            File.Move(temporary, destination); return destination;
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                { saveError = ex.Message; }
            }
            return new(copied, saved, CopyError: copyError, SaveError: saveError);
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException or ArgumentException or OverflowException)
        { return new(CaptureError: "截图未完成，请稍后重试"); }
        finally { gate.Release(); }
    }
    internal static async Task Flush() { await gate.WaitAsync(); gate.Release(); }
}
