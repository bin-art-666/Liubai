using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyScreenshot()
    {
        string result = "PASS", error = "";
        var checks = new List<object>();
        try
        {
            Activate(); await Task.Delay(200);
            // Verification never reads or changes the user's system clipboard.
            // Inspect only the image package created from our new test PNG.
            var monitor = Display.Info.Monitor;
            var bounds = new RectInt32(monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top);
            var options = new ScreenshotOptions(true, Path.Combine(App.DataDirectory, "images"));
            App.Screenshots.Save(options);
            if (new ScreenshotOptionsStore(Path.Combine(App.DataDirectory, "screenshot.json")).Value != options)
                throw new InvalidOperationException("Screenshot settings did not survive reload.");
            var first = await ScreenshotService.Capture(bounds, options, copy: false);
            if (first.SavedPath == null || first.CaptureError != null || first.SaveError != null)
                throw new InvalidOperationException("PNG capture/save/copy failed: " + first);
            var file = await StorageFile.GetFileFromPathAsync(first.SavedPath);
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth != bounds.Width || decoder.PixelHeight != bounds.Height || decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId)
                    throw new InvalidOperationException("Screenshot was not a full-resolution PNG.");
            }
            checks.Add(new { png = "PASS", width = bounds.Width, height = bounds.Height, settings = "PASS" });
            using (var stream = await file.OpenReadAsync())
            {
                var package = new DataPackage(); package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                var image = package.GetView();
                if (!image.Contains(StandardDataFormats.Bitmap)) throw new InvalidOperationException("Image package did not contain a bitmap.");
                using var encoded = await (await image.GetBitmapAsync()).OpenReadAsync();
                var decoded = await BitmapDecoder.CreateAsync(encoded);
                if (decoded.PixelWidth != bounds.Width || decoded.PixelHeight != bounds.Height)
                    throw new InvalidOperationException("Image package dimensions changed.");
                checks.Add(new { bitmapDataPackage = "PASS", systemClipboard = "not accessed" });
            }
            await Task.Delay(600);
            var disabled = await ScreenshotService.Capture(bounds, options with { SaveFiles = false }, copy: false);
            if (disabled.Busy || disabled.CaptureError != null || disabled.SavedPath != null || Directory.GetFiles(options.SaveDirectory, "*.png").Length != 1)
                throw new InvalidOperationException("Save-disabled mode wrote a file or failed capture.");
            checks.Add(new { saveDisabled = "PASS" });
            await Task.Delay(600);
            string blocked = Path.Combine(App.DataDirectory, "not-a-directory"); File.WriteAllText(blocked, "test");
            var failure = await ScreenshotService.Capture(bounds, options with { Directory = blocked }, copy: false);
            if (failure.SaveError == null || failure.SavedPath != null || failure.CaptureError != null)
                throw new InvalidOperationException("Save failure interfered with clipboard capture.");
            checks.Add(new { independentSaveFailure = "PASS" });
            await Task.Delay(600);
            var pending = ScreenshotService.Capture(bounds, options with { SaveFiles = false }, copy: false);
            var repeated = await ScreenshotService.Capture(bounds, options with { SaveFiles = false }, copy: false);
            await pending;
            if (!repeated.Busy) throw new InvalidOperationException("Concurrent screenshot requests were not coalesced.");
            checks.Add(new { doubleClickGuard = "PASS" });
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "screenshot-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Quit();
        }
    }
}
