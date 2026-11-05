using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    NumberBox stationCapacity = null!;
    TextBlock stationArchiveDirectory = null!;
    ToggleSwitch stationRemoveArchived = null!;
    bool stationSync;
    Task<bool> stationOptionsSave = Task.FromResult(true);

    Expander BuildStationSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        stationCapacity = new NumberBox { Header = "最多保存项目数", Minimum = 1, Maximum = StationStore.MaximumCapacity,
            SmallChange = 1, LargeChange = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Value = App.Station.Capacity };
        AutomationProperties.SetName(stationCapacity, "中转站最多保存项目数");
        panel.Children.Add(stationCapacity);
        panel.Children.Add(Hint("默认 200 项，可设置 1～2000 项。调低上限保留已有内容，超出时暂停新增；不会自动删除旧项目。"));
        panel.Children.Add(Hint("归档保存位置"));
        stationArchiveDirectory = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        panel.Children.Add(stationArchiveDirectory);
        var choose = new Button { Content = "选择归档目录", IsEnabled = !App.Station.ReadOnly };
        choose.Click += async (_, _) =>
        {
            using var lease = KeepOpen(); choose.IsEnabled = false;
            try
            {
                string? directory = await StationArchivePicker.Pick(this);
                if (directory != null)
                {
                    if (!await SaveStationSettings()) return;
                    await App.Station.UpdateOptions(App.Station.Capacity, directory, App.Station.RemoveAfterArchive);
                    RefreshStationSettings(); status.Text = "中转站归档位置已保存";
                }
            }
            catch { status.Text = "归档位置保存失败，请重试"; }
            finally { choose.IsEnabled = !App.Station.ReadOnly; }
        };
        panel.Children.Add(choose);
        stationRemoveArchived = new ToggleSwitch { Header = "归档成功后移出中转站", OnContent = "移出成功归档的记录", OffContent = "保留所有记录", IsEnabled = !App.Station.ReadOnly };
        AutomationProperties.SetName(stationRemoveArchived, "归档成功后移出中转站"); panel.Children.Add(stationRemoveArchived);
        panel.Children.Add(Hint("归档复制实际文件，文字保存为 TXT，链接保存为 URL。取消或失败的记录保留，原文件不删除。此分组独立于外观配置。"));
        stationCapacity.IsEnabled = !App.Station.ReadOnly;
        stationCapacity.ValueChanged += async (_, _) => { if (!stationSync && interactive && !syncing) await SaveStationSettings(); };
        stationRemoveArchived.Toggled += async (_, _) => { if (!stationSync && interactive && !syncing) await SaveStationSettings(); };
        RefreshStationSettings();
        return new Expander { Header = "中转站设置", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void RefreshStationSettings()
    {
        if (stationCapacity == null) return;
        stationSync = true;
        try
        {
            stationCapacity.Value = App.Station.Capacity;
            stationArchiveDirectory.Text = App.Station.ArchiveDirectory.Length == 0 ? "尚未选择，首次归档时选择" : App.Station.ArchiveDirectory;
            stationRemoveArchived.IsOn = App.Station.RemoveAfterArchive;
        }
        finally { stationSync = false; }
    }
    Task<bool> SaveStationSettings()
    {
        if (stationCapacity == null || App.Station.ReadOnly) return Task.FromResult(true);
        string input = stationCapacity.IsLoaded ? stationCapacity.Text : stationCapacity.Value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        if (!int.TryParse(input, out int capacity) || capacity is < 1 or > StationStore.MaximumCapacity)
        { status.Text = "中转站数量请输入 1～2000 的整数"; return Task.FromResult(false); }
        bool remove = stationRemoveArchived.IsOn;
        var previous = stationOptionsSave;
        async Task<bool> Save()
        {
            await previous;
            try { await App.Station.UpdateOptions(capacity, App.Station.ArchiveDirectory, remove); return true; }
            catch { status.Text = "中转站设置保存失败，请重试"; return false; }
        }
        return stationOptionsSave = Save();
    }
    internal Task<bool> FlushStationOptions() => IsOpen && !SaveGuardOptions() ? Task.FromResult(false) : IsOpen ? SaveStationSettings() : stationOptionsSave;
}
