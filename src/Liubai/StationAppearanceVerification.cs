using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyStationAppearance(List<string> checks)
    {
        string path = Path.Combine(App.DataDirectory, "蓝海与沙滩.png");
        var file = await StorageFolder.GetFolderFromPathAsync(App.DataDirectory);
        var imageFile = await file.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using (var stream = await imageFile.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            var pixels = new byte[96 * 64 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                bool sea = i / 4 / 96 < 44;
                pixels[i] = sea ? (byte)185 : (byte)110;
                pixels[i + 1] = sea ? (byte)145 : (byte)190;
                pixels[i + 2] = sea ? (byte)35 : (byte)230; pixels[i + 3] = 255;
            }
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 96, 64, 96, 96, pixels); await encoder.FlushAsync();
        }
        var package = new DataPackage(); package.SetStorageItems(new[] { imageFile });
        if (!await stationWindow!.Receive(package.GetView())) throw new Exception("Image fixture import failed");
        var root = (DependencyObject)stationWindow.Content;
        for (int i = 0; i < 50 && !StationControls<Border>(root).Any(b => AutomationProperties.GetName(b) == "文件缩略图"); i++) await Task.Delay(150);
        if (!StationControls<Border>(root).Any(b => AutomationProperties.GetName(b) == "文件缩略图" && b.Child is Image { Source: not null })) throw new Exception("Real Windows image thumbnail missing");
        if (!StationControls<Border>(root).Any(b => b.Background is LinearGradientBrush)) throw new Exception("Image-derived card tint missing");
        foreach (var open in StationControls<Button>(root).Where(b => AutomationProperties.GetName(b).StartsWith("打开", StringComparison.Ordinal)))
        {
            if (open.Parent is not Grid buttons || buttons.Children.Count != 3) throw new Exception("Item actions not grouped");
            var names = buttons.Children.OfType<Button>().Select(AutomationProperties.GetName).ToArray();
            if (names[1] != "复制内容" || names[2] != "更多操作") throw new Exception("Action order mismatch");
            if (buttons.Children.OfType<Button>().Any(b => b.BorderThickness.Left != 1 || b.BorderBrush is not SolidColorBrush brush || brush.Color.R != 255 || brush.Color.G != 255 || brush.Color.B != 255)) throw new Exception("White action borders missing");
        }
        checks.Add("Windows image thumbnail, image-derived card gradient, open/copy/more order and white button borders");
        if (StationWindow.ExtractColors([0, 0, 0, 0, 255, 255, 255, 255]) != null) throw new Exception("Transparent/white pixels produced tint");
        var colors = StationWindow.ExtractColors([180, 140, 30, 255, 110, 190, 230, 255]) ?? throw new Exception("Palette empty");
        foreach (bool dark in new[] { false, true })
        {
            var brush = (LinearGradientBrush)StationWindow.CardTint(colors, dark);
            if (brush.GradientStops.Any(s => dark ? s.Color.R > 90 || s.Color.G > 90 || s.Color.B > 90 : s.Color.R < 190 || s.Color.G < 190 || s.Color.B < 190)) throw new Exception("Tint contrast floor violated");
        }
        checks.Add("Palette ignores transparent/white pixels; tint retains contrast in light and dark themes");
        var surface = (FrameworkElement)stationWindow.Content;
        var originalTheme = surface.RequestedTheme;
        surface.RequestedTheme = ElementTheme.Dark; await Task.Delay(150);
        var tinted = StationControls<Border>(root).First(b => b.Background is LinearGradientBrush);
        if (tinted.ActualTheme != ElementTheme.Dark || ((LinearGradientBrush)tinted.Background).GradientStops.Any(s => s.Color.R > 90 || s.Color.G > 90 || s.Color.B > 90)) throw new Exception("Live dark-theme tint did not update");
        surface.RequestedTheme = ElementTheme.Light; await Task.Delay(150);
        if (((LinearGradientBrush)tinted.Background).GradientStops.Any(s => s.Color.R < 190 || s.Color.G < 190 || s.Color.B < 190)) throw new Exception("Live light-theme tint did not update");
        surface.RequestedTheme = originalTheme;
        string missingPath = Path.Combine(App.DataDirectory, "missing-preview-fixture.txt");
        await App.Station.Add([new StationInput(StationKind.File, missingPath)]);
        var missing = App.Station.Items.Single(i => i.Value == missingPath);
        var missingGrip = StationControls<ContentControl>(root).Single(b => AutomationProperties.GetName(b) == "拖出文件：missing-preview-fixture.txt");
        for (int i = 0; i < 50 && missingGrip.CanDrag; i++) await Task.Delay(150);
        if (missingGrip.CanDrag || missingGrip.Parent is not Grid row) throw new Exception("Missing file remained draggable");
        var missingButtons = StationControls<Button>(row).ToArray();
        if (missingButtons.Length != 3 || missingButtons[0].IsEnabled || missingButtons[1].IsEnabled || !missingButtons[2].IsEnabled) throw new Exception("Missing file action availability incorrect");
        await App.Station.Remove(missing.Id);
        checks.Add("Live light/dark theme changes; missing files keep fallback icon and disable only open/copy/drag");
        int count = App.Station.Items.Count;
        var previewTask = stationWindow.OpenItem(App.Station.Items.First(i => i.Kind == StationKind.Text));
        ContentDialog? dialog=null;long previewDeadline=Environment.TickCount64+3000;
        do {
            dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)stationWindow.Content).XamlRoot)
                .SelectMany(p=>p.Child is {} child?StationControls<ContentDialog>(child):[]).SingleOrDefault();
            if(dialog!=null)break;
            if(previewTask.IsCompleted){await previewTask;break;}
            await Task.Delay(40);
        } while(Environment.TickCount64<previewDeadline);
        if (dialog?.Title as string != "文字预览" || !stationWindow.ModalActive) throw new Exception("Text preview dialog missing");
        dialog.Hide(); await previewTask;
        if (App.Station.Items.Count != count) throw new Exception("Opening preview changed stored items");
        checks.Add("Selectable text preview opens and closes without removing or modifying content");
        stationWindow.ConfirmDrop(); await Task.Delay(400);
    }
}
