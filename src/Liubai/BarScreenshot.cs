using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Liubai;

public sealed partial class BarWindow
{
    bool preparingScreenshot;
    async Task TakeScreenshot()
    {
        if (preparingScreenshot || closing) return;
        preparingScreenshot = true;
        try
        {
            screenshotTipTimer.Stop(); screenshotTip.IsOpen = false;
            screenshotTip.Content = "一键截取整个屏幕";
            VisualStateManager.GoToState(screenshot, "Normal", false);
            // Let pointer/tooltip visuals settle before capturing any feedback.
            await Task.Delay(160);
            if (closing) return;
            if (!Native.MonitorExists(Display.Handle)) { ShowScreenshotResult("屏幕已断开，请重试"); return; }
            var monitor = Display.Info.Monitor;
            var result = await ScreenshotService.Capture(new RectInt32(monitor.Left, monitor.Top,
                monitor.Right - monitor.Left, monitor.Bottom - monitor.Top), App.Screenshots.Value);
            if (!closing && !result.Busy) ShowScreenshotResult(result.Message);
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
