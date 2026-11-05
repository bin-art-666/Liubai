using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    Expander privacySection = null!;
    StackPanel privacyDevices = null!;
    TextBlock privacyStatus = null!, privacyResults = null!;
    Button privacyToggle = null!, privacyRefresh = null!;
    bool privacySelectionSync;
    internal bool PrivacyVisible => privacySection.IsExpanded;
    internal void OpenPrivacy() { privacySection.IsExpanded = true; privacySection.StartBringIntoView(); }
    Expander BuildPrivacySettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        privacyStatus = Hint(""); panel.Children.Add(privacyStatus);
        privacyToggle = new Button(); AutomationProperties.SetName(privacyToggle, "开启或恢复隐私设备");
        privacyToggle.Click += async (_, _) => await App.Privacy.Toggle(); panel.Children.Add(privacyToggle);
        panel.Children.Add(Hint("左键顶栏锁形图标直接开启／恢复，右键进入这里。首次默认选择当前支持的摄像头和录音输入，此后保留你的选择。开启时申请管理员授权；摄像头禁用可能影响 Windows Hello，麦克风禁用会中断录音和通话。"));
        privacyRefresh = new Button { Content = "刷新设备列表（不会开启摄像头）" };
        privacyRefresh.Click += async (_, _) => await LoadPrivacyDevices(); panel.Children.Add(privacyRefresh);
        privacyDevices = new StackPanel { Spacing = 8 }; panel.Children.Add(privacyDevices);
        privacyResults = Hint(""); panel.Children.Add(privacyResults);
        panel.Children.Add(Hint("定位：由 Windows 系统设置管理，留白不会自动关闭。关闭系统定位也无法阻止网站通过 IP 等推测位置。"));
        var location = new Button { Content = "打开 Windows 定位设置" };
        location.Click += async (_, _) => { try { await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-location")); } catch { privacyStatus.Text = "无法打开系统设置，请手动进入隐私和安全性 → 位置。"; } };
        panel.Children.Add(location);
        panel.Children.Add(Hint("退出留白不会自动开启设备；再次启动可从这里恢复。只恢复本次操作的设备，原本关闭的保持关闭。未连接、被驱动拒绝或状态不明的设备会保留恢复记录。新插入的设备不自动禁用，请刷新后选择。"));
        panel.Children.Add(Hint("麦克风控制针对选中的 Windows 录音输入端点，不能保证覆盖驱动提供的其他采集接口。此功能不等同于物理断电。"));
        privacySection = new Expander { Header = "隐私保护", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        privacySection.Expanding += async (_, _) => { if (privacyDevices.Children.Count == 0) await LoadPrivacyDevices(); };
        App.Privacy.Changed += RefreshPrivacySettings;
        Closed += (_, _) => App.Privacy.Changed -= RefreshPrivacySettings;
        RefreshPrivacySettings(); return privacySection;
    }
    async Task LoadPrivacyDevices()
    {
        privacyRefresh.IsEnabled = false;
        try
        {
            var devices = await App.Privacy.Devices(); privacyDevices.Children.Clear();
            var selected = App.Privacy.SelectedDevices(devices);
            foreach (var device in devices)
            {
                var check = new CheckBox { Content = device.Kind + " · " + device.Name + (device.Disabled ? "（已禁用）" : device.Enabled ? "" : "（状态异常）"),
                    Tag = device.Id, IsChecked = selected.Contains(device.Id, StringComparer.OrdinalIgnoreCase) };
                AutomationProperties.SetName(check, device.Kind + " · " + device.Name);
                check.Checked += (_, _) => SavePrivacySelection(); check.Unchecked += (_, _) => SavePrivacySelection();
                privacyDevices.Children.Add(check);
            }
            // Retain disconnected selections rather than silently forgetting them.
            foreach (string id in App.Privacy.Options.Devices!.Where(id => !devices.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            {
                var check = new CheckBox { Content = "未连接的已选设备", Tag = id, IsChecked = true };
                check.Unchecked += (_, _) => SavePrivacySelection(); check.Checked += (_, _) => SavePrivacySelection(); privacyDevices.Children.Add(check);
            }
            if (privacyDevices.Children.Count == 0) privacyDevices.Children.Add(Hint("未发现支持的摄像头或录音输入设备。"));
        }
        catch { privacyStatus.Text = "设备列表读取失败，请重试。"; }
        finally { privacyRefresh.IsEnabled = true; RefreshPrivacySettings(); }
    }
    void SavePrivacySelection()
    {
        if (privacySelectionSync) return;
        try { App.Privacy.SaveSelection(privacyDevices.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag)); }
        catch (Exception ex)
        {
            privacySelectionSync = true;
            try { foreach (var check in privacyDevices.Children.OfType<CheckBox>()) check.IsChecked = App.Privacy.Options.Devices!.Contains((string)check.Tag, StringComparer.OrdinalIgnoreCase); }
            finally { privacySelectionSync = false; }
            privacyStatus.Text = ex.Message;
        }
    }
    void RefreshPrivacySettings()
    {
        if (privacyStatus is null) return;
        var service = App.Privacy; privacyStatus.Text = service.Status;
        privacyToggle.Content = service.Busy ? "正在处理…" : service.Journal.Active ? "恢复之前的设备" : "开启一键隐私";
        privacyToggle.IsEnabled = !service.Busy && !service.Broken;
        foreach (var check in privacyDevices.Children.OfType<CheckBox>()) check.IsEnabled = !service.Busy && !service.Journal.Active && !service.Broken;
        privacyResults.Text = string.Join("\n", (service.Journal.Entries ?? []).Select(e => e.Kind + " · " + e.Name + "：" + (service.Journal.Active && e.State == "disabled" ?
            service.Observed.TryGetValue(e.Id, out var device) ? device.Disabled ? "当前已禁用" : device.Enabled ? "当前已开启（状态已变化）" : "当前状态异常" : "尚未核验或设备未连接"
            : e.Error.Length > 0 ? e.Error : e.State switch
        { "disabled" => "已核验禁用", "pending" => "状态未确认，请恢复或检查设备管理器", "restored" => "已处理／未修改", "failed" => "未能处理", _ => "尚未操作" })));
    }
}
