using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;

namespace Liubai;

internal sealed partial class StationWindow
{
    const string SweepPath = "M 10 9 L 16 2 M 8 8 L 13 12 M 8 8 C 5 10 3 13 2 17 L 10 18 C 10 15 11 13 13 12 M 6 12 L 4 17 M 9 13 L 7 17";
    const string ArchivePath = "M 10 2 L 10 12 M 6 8 L 10 12 L 14 8 M 3 10 L 2 17 C 2 17.6 2.4 18 3 18 L 17 18 C 17.6 18 18 17.6 18 17 L 17 10 M 3 14 L 6 14 L 7.5 16 L 12.5 16 L 14 14 L 17 14";
    const string OpenPath = "M 11.5 3.5 L 6 3.5 C 4.6 3.5 3.5 4.6 3.5 6 L 3.5 15 C 3.5 16.4 4.6 17.5 6 17.5 L 15 17.5 C 16.4 17.5 17.5 16.4 17.5 15 L 17.5 9 M 11 9 L 18 2 M 13 2 L 18 2 L 18 7";
    const string CopyPath = "M 7 6 L 5 6 C 3.9 6 3 6.9 3 8 L 3 16 C 3 17.1 3.9 18 5 18 L 11 18 C 12.1 18 13 17.1 13 16 L 13 14 M 9 2 L 15 2 C 16.1 2 17 2.9 17 4 L 17 12 C 17 13.1 16.1 14 15 14 L 9 14 C 7.9 14 7 13.1 7 12 L 7 4 C 7 2.9 7.9 2 9 2 Z";
    const string TrashPath = "M 3 5 L 17 5 M 7 5 L 7 3 C 7 2.4 7.4 2 8 2 L 12 2 C 12.6 2 13 2.4 13 3 L 13 5 M 5 5 L 6 17 C 6 17.6 6.4 18 7 18 L 13 18 C 13.6 18 14 17.6 14 17 L 15 5 M 8 8 L 8.5 14 M 12 8 L 11.5 14";
    CancellationTokenSource? previewCancellation;
    readonly SemaphoreSlim previewSlots = new(3);
    readonly Dictionary<string, StationPreview> previewCache = new(StringComparer.OrdinalIgnoreCase);
    sealed record StationPreview(ImageSource Image, bool Photo, StationColors? Colors);
    internal sealed record StationColors(Windows.UI.Color First, Windows.UI.Color Second);

    void CancelPreviews() { previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null; }
    static Button ItemAction(string name, string data, bool header = false)
    {
        var button = IconButton("", name);
        SetStationPath(button,data);
        if(data==TrashPath||data==SweepPath) DangerAction(button);
        return button;
    }
    static void SetStationPath(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button,string data)
    {
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "' StrokeThickness='1.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
        path.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty, new Binding { Source = button, Path = new PropertyPath("Foreground") });
        path.StrokeThickness = PanelTheme.Value<double>("IconStroke");
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false }; canvas.Children.Add(path);
        button.Content = PanelTheme.Track(new Viewbox { Child = canvas, IsHitTestVisible = false },
            static view => view.Width = view.Height = PanelTheme.Value<double>("IconSize"));
    }
    static Border CreatePreview(StationItem item)
    {
        var glyph = item.Kind switch { StationKind.Folder => "\uE8B7", StationKind.Link => "\uE71B", StationKind.Text => "\uE8A5", _ => "\uE8A5" };
        var host = new Border { Width = 48, Height = 48, CornerRadius = PanelTheme.Value<CornerRadius>("ControlRadius"), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128)), Child = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 23, Opacity = .65 } };
        PanelTheme.Track(host, static border => border.CornerRadius = PanelTheme.Value<CornerRadius>("ControlRadius"));
        AutomationProperties.SetName(host, "内容预览"); return host;
    }
    async Task LoadPreview(StationItem item, Border host, Border card, TextBlock meta, FrameworkElement grip, Button copy, Button open, int version, string label, CancellationToken cancellation)
    {
        bool entered = false, available = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = timeout.Token;
        try
        {
            await previewSlots.WaitAsync(token); entered = true; timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var storage = await StationTransfer.Storage(item).WaitAsync(token);
            available = true;
            if (destroyed || version != renderVersion || token.IsCancellationRequested) return;
            var properties = await storage.GetBasicPropertiesAsync().AsTask(token);
            uint size = (uint)Math.Clamp(Math.Ceiling(48 * display.Dpi / 96d), 48, 192);
            string key = $"{item.Value}|{properties.DateModified.UtcTicks}|{properties.Size}|{size}";
            if (!previewCache.TryGetValue(key, out var preview))
            {
                using var thumbnail = storage switch
                {
                    StorageFile file => await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail).AsTask(token),
                    StorageFolder folder => await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail).AsTask(token),
                    _ => null
                };
                if (thumbnail == null) return;
                var bitmap = new BitmapImage { DecodePixelWidth = (int)size };
                using (var stream = thumbnail.CloneStream()) await bitmap.SetSourceAsync(stream).AsTask(token);
                StationColors? colors = null;
                if (thumbnail.Type == ThumbnailType.Image)
                {
                    try
                    {
                    using var stream = thumbnail.CloneStream();
                    var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
                    var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                        new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24 }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token);
                    colors = ExtractColors(pixels.DetachPixelData());
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException) { }
                }
                preview = new(bitmap, thumbnail.Type == ThumbnailType.Image, colors);
                if (destroyed || version != renderVersion || token.IsCancellationRequested) return;
                if (previewCache.Count >= 60) previewCache.Remove(previewCache.Keys.First());
                previewCache[key] = preview;
            }
            if (destroyed || version != renderVersion || token.IsCancellationRequested) return;
            host.Child = new Image { Source = preview.Image, Stretch = preview.Photo ? Stretch.UniformToFill : Stretch.Uniform,
                Margin = preview.Photo ? new Thickness(0) : new Thickness(5), IsHitTestVisible = false };
            AutomationProperties.SetName(host, preview.Photo ? "文件缩略图" : "文件图标");
            if (preview.Colors is { } colorsForCard)
            {
                void Tint() => card.Background = CardTint(colorsForCard, card.ActualTheme == ElementTheme.Dark);
                Tint(); card.ActualThemeChanged += (_, _) => Tint();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* Keep the file/type icon if its provider cannot produce a preview. */ }
        finally
        {
            if (entered) previewSlots.Release();
            if (!destroyed && version == renderVersion && !cancellation.IsCancellationRequested)
            {
                meta.Text = available ? label : label + " · 原位置暂不可用或已失效";
                if (!available) ToolTipService.SetToolTip(meta, meta.Text + "\n" + item.Value);
                grip.CanDrag = available; copy.IsEnabled = available; open.IsEnabled = available;
            }
        }
    }
    internal static StationColors? ExtractColors(byte[] bgra)
    {
        var bins = new Dictionary<int, (int Count, long R, long G, long B)>();
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            if (bgra[i + 3] < 128 || Math.Max(r, Math.Max(g, b)) < 24 || Math.Min(r, Math.Min(g, b)) > 238) continue;
            int key = (r / 32 << 6) | (g / 32 << 3) | b / 32;
            bins.TryGetValue(key, out var bin); bins[key] = (bin.Count + 1, bin.R + r, bin.G + g, bin.B + b);
        }
        var sorted = bins.Values.OrderByDescending(b => b.Count).ToArray();
        if (sorted.Length == 0) return null;
        static Windows.UI.Color Color((int Count, long R, long G, long B) bin) => Windows.UI.Color.FromArgb(255, (byte)(bin.R / bin.Count), (byte)(bin.G / bin.Count), (byte)(bin.B / bin.Count));
        var first = Color(sorted[0]); var second = first;
        foreach (var bin in sorted.Skip(1))
        { var next = Color(bin); if (Math.Abs(first.R - next.R) + Math.Abs(first.G - next.G) + Math.Abs(first.B - next.B) > 65) { second = next; break; } }
        return new(first, second);
    }
    internal static Brush CardTint(StationColors colors, bool dark)
    {
        Windows.UI.Color Tint(Windows.UI.Color c) => Windows.UI.Color.FromArgb(190,
            (byte)(c.R * (dark ? .28 : .24) + (dark ? 18 : 255) * (dark ? .72 : .76)),
            (byte)(c.G * (dark ? .28 : .24) + (dark ? 18 : 255) * (dark ? .72 : .76)),
            (byte)(c.B * (dark ? .28 : .24) + (dark ? 18 : 255) * (dark ? .72 : .76)));
        return new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), GradientStops =
        { new GradientStop { Color = Tint(colors.First), Offset = 0 }, new GradientStop { Color = Tint(colors.Second), Offset = 1 } } };
    }
    internal async Task OpenItem(StationItem item)
    {
        using var lease = KeepOpen();
        bool opened;
        switch (item.Kind)
        {
            case StationKind.File:
                opened = await Launcher.LaunchFileAsync((StorageFile)await StationTransfer.Storage(item)); break;
            case StationKind.Folder:
                opened = await Launcher.LaunchFolderAsync((StorageFolder)await StationTransfer.Storage(item)); break;
            case StationKind.Link:
                opened = await Launcher.LaunchUriAsync(new Uri(item.Value)); break;
            default:
                var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = "文字预览", CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Close,
                    Content = new ScrollViewer { MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = new TextBlock { Text = item.Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 15, MaxWidth = 420 } } };
                await dialog.ShowAsync(); return;
        }
        status.Text = opened ? "已打开，内容仍保留在中转站。" : "未能打开，可复制后重试。";
    }
}
