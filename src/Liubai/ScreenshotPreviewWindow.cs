using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Liubai;

internal sealed class ScreenshotPreviewWindow : UtilityPanel
{
    readonly Image image = new() { MaxHeight = 180, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
    readonly TextBox recognized = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, Visibility = Visibility.Collapsed };
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer dismiss;
    readonly Func<Guid, Task> openNote;
    byte[]? bytes;
    string? savedPath;
    bool busy;
    internal ScreenshotPreviewWindow(Func<Guid, Task> openNote) : base("截图轻操作")
    {
        this.openNote = openNote; Body.Children.Add(image);
        var station = new Button { Content = "存入中转站", HorizontalAlignment = HorizontalAlignment.Stretch };
        station.Click += async (_, _) => await Operate(async () =>
        {
            var capture = bytes ?? throw new IOException("预览已过期，请重新截图。");
            if (savedPath is null || !File.Exists(savedPath))
            {
                string folder = Path.Combine(App.DataDirectory, "screenshot-files"); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "截图_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N")[..6] + ".png");
                await File.WriteAllBytesAsync(path + ".tmp", capture); File.Move(path + ".tmp", path); savedPath = path;
            }
            var result = await App.Station.Add([new(StationKind.File, savedPath)]); StatusLine.Text = result.Added > 0 ? "截图已存入中转站。" : "截图已在中转站。";
        });
        var ocr = new Button { Content = "提取文字", HorizontalAlignment = HorizontalAlignment.Stretch };
        ocr.Click += async (_, _) => await Operate(Recognize);
        var note = new Button { Content = "记一笔", HorizontalAlignment = HorizontalAlignment.Stretch };
        note.Click += async (_, _) => { bool opened = false; await Operate(async () => { Guid id = await WorkImages.CreateNote(recognized.Text, [], bytes ?? throw new IOException("预览已过期。")); await openNote(id); opened = true; }); if (opened) Hide(); };
        Body.Children.Add(station); Body.Children.Add(ocr); Body.Children.Add(note); Body.Children.Add(recognized);
        var copy = new Button { Content = "复制提取文字", HorizontalAlignment = HorizontalAlignment.Stretch };
        copy.Click += (_, _) => { if (string.IsNullOrWhiteSpace(recognized.Text)) { StatusLine.Text = "先提取文字，再复制。"; return; } try { var data = new DataPackage(); data.SetText(recognized.Text); Clipboard.SetContent(data); StatusLine.Text = "文字已复制。"; } catch { StatusLine.Text = "复制失败，请重试。"; } }; Body.Children.Add(copy);
        dismiss = DispatcherQueue.CreateTimer(); dismiss.Interval = TimeSpan.FromSeconds(10); dismiss.IsRepeating = false; dismiss.Tick += (_, _) => { if (!busy) Hide(); };
        Layout.PointerEntered += (_, _) => dismiss.Stop();
        Layout.PointerExited += (_, e) => { var p = e.GetCurrentPoint(Layout).Position; if (p.X < 0 || p.Y < 0 || p.X > Layout.ActualWidth || p.Y > Layout.ActualHeight) RestartDismiss(); };
        Closed += (_, _) => dismiss.Stop();
    }
    internal async Task Open(ScreenshotResult capture, DesktopMonitor display, int top)
    {
        bytes = capture.PreviewBytes; savedPath = capture.SavedPath; recognized.Text = ""; recognized.Visibility = Visibility.Collapsed; StatusLine.Text = capture.Message;
        if (bytes == null) return;
        using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        stream.Seek(0); var source = new BitmapImage { DecodePixelWidth = 700 }; await source.SetSourceAsync(stream); image.Source = source;
        Present(display, top, 660); RestartDismiss();
    }
    void RestartDismiss() { if (IsOpen && !busy) { dismiss.Stop(); dismiss.Start(); } }
    internal async Task VerifyRecognition()
    {
        await Guard(Recognize);
        if (StatusLine.Text.StartsWith("操作未完成：") && !StatusLine.Text.Contains("语言包"))
            throw new IOException("Local OCR failed: " + StatusLine.Text);
    }
    async Task Operate(Func<Task> action)
    {
        if (busy) return; busy = true; dismiss.Stop();
        try { await Guard(action); } finally { busy = false; RestartDismiss(); }
    }
    async Task Recognize()
    {
        var input = bytes ?? throw new IOException("预览已过期。");
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new IOException("系统未安装可用的 OCR 语言包。可在 Windows 语言设置中安装所需语言；不会上传截图识别。");
        using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(input); await writer.StoreAsync(); }
        stream.Seek(0); var decoder = await BitmapDecoder.CreateAsync(stream);
        double scale = Math.Min(1, OcrEngine.MaxImageDimension / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)), ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap); recognized.Text = result.Text; recognized.Visibility = Visibility.Visible;
        StatusLine.Text = result.Text.Length == 0 ? "未发现可识别文字，可直接记一笔。" : "文字已在本机提取，可选择复制或记一笔。";
    }
    protected override void OnHiding() { dismiss.Stop(); bytes = null; image.Source = null; recognized.Text = ""; savedPath = null; }
}
