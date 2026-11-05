using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    ToggleSwitch allDisplays = null!, followPaused = null!, startup = null!;
    TextBlock startupInfo = null!;
    ComboBox personalPresets = null!;
    TextBox presetName = null!;
    Button usePreset = null!, deletePreset = null!;
    bool fileDialogOpen;

    Expander BuildDailySettings()
    {
        var panel = new StackPanel { Spacing = 14 };
        allDisplays = new ToggleSwitch { Header = "显示范围", OnContent = "所有屏幕", OffContent = "仅主屏" };
        followPaused = new ToggleSwitch { Header = "暂停颜色跟随", OnContent = "保持当前外观", OffContent = "自动跟随窗口" };
        startup = new ToggleSwitch { Header = "开机启动", OnContent = "登录后延迟 15 秒启动", OffContent = "手动启动" };
        panel.Children.Add(allDisplays); panel.Children.Add(followPaused); panel.Children.Add(startup);
        startupInfo = Hint(StartupService.RegistrationWarning ?? "通过当前用户的登录任务启动，异常退出会重试；无需管理员权限。");
        panel.Children.Add(startupInfo);
        panel.Children.Add(Hint("多屏分别跟随各自最上层窗口，共用高度和外观设置。暂停时保留当前颜色，恢复后重新识别。"));
        var import = new Button { Content = "导入配置", HorizontalAlignment = HorizontalAlignment.Stretch };
        var export = new Button { Content = "导出配置", HorizontalAlignment = HorizontalAlignment.Stretch };
        var reset = new Button { Content = "恢复默认设置", HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(import); panel.Children.Add(export); panel.Children.Add(reset);
        panel.Children.Add(Hint("导入会替换当前配置及个人预设，可先导出备份。开机启动是本机选项，不参与导入导出；恢复默认会保留个人预设和开机启动选择。"));
        allDisplays.Toggled += (_, _) => SaveGeneral();
        followPaused.Toggled += (_, _) => SaveGeneral();
        startup.Toggled += (_, _) =>
        {
            if (syncing || !interactive) return;
            try
            {
                StartupService.SetEnabled(startup.IsOn);
                status.Text = startup.IsOn ? "已开启开机启动，登录后延迟 15 秒启动" : "已关闭开机启动";
                startupInfo.Text = "通过当前用户的登录任务启动，异常退出会重试；无需管理员权限。";
            }
            catch
            {
                syncing = true; try { startup.IsOn = StartupService.Enabled; } catch { startup.IsOn = false; } finally { syncing = false; }
                status.Text = "开机启动设置失败，请检查系统权限";
            }
        };
        import.Click += async (_, _) => await ImportConfiguration();
        export.Click += async (_, _) => await ExportConfiguration();
        reset.Click += async (_, _) =>
        {
            using var lease = KeepOpen();
            var confirm = new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot,
                Title = "恢复默认设置？", Content = "恢复默认外观、字号、图标顺序、36 像素高度及所有屏幕显示，并恢复颜色跟随。个人预设和开机启动选择会保留。",
                PrimaryButtonText = "恢复默认", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                saveTimer.Stop(); previewQueued = heightQueued = expandedQueued = false;
                preferences.ResetDefaults(); dirty = false;
                ApplyConfiguration();
                status.Text = "已恢复默认设置并保存";
            }
            catch { status.Text = "恢复默认失败，当前配置未替换"; }
        };
        return new Expander { Header = "日常设置与配置", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    Expander BuildPersonalPresets()
    {
        var panel = new StackPanel { Spacing = 10 };
        personalPresets = Combo("我的预设");
        presetName = new TextBox { Header = "预设名称", PlaceholderText = "例如：浅色办公", MaxLength = 40 };
        var save = new Button { Content = "保存为我的预设", HorizontalAlignment = HorizontalAlignment.Stretch };
        usePreset = new Button { Content = "应用选中预设", HorizontalAlignment = HorizontalAlignment.Stretch };
        deletePreset = new Button { Content = "删除选中预设", HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(personalPresets); panel.Children.Add(presetName); panel.Children.Add(save);
        panel.Children.Add(usePreset); panel.Children.Add(deletePreset);
        panel.Children.Add(Hint("保存日常材质、最大化外观、切换动画与栏高。同名预设会更新，最多 30 个。"));
        personalPresets.SelectionChanged += (_, _) =>
        {
            bool selected = personalPresets.SelectedIndex >= 0;
            usePreset.IsEnabled = deletePreset.IsEnabled = selected;
            if (!syncing && selected) presetName.Text = preferences.Presets[personalPresets.SelectedIndex].Name;
        };
        save.Click += (_, _) =>
        {
            if (!PrepareSave()) return;
            try { preferences.SavePreset(presetName.Text); RefreshPersonalPresets(); status.Text = "个人预设已保存"; }
            catch (Exception ex) { status.Text = ex is ArgumentException or InvalidOperationException ? ex.Message : "个人预设保存失败"; }
        };
        usePreset.Click += (_, _) =>
        {
            if (personalPresets.SelectedIndex < 0 || !PrepareSave()) return;
            string id = preferences.Presets[personalPresets.SelectedIndex].Id;
            try { preferences.ApplyPreset(id); ApplyConfiguration(); status.Text = "个人预设已应用并保存"; }
            catch { status.Text = "应用预设失败，当前配置未替换"; }
        };
        deletePreset.Click += (_, _) =>
        {
            if (personalPresets.SelectedIndex < 0) return;
            try { preferences.DeletePreset(preferences.Presets[personalPresets.SelectedIndex].Id); RefreshPersonalPresets(); status.Text = "已删除选中的个人预设"; }
            catch { status.Text = "预设删除失败"; }
        };
        return new Expander { Header = "个人预设", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    void RefreshPersonalPresets()
    {
        string? name = personalPresets.SelectedItem as string;
        personalPresets.Items.Clear();
        foreach (var preset in preferences.Presets) personalPresets.Items.Add(preset.Name);
        personalPresets.SelectedIndex = name != null ? preferences.Presets.ToList().FindIndex(p => p.Name == name) : -1;
        usePreset.IsEnabled = deletePreset.IsEnabled = personalPresets.SelectedIndex >= 0;
    }
    void RefreshDailySettings()
    {
        allDisplays.IsOn = preferences.AllDisplays; followPaused.IsOn = preferences.FollowPaused;
        try { startup.IsOn = StartupService.Enabled; startup.IsEnabled = true; } catch { startup.IsEnabled = false; }
        RefreshPersonalPresets();
        RefreshScreenshotSettings();
    }
    bool PrepareSave()
    {
        bool valid = (automatic.IsOn || MaterialOptions.IsColor(colorInput.Text))
            && (!expandedEnabled.IsOn || expandedMode.SelectedIndex != (int)ExpandedMode.Custom || MaterialOptions.IsColor(expandedColor.Text));
        FlushChanges(force: true);
        return valid && !dirty;
    }
    void ApplyConfiguration()
    {
        RefreshStyle(preferences.Style); apply(); resize(); adapt();
        Reposition(display.Info.Work.Top);
    }
    void SaveGeneral()
    {
        if (syncing || !interactive) return;
        if (!PrepareSave()) { RefreshStyle(preferences.Style); return; }
        bool oldAll = preferences.AllDisplays, oldPaused = preferences.FollowPaused;
        preferences.SetAllDisplays(allDisplays.IsOn); preferences.SetFollowPaused(followPaused.IsOn);
        try { preferences.SaveCurrent(); adapt(); status.Text = "日常设置已保存"; }
        catch { preferences.SetAllDisplays(oldAll); preferences.SetFollowPaused(oldPaused); RefreshStyle(preferences.Style); ShowSaveError(); }
    }
    async Task ImportConfiguration()
    {
        if (fileDialogOpen || !PrepareSave()) return;
        fileDialogOpen = true;
        using var lease = KeepOpen();
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            preferences.Import(file.Path); dirty = false;
            ApplyConfiguration();
            status.Text = "配置已导入并保存";
        }
        catch { status.Text = "导入失败：配置格式、版本或文件权限有误，当前设置未替换"; }
        finally { fileDialogOpen = false; }
    }
    async Task ExportConfiguration()
    {
        if (fileDialogOpen || !PrepareSave()) return;
        fileDialogOpen = true;
        using var lease = KeepOpen();
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "留白配置" };
            picker.FileTypeChoices.Add("留白配置", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            preferences.Export(file.Path); status.Text = "配置已导出";
        }
        catch { status.Text = "导出失败，请检查目标文件权限"; }
        finally { fileDialogOpen = false; }
    }
}
