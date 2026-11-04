using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    ToggleSwitch screenshotSave = null!;
    TextBlock screenshotDirectory = null!;
    Expander BuildScreenshotSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Hint("点击相机直接截取所在的整块屏幕并复制，无需框选。"));
        screenshotSave = new ToggleSwitch { Header = "同时保存 PNG 文件", OnContent = "复制并保存", OffContent = "仅复制到剪贴板" };
        panel.Children.Add(screenshotSave);
        panel.Children.Add(Hint("保存目录"));
        screenshotDirectory = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13 };
        panel.Children.Add(screenshotDirectory);
        var choose = new Button { Content = "更改保存目录", HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(choose);
        screenshotSave.Toggled += (_, _) =>
        {
            if (syncing || !interactive) return;
            try { App.Screenshots.Save(App.Screenshots.Value with { SaveFiles = screenshotSave.IsOn }); status.Text = "截图设置已保存"; }
            catch
            {
                syncing = true; screenshotSave.IsOn = App.Screenshots.Value.SaveFiles; syncing = false;
                status.Text = "截图设置保存失败，请重试";
            }
        };
        choose.Click += async (_, _) =>
        {
            if (fileDialogOpen) return;
            fileDialogOpen = true; using var lease = KeepOpen();
            try
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync();
                if (folder == null) return;
                App.Screenshots.Save(App.Screenshots.Value with { Directory = folder.Path });
                RefreshScreenshotSettings(); status.Text = "截图目录已保存";
            }
            catch { status.Text = "截图目录未更改，请重试"; }
            finally { fileDialogOpen = false; }
        };
        return new Expander { Header = "截图设置", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void RefreshScreenshotSettings()
    { screenshotSave.IsOn = App.Screenshots.Value.SaveFiles; screenshotDirectory.Text = App.Screenshots.Value.SaveDirectory; }
}
