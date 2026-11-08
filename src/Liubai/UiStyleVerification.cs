using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    internal async Task PreviewUiStyleFixture()
    {
        using var lease = KeepOpen();
        Open(display.Info.Work.Top);
        var section = StationControlsForStyle<Expander>((DependencyObject)Content).Single(e => Equals(e.Header, "界面风格"));
        section.IsExpanded = true;
        await Task.Delay(TimeSpan.FromSeconds(60));
    }
    static IEnumerable<T> StationControlsForStyle<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in StationControlsForStyle<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    internal async Task<string[]> VerifyUiStyle(Action<UiStyleOptions> inspect)
    {
        using var visibility = KeepOpen();
        var checks = new List<string>();
        async Task Until(Func<bool> ready)
        {
            long end = Environment.TickCount64 + 5000;
            while (!ready()) { if (Environment.TickCount64 > end) throw new Exception("Style preview/save timed out: " + uiStyleStatus.Text); await Task.Delay(40); }
        }
        await Until(() => interactive);
        uiPreset.SelectedIndex = (int)UiStylePreset.Rounded;
        await Until(() => App.UiStyle.Data.Preset == UiStylePreset.Rounded);
        inspect(PanelTheme.Current);
        if (uiPreset.CornerRadius.TopLeft != 12 || uiRoundness.Value != 12) throw new Exception("Existing native controls did not adopt preset corners");
        if (new UiStyleStore(Path.Combine(App.DataDirectory, "ui-style.json")).Data != PanelTheme.Current) throw new Exception("Preset not persisted");
        checks.Add("Preset updates existing native controls and a second live panel; atomic configuration reload matches preview");
        uiRoundness.Value = 13; uiIcons.SelectedIndex = (int)UiIconSize.Large;
        await Until(() => App.UiStyle.Data.Roundness == 13 && App.UiStyle.Data.Icons == UiIconSize.Large);
        inspect(PanelTheme.Current);
        if (uiPreset.SelectedIndex != (int)UiStylePreset.Custom || uiPreset.CornerRadius.TopLeft != 13) throw new Exception("Manual edits did not become a live custom style");
        uiMotion.SelectedIndex = (int)UiMotion.Off;
        await Until(() => App.UiStyle.Data.Motion == UiMotion.Off);
        if (uiDuration.IsEnabled) throw new Exception("Disabled animation retained active duration slider");
        checks.Add("Manual edits become Custom, live icon targets resize, and animation-off disables duration editing");
        uiTint.Value = 31;
        visibility.Dispose();
        Hide();
        if (App.UiStyle.Data.Tint != 31 || PanelTheme.Current.Tint != 31) throw new Exception("Immediate hide lost pending preview/save");
        checks.Add("Closing before debounce flushes the latest preview and settings");
        using var reopenedVisibility = KeepOpen();
        Open(display.Info.Work.Top); await Task.Delay(100);
        QueueUiStyle(new()); SaveUiStyle();
        inspect(PanelTheme.Current);
        if (App.UiStyle.Data != new UiStyleOptions() || uiPreset.SelectedIndex != 0) throw new Exception("Default restore failed");
        uiShade.Value = 66; FlushChanges(force: true);
        if (App.UiStyle.Data.Shade != 66 || !status.Text.Contains("全部设置已保存")) throw new Exception("Save all did not flush style settings");
        QueueUiStyle(new()); SaveUiStyle();
        checks.Add("Default restore synchronizes controls; Save all persists pending style values");
        return checks.ToArray();
    }
}

internal sealed partial class StationWindow
{
    internal void InspectUiStyle(UiStyleOptions value)
    {
        if (root.Padding != PanelTheme.Value<Thickness>("PanelPadding") || root.RowSpacing != PanelTheme.Value<double>("PanelGap")
            || expand.Width != PanelTheme.Value<double>("ActionSize") || expand.Content is not Viewbox icon || icon.Width != PanelTheme.Value<double>("IconSize"))
            throw new Exception("Existing station panel did not update geometry/icons");
        var card = cards.Children.OfType<Border>().First();
        if (card.CornerRadius.TopLeft != value.Roundness * 1.5 || card.Padding != PanelTheme.Value<Thickness>("CardPadding")) throw new Exception("Existing card did not update");
    }
}

public sealed partial class BarWindow
{
    async Task VerifyUiStyle()
    {
        string result = "PASS", error = ""; var checks = new List<string>();
        StationWindow? panel = null; SettingsWindow? settings = null;
        try
        {
            await Task.Delay(450); if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before style verification");
            var store = new StationStore(Path.Combine(App.DataDirectory, "style-station.json")); await store.Add([StationStore.Text("风格测试保留内容")]);
            byte[] data = File.ReadAllBytes(Path.Combine(App.DataDirectory, "style-station.json"));
            panel = new StationWindow(store, Display); panel.Open(Display, Display.Info.Work.Top); panel.ToggleDetached();
            var bounds = panel.AppWindow.Size; var backdrop = panel.SystemBackdrop;
            var preferences = new AppearanceStore(Path.Combine(App.DataDirectory, "style-appearance.json")); var originalMaterial = preferences.Material;
            settings = new SettingsWindow(preferences, () => { }, () => { }, () => { }, () => { }, Display); settings.Open(Display.Info.Work.Top);
            checks.AddRange(await settings.VerifyUiStyle(panel.InspectUiStyle));
            if (panel.AppWindow.Size != bounds || !ReferenceEquals(backdrop, panel.SystemBackdrop) || !File.ReadAllBytes(Path.Combine(App.DataDirectory, "style-station.json")).SequenceEqual(data)
                || preferences.Material != originalMaterial) throw new Exception("Style changes resized a window, recreated backdrop, changed top bar or modified feature data");
            checks.Add("Visual changes preserve window geometry, backdrop instance, top-bar material and station data");
            if (Environment.GetCommandLineArgs().Contains("--preview-ui-style")) await settings.PreviewUiStyleFixture();
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            settings?.Destroy(); panel?.Destroy();
            File.WriteAllText(Path.Combine(App.DataDirectory, "ui-style-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks })); Close();
        }
    }
}
