using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    ComboBox uiPreset = null!, uiDensity = null!, uiIcons = null!, uiMotion = null!;
    Slider uiRoundness = null!, uiTint = null!, uiShade = null!, uiDuration = null!;
    TextBlock uiStyleStatus = null!;
    Microsoft.UI.Dispatching.DispatcherQueueTimer uiSaveTimer = null!;
    bool uiSync, uiPreviewQueued;
    UiStyleOptions? uiPending;

    Expander BuildUiStyleSettings()
    {
        var body = new StackPanel { Spacing = 14 };
        uiPreset = Combo("风格预设", "默认轻透", "柔和圆润", "紧凑清爽", "自定义"); body.Children.Add(uiPreset);
        uiRoundness = AddSlider(body, "圆角程度", "统一调整卡片、按钮与输入框，0 为直角。", 0, 20, "", PreviewUiStyle);
        uiDensity = Combo("界面疏密", "紧凑", "标准", "宽松"); body.Children.Add(uiDensity);
        uiIcons = Combo("面板图标大小", "小", "标准", "大"); body.Children.Add(uiIcons);
        body.Children.Add(Hint("仅调整功能面板图标；小图标仍保留足够的点击区域。"));
        uiTint = AddSlider(body, "面板染色浓度", "越低越通透，越高颜色越浓。", 0, 100, "%", PreviewUiStyle);
        uiShade = AddSlider(body, "面板明暗遮罩", "调节玻璃遮蔽感，颜色自动跟随系统明暗。", 0, 100, "%", PreviewUiStyle);
        uiMotion = Combo("面板动画", "关闭", "轻柔"); body.Children.Add(uiMotion);
        uiDuration = AddSlider(body, "打开动画时长", "数值越小越快；系统关闭动画时直接显示。", 100, 300, " ms", PreviewUiStyle);
        body.Children.Add(Hint("修改即时预览，停止调节后自动保存。Windows 最外层窗口圆角由系统控制；AI 网页内部样式由网站决定。"));
        var reset = new Button { Content = "恢复默认风格", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(reset, "恢复默认界面风格");
        reset.Click += (_, _) => { QueueUiStyle(new()); SaveUiStyle(); }; body.Children.Add(reset);
        uiStyleStatus = Hint(App.UiStyle.Warning ?? "界面风格独立保存，不改变顶栏外观与功能数据。");
        AutomationProperties.SetLiveSetting(uiStyleStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); body.Children.Add(uiStyleStatus);
        uiSaveTimer = DispatcherQueue.CreateTimer(); uiSaveTimer.IsRepeating = false; uiSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
        uiSaveTimer.Tick += (_, _) => SaveUiStyle();
        uiPreset.SelectionChanged += (_, _) =>
        {
            if (uiSync || !interactive || uiPreset.SelectedIndex < 0) return;
            var selected = (UiStylePreset)uiPreset.SelectedIndex;
            QueueUiStyle(selected == UiStylePreset.Custom ? PanelTheme.Current with { Preset = selected } : UiStyleOptions.ForPreset(selected));
        };
        uiDensity.SelectionChanged += (_, _) => PreviewUiStyle(); uiIcons.SelectionChanged += (_, _) => PreviewUiStyle();
        uiMotion.SelectionChanged += (_, _) => PreviewUiStyle();
        PanelTheme.Changed += RefreshUiStyle;
        Closed += (_, _) => { uiSaveTimer.Stop(); PanelTheme.Changed -= RefreshUiStyle; };
        RefreshUiStyle();
        if (App.UiStyle.ReadOnly) foreach (var control in body.Children.OfType<Control>()) control.IsEnabled = false;
        return new Expander { Header = "界面风格", Content = body, IsEnabled = !App.UiStyle.ReadOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void RefreshUiStyle()
    {
        uiSync = true;
        try
        {
            var value = uiPending ?? PanelTheme.Current;
            uiPreset.SelectedIndex = (int)value.Preset; uiRoundness.Value = value.Roundness;
            uiDensity.SelectedIndex = (int)value.Density; uiIcons.SelectedIndex = (int)value.Icons;
            uiTint.Value = value.Tint; uiShade.Value = value.Shade; uiMotion.SelectedIndex = (int)value.Motion;
            uiDuration.Value = value.Duration; uiDuration.IsEnabled = value.Motion != UiMotion.Off;
        }
        finally { uiSync = false; }
    }
    void PreviewUiStyle()
    {
        if (uiSync || !interactive || App.UiStyle.ReadOnly || uiDensity.SelectedIndex < 0 || uiIcons.SelectedIndex < 0 || uiMotion.SelectedIndex < 0) return;
        QueueUiStyle(new(Preset: UiStylePreset.Custom, Roundness: (int)uiRoundness.Value, Density: (UiDensity)uiDensity.SelectedIndex,
            Icons: (UiIconSize)uiIcons.SelectedIndex, Tint: (int)uiTint.Value, Shade: (int)uiShade.Value,
            Motion: (UiMotion)uiMotion.SelectedIndex, Duration: (int)uiDuration.Value));
    }
    void QueueUiStyle(UiStyleOptions value)
    {
        if (App.UiStyle.ReadOnly) return;
        uiPending = value;
        if (!uiPreviewQueued)
        {
            uiPreviewQueued = true;
            DispatcherQueue.TryEnqueue(() => { uiPreviewQueued = false; ApplyUiStylePreview(); });
        }
        uiStyleStatus.Text = "正在预览，稍后自动保存…";
        uiSaveTimer.Stop(); uiSaveTimer.Start();
    }
    void ApplyUiStylePreview()
    {
        if (uiPending is not { } value) return;
        uiPending = null; PanelTheme.Apply(value);
    }
    bool SaveUiStyle()
    {
        uiSaveTimer.Stop(); ApplyUiStylePreview();
        if (App.UiStyle.ReadOnly) return true;
        try
        {
            if (App.UiStyle.Data != PanelTheme.Current) App.UiStyle.Save(PanelTheme.Current);
            uiStyleStatus.Text = $"界面风格已保存 · {DateTime.Now:HH:mm:ss}"; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { uiStyleStatus.Text = "效果已预览，但保存失败。请重试，或检查数据目录权限。"; return false; }
    }
}
