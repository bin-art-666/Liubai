using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace Liubai;

public sealed partial class BarWindow
{
    bool preparingScreenshot;
    bool openingScreenshotFolder;
    ScreenshotPreviewWindow? screenshotPreview;
    void InitializeScreenshotMenu()
    {
        var menu = new MenuFlyout();
        var openFolder = new MenuFlyoutItem { Text = "打开截图文件夹", Icon = new SymbolIcon(Symbol.Folder) };
        openFolder.Click += async (_, _) => await OpenScreenshotFolder();
        menu.Items.Add(openFolder);
        menu.Opening += (_, _) => openFolder.IsEnabled = !closing && !openingScreenshotFolder && !ToolbarClickBlocked;
        screenshot.ContextFlyout = menu;
    }
    async Task OpenScreenshotFolder()
    {
        if (closing || openingScreenshotFolder || ToolbarClickBlocked) return;
        openingScreenshotFolder = true;
        try
        {
            // Resolve on each click so a changed save location takes effect immediately.
            string directory = Path.GetFullPath(App.Screenshots.Value.SaveDirectory);
            await Task.Run(() => Directory.CreateDirectory(directory));
            if (closing) return;
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            if (!await Launcher.LaunchFolderAsync(folder)) ShowScreenshotResult("无法打开截图文件夹，请检查保存目录");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
        { ShowScreenshotResult("无法打开截图文件夹，请检查路径或访问权限"); }
        finally { openingScreenshotFolder = false; }
    }
    async Task TakeScreenshot()
    {
        if (preparingScreenshot || closing || PanelModal || manager?.HasModal == true) return;
        preparingScreenshot = true;
        try
        {
            screenshotPreview?.Hide();
            screenshotTipTimer.Stop(); screenshotTip.IsOpen = false;
            screenshotTip.Content = "一键截取整个屏幕";
            VisualStateManager.GoToState(screenshot, "Normal", false);
            // Let pointer/tooltip visuals settle before capturing any feedback.
            await Task.Delay(160);
            if (closing) return;
            if (!Native.MonitorExists(Display.Handle)) { ShowScreenshotResult("屏幕已断开，请重试"); return; }
            var monitor = Display.Info.Monitor;
            var result = await ScreenshotService.Capture(new RectInt32(monitor.Left, monitor.Top,
                monitor.Right - monitor.Left, monitor.Bottom - monitor.Top), App.Screenshots.Value, preview: true);
            if (!closing && !result.Busy)
            {
                ShowScreenshotResult(result.Message);
                if (result.PreviewBytes != null)
                {
                    screenshotPreview ??= new ScreenshotPreviewWindow(id => OpenWorkItem(new(WorkKind.Note, id)));
                    await screenshotPreview.Open(result, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
                }
            }
        }
        finally { preparingScreenshot = false; }
    }
    void ShowScreenshotResult(string message)
    {
        if (closing || !windowShown || actions.Visibility != Visibility.Visible) return;
        screenshotTip.Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 310 };
        screenshotTip.IsOpen = true; screenshotTipTimer.Stop(); screenshotTipTimer.Start();
    }
}
