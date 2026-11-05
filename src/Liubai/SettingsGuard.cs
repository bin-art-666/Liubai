using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    ToggleSwitch guardEnabled = null!;
    ComboBox guardCamera = null!, guardMode = null!;
    TextBox guardMessage = null!;
    NumberBox guardHold = null!;
    TextBlock guardStatus = null!;
    Microsoft.UI.Dispatching.DispatcherQueueTimer guardSaveTimer = null!;
    bool guardSync;
    sealed record CameraChoice(string Id, string Name) { public override string ToString() => Name; }
    Expander BuildGuardSettings()
    {
        var options = App.GuardOptions.Data;
        var panel = new StackPanel { Spacing = 12 };
        guardEnabled = new ToggleSwitch { Header = "启用屏幕守护", OnContent = "本地检测", OffContent = "摄像头关闭" };
        AutomationProperties.SetName(guardEnabled, "启用屏幕守护"); panel.Children.Add(guardEnabled);
        guardStatus = Hint(""); panel.Children.Add(guardStatus);
        guardCamera = new ComboBox { Header = "摄像头", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !App.GuardOptions.ReadOnly };
        guardCamera.Items.Add(new CameraChoice("", "默认摄像头"));
        if (options.CameraId.Length > 0) guardCamera.Items.Add(new CameraChoice(options.CameraId, "已保存的摄像头（刷新列表查看）"));
        guardCamera.SelectedIndex = guardCamera.Items.Count - 1;
        AutomationProperties.SetName(guardCamera, "屏幕守护摄像头"); panel.Children.Add(guardCamera);
        var refresh = new Button { Content = "刷新摄像头列表", IsEnabled = !App.GuardOptions.ReadOnly };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try
            {
                if (!SaveGuardOptions()) return;
                string selected = App.GuardOptions.Data.CameraId;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var devices = await GuardCameraFactory.Devices(timeout.Token);
                guardSync = true;
                try
                {
                    guardCamera.Items.Clear(); guardCamera.Items.Add(new CameraChoice("", "默认摄像头"));
                    foreach (var device in devices) guardCamera.Items.Add(new CameraChoice(device.Id, device.Name));
                    if (selected.Length > 0 && !devices.Any(d => d.Id == selected)) guardCamera.Items.Add(new CameraChoice(selected, "所选摄像头暂不可用"));
                    guardCamera.SelectedItem = guardCamera.Items.OfType<CameraChoice>().First(c => c.Id == selected);
                }
                finally { guardSync = false; }
                status.Text = $"发现 {devices.Length} 个摄像头（刷新列表不会打开画面）";
            }
            catch { status.Text = "摄像头列表读取失败，请检查连接或权限"; }
            finally { refresh.IsEnabled = !App.GuardOptions.ReadOnly; }
        };
        panel.Children.Add(refresh);
        guardMode = Combo("提醒方式", "屏幕中央提示", "全部屏幕黑色遮挡"); guardMode.SelectedIndex = (int)options.Mode;
        AutomationProperties.SetName(guardMode, "屏幕守护提醒方式"); guardMode.IsEnabled = !App.GuardOptions.ReadOnly; panel.Children.Add(guardMode);
        guardHold = new NumberBox { Header = "触发等待时间（秒）", Minimum = .5, Maximum = 5, SmallChange = .5, LargeChange = 1,
            Value = options.HoldMilliseconds / 1000d, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, IsEnabled = !App.GuardOptions.ReadOnly };
        AutomationProperties.SetName(guardHold, "屏幕守护触发等待时间"); panel.Children.Add(guardHold);
        panel.Children.Add(Hint("可设置 0.5～5 秒，默认 1.5 秒。时间越长，越不容易被路过的人触发。右键盾牌可暂停 10 分钟或立即恢复，暂停期间释放摄像头。"));
        guardMessage = new TextBox { Header = "自定义提示内容", Text = options.Message, MaxLength = 200, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsEnabled = !App.GuardOptions.ReadOnly };
        AutomationProperties.SetName(guardMessage, "屏幕守护提示内容"); panel.Children.Add(guardMessage);
        var test = new Button { Content = "测试提醒（不打开摄像头）" };
        test.Click += (_, _) => { if (SaveGuardOptions()) App.Guard.TestAlert(); }; panel.Children.Add(test);
        panel.Children.Add(Hint("默认关闭，每次启动需手动开启。至少两张人脸持续达到所设时间才提醒；按 Enter 解除，冷却 30 秒后继续。"));
        panel.Children.Add(Hint("画面仅在本机内存处理，不保存、不上传，不使用麦克风。最多每秒分析 2 次，画面不超过 320×240。只检测视野内额外人脸，无法确认注视方向。锁屏和睡眠时释放摄像头。"));
        if (App.GuardOptions.Warning is { } warning) panel.Children.Add(Hint(warning));
        guardSaveTimer = DispatcherQueue.CreateTimer(); guardSaveTimer.IsRepeating = false; guardSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        guardSaveTimer.Tick += (_, _) => SaveGuardOptions();
        guardMessage.TextChanged += (_, _) => { if (!guardSync && !syncing && interactive) { guardSaveTimer.Stop(); guardSaveTimer.Start(); } };
        guardCamera.SelectionChanged += (_, _) => { if (!guardSync && !syncing && interactive) SaveGuardOptions(); };
        guardMode.SelectionChanged += (_, _) => { if (!guardSync && !syncing && interactive) SaveGuardOptions(); };
        guardHold.ValueChanged += (_, e) => { if (!guardSync && !syncing && interactive) SaveGuardOptions(e.NewValue); };
        guardEnabled.Toggled += async (_, _) =>
        {
            if (guardSync || syncing || !interactive) return;
            if (!SaveGuardOptions()) { RefreshGuardSettings(); return; }
            await App.Guard.SetEnabled(guardEnabled.IsOn);
        };
        App.Guard.Changed += RefreshGuardSettings;
        Closed += (_, _) => { guardSaveTimer.Stop(); App.Guard.Changed -= RefreshGuardSettings; };
        RefreshGuardSettings();
        return new Expander { Header = "屏幕守护", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void RefreshGuardSettings()
    {
        if (guardEnabled == null) return;
        guardSync = true;
        try { guardEnabled.IsOn = App.Guard.Wanted; guardStatus.Text = App.Guard.Status; }
        finally { guardSync = false; }
    }
    bool SaveGuardOptions(double? holdOverride = null)
    {
        if (guardMessage == null || App.GuardOptions.ReadOnly) return true;
        guardSaveTimer.Stop();
        try
        {
            string input = guardHold.IsLoaded ? guardHold.Text : guardHold.Value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            double seconds = holdOverride ?? (double.TryParse(input, out double parsed) ? parsed : double.NaN);
            if (!double.IsFinite(seconds) || seconds is < .5 or > 5)
                throw new InvalidDataException("触发等待时间请输入 0.5～5 秒。");
            App.GuardOptions.Save(App.GuardOptions.Data with { CameraId = (guardCamera.SelectedItem as CameraChoice)?.Id ?? "", Mode = (GuardAlertMode)guardMode.SelectedIndex, Message = guardMessage.Text,
                HoldMilliseconds = (int)Math.Round(seconds * 1000) });
            return true;
        }
        catch (Exception ex) { status.Text = ex is InvalidDataException ? ex.Message : "守护设置保存失败，请重试"; return false; }
    }
}
