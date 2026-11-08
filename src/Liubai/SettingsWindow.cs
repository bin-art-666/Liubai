using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Liubai;

internal sealed partial class SettingsWindow : PanelWindow
{
    DesktopMonitor display;
    readonly AppearanceStore preferences;
    readonly Action apply;
    readonly Action resize;
    readonly Action adapt;
    readonly ToggleSwitch expandedEnabled;
    readonly ComboBox expandedMode, expandedSpeed, expandedAnimation;
    readonly TextBox expandedColor;
    readonly StackPanel expandedCustom;
    readonly Border expandedColorPreview;
    readonly TextBlock expandedError;
    readonly ComboBox preset, basis;
    readonly Slider height, tint, luminosity;
    readonly ToggleSwitch automatic, line;
    readonly TextBox colorInput;
    readonly Slider red, green, blue;
    readonly Border colorPreview;
    readonly StackPanel colorSection;
    readonly Expander colorExpander;
    readonly TextBlock colorError;
    readonly TextBlock status;
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer;
    bool syncing = true, dirty, interactive, previewQueued, heightQueued, heightMoved, expandedQueued;

    internal SettingsWindow(AppearanceStore preferences, Action apply, Action resize, Action adapt, Action quit, DesktopMonitor? display = null)
    {
        this.display = display ?? Native.Monitors().First(m => m.Primary);
        this.preferences = preferences;
        this.apply = apply;
        this.resize = resize;
        this.adapt = adapt;
        Title = "留白设置";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        int corner = 2;
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 33, ref corner, sizeof(int));
        int borderColor = unchecked((int)0xFFFFFFFE);
        Native.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 34, ref borderColor, sizeof(int));
        saveTimer = DispatcherQueue.CreateTimer();
        saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => FlushChanges();

        var root = PanelTheme.Layout();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = new TextBlock { Text = "留白", FontSize = 12, Opacity = .6, CharacterSpacing = 140, VerticalAlignment = VerticalAlignment.Center };
        var headerActions = PanelTheme.Actions();
        var headerSave = PanelTheme.Icon("\uE74E", "立即保存全部设置");
        headerSave.Click += async (_, _) => await SaveAllSettings(); headerActions.Children.Add(headerSave);
        var close = PanelTheme.Icon("\uE8BB", "收起设置");
        close.Click += (_, _) => Hide();
        headerActions.Children.Add(close);
        root.Children.Add(PanelTheme.Header(brand, headerActions));

        var stack = new StackPanel { Spacing = 16, Padding = new Thickness(0, 0, 8, 4) };
        stack.Children.Add(new TextBlock { Text = "留白设置", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(Hint("管理常用功能与顶栏外观。"));
        stack.Children.Add(BuildDailySettings());
        stack.Children.Add(BuildUpdateSettings());
        stack.Children.Add(BuildToolbarLayoutSettings());
        stack.Children.Add(BuildUiStyleSettings());
        stack.Children.Add(BuildScreenshotSettings());
        stack.Children.Add(BuildClockSettings());
        stack.Children.Add(BuildStationSettings());
        stack.Children.Add(BuildGuardSettings());
        stack.Children.Add(BuildPrivacySettings());
        stack.Children.Add(BuildAiSettings());
        stack.Children.Add(BuildHelpSettings());
        var appearance = new StackPanel { Spacing = 16 };
        height = AddSlider(appearance, "顶栏高度", "最小 1，默认 36；低于 24 时通过托盘打开设置。", AppearanceStore.MinimumHeight, AppearanceStore.MaximumHeight, " px", PreviewHeight);
        var resetHeight = new Button { Content = "恢复默认高度", HorizontalAlignment = HorizontalAlignment.Left };
        resetHeight.Click += (_, _) => height.Value = AppearanceStore.DefaultHeight;
        appearance.Children.Add(resetHeight);
        appearance.Children.Add(Separator());
        appearance.Children.Add(new TextBlock { Text = "最大化／全屏外观", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        expandedEnabled = new ToggleSwitch { Header = "启用专用外观", OnContent = "仅最大化或全屏时启用", OffContent = "使用日常外观，全屏时隐藏" };
        AutomationProperties.SetName(expandedEnabled, "启用最大化和全屏专用外观");
        appearance.Children.Add(expandedEnabled);
        expandedMode = Combo("外观模式", "自动黑白", "自定义底色", "跟随窗口颜色");
        appearance.Children.Add(expandedMode);
        expandedCustom = new StackPanel { Spacing = 8 };
        expandedColorPreview = new Border { Height = 24, CornerRadius = new CornerRadius(6) };
        expandedCustom.Children.Add(expandedColorPreview);
        var expandedSwatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string hex in new[] { "#000000", "#FFFFFF", "#202630", "#A7DCCB", "#7CAEDD", "#B8A4D5" })
        {
            uint rgb = Convert.ToUInt32(hex[1..], 16);
            var swatch = new Button { Width = 32, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) };
            AutomationProperties.SetName(swatch, "全屏底色 " + hex);
            swatch.Click += (_, _) => SelectExpandedColor(hex);
            expandedSwatches.Children.Add(swatch);
        }
        expandedCustom.Children.Add(expandedSwatches);
        expandedColor = new TextBox { Header = "自定义底色（HEX）", PlaceholderText = "#202020", MaxLength = 7 };
        AutomationProperties.SetName(expandedColor, "最大化和全屏自定义底色 HEX");
        expandedCustom.Children.Add(expandedColor);
        expandedError = Hint("请输入 # 加 6 位十六进制色值");
        expandedError.Visibility = Visibility.Collapsed;
        expandedCustom.Children.Add(expandedError);
        appearance.Children.Add(expandedCustom);
        appearance.Children.Add(Hint("自动黑白识别窗口明暗；跟随窗口颜色延伸顶部底色。图标自动选黑白，窗口还原后恢复日常玻璃。"));
        appearance.Children.Add(Hint("普通窗口和分屏使用日常玻璃。跟随取色需横向覆盖足够；局部区域或遮挡不会决定整条顶栏的颜色。"));
        expandedSpeed = Combo("切换速度", "灵敏", "均衡（推荐）", "稳重");
        expandedAnimation = Combo("过渡动画", "标准", "柔和", "关闭");
        appearance.Children.Add(expandedSpeed);
        appearance.Children.Add(expandedAnimation);
        appearance.Children.Add(Hint("灵敏更快跟随窗口；稳重减少颜色波动。系统关闭动画时立即切换。"));
        appearance.Children.Add(Separator());
        appearance.Children.Add(new TextBlock { Text = "日常玻璃外观", FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        preset = Combo("外观预设", "轻透玻璃", "柔和磨砂", "浓厚亚克力", "自定义");
        appearance.Children.Add(preset);
        appearance.Children.Add(Separator());
        basis = Combo("材质基底", "轻薄玻璃", "厚实亚克力", "无基底");
        appearance.Children.Add(basis);
        appearance.Children.Add(Hint("无基底不模糊背景，仅保留染色与明暗遮罩；两项均为 0 时完全透明。"));
        tint = AddSlider(appearance, "染色不透明度", "越低越通透，越高颜色越浓。");
        luminosity = AddSlider(appearance, "明暗遮罩", "调节背景的明暗层次与遮蔽感。");
        automatic = new ToggleSwitch { Header = "染色跟随系统明暗", OnContent = "自动配色", OffContent = "自定义颜色" };
        AutomationProperties.SetName(automatic, "染色跟随系统明暗");
        appearance.Children.Add(automatic);
        colorSection = new StackPanel { Spacing = 8 };
        colorSection.Children.Add(Hint("关闭自动配色后，可选颜色、调 RGB 或输入 HEX。"));
        colorPreview = new Border { Height = 28, CornerRadius = new CornerRadius(6) };
        colorSection.Children.Add(colorPreview);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (name, hex) in new[] { ("云白", "#F6FBF9"), ("墨黑", "#141D1C"), ("薄荷", "#A7DCCB"), ("晴蓝", "#7CAEDD"), ("雾紫", "#B8A4D5"), ("暖杏", "#E9C69D") })
        {
            uint rgb = Convert.ToUInt32(hex[1..], 16);
            var swatch = new Button { Width = 32, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) };
            AutomationProperties.SetName(swatch, name + " " + hex);
            ToolTipService.SetToolTip(swatch, name);
            swatch.Click += (_, _) => SelectColor(hex);
            swatches.Children.Add(swatch);
        }
        colorSection.Children.Add(swatches);
        colorInput = new TextBox { Header = "HEX 色值", PlaceholderText = "#D5E9E1", MaxLength = 7 };
        AutomationProperties.SetName(colorInput, "HEX 色值");
        colorSection.Children.Add(colorInput);
        colorError = Hint(""); colorError.Visibility = Visibility.Collapsed; colorSection.Children.Add(colorError);
        red = AddColorSlider(colorSection, "红 R");
        green = AddColorSlider(colorSection, "绿 G");
        blue = AddColorSlider(colorSection, "蓝 B");
        colorExpander = new Expander { Header = "自定义染色颜色", Content = colorSection, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        appearance.Children.Add(colorExpander);
        line = new ToggleSwitch { Header = "显示顶栏底部分隔线", OnContent = "显示", OffContent = "隐藏" };
        AutomationProperties.SetName(line, "显示顶栏底部分隔线");
        appearance.Children.Add(line);
        appearance.Children.Add(Hint("拖动时顶栏即时预览，停止调节后自动保存。模糊由 Windows 材质控制。"));
        var reset = new Button { Content = "重置自定义", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { preferences.Preview(new MaterialOptions()); RefreshStyle(preferences.Style); Preview(); };
        appearance.Children.Add(reset);
        appearance.Children.Add(BuildPersonalPresets());
        stack.Children.Add(new Expander { Header = "顶栏外观设置", Content = appearance,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var footer = new StackPanel { Spacing = 12 };
        footer.Children.Add(Separator());
        status = Hint("调整后自动保存，下次启动继续使用");
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        footer.Children.Add(status);
        var save = new Button { Content = "确定保存", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(save, "确定保存全部设置");
        save.Click += async (_, _) => await SaveAllSettings();
        footer.Children.Add(save);
        var exit = new Button { Content = "退出留白", HorizontalAlignment = HorizontalAlignment.Stretch };
        exit.Click += async (_, _) => { if (SaveGuardOptions() && SaveAiSettings(AiCustomSelected) && await SaveStationSettings()) { FlushChanges(); quit(); } };
        footer.Children.Add(exit);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var surface = new Border { Child = root, CornerRadius = PanelTheme.Value<CornerRadius>("PanelRadius"), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
        PanelTheme.Track(surface, static border => border.CornerRadius = PanelTheme.Value<CornerRadius>("PanelRadius"));
        InitializePanel(surface);
        SystemBackdrop = PanelTheme.Backdrop();

        preset.SelectionChanged += (_, _) =>
        {
            if (syncing || !interactive || preset.SelectedIndex < 0) return;
            saveTimer.Stop();
            try
            {
                preferences.Save((GlassStyle)preset.SelectedIndex);
                dirty = false;
                apply(); RefreshStyle(preferences.Style);
                status.Text = "已保存，下次启动继续使用";
            }
            catch { RefreshStyle(preferences.Style); ShowSaveError(); }
        };
        basis.SelectionChanged += (_, _) => Preview();
        automatic.Toggled += (_, _) => { colorExpander.IsEnabled = !automatic.IsOn; Preview(); };
        colorInput.TextChanged += (_, _) =>
        {
            if (syncing) return;
            bool valid = MaterialOptions.IsColor(colorInput.Text);
            colorError.Text = "请输入 # 加 6 位十六进制色值，例如 #7CAEDD";
            colorError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            if (!valid) return;
            syncing = true;
            uint rgb = Convert.ToUInt32(colorInput.Text[1..], 16);
            red.Value = (byte)(rgb >> 16); green.Value = (byte)(rgb >> 8); blue.Value = (byte)rgb;
            colorPreview.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            syncing = false;
            Preview();
        };
        line.Toggled += (_, _) => Preview();
        expandedEnabled.Toggled += (_, _) => { UpdateExpandedControls(); PreviewExpanded(); };
        expandedMode.SelectionChanged += (_, _) => { UpdateExpandedControls(); PreviewExpanded(); };
        expandedColor.TextChanged += (_, _) => PreviewExpanded();
        expandedSpeed.SelectionChanged += (_, _) => PreviewExpanded();
        expandedAnimation.SelectionChanged += (_, _) => PreviewExpanded();
        RefreshStyle(preferences.Style);
        surface.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => interactive = true);
    }

    static TextBlock Hint(string text) => PanelTheme.Hint(text);
    static Border Separator() => new() { Height = 1, Background = new SolidColorBrush(Color.FromArgb(25, 128, 128, 128)) };
    static ComboBox Combo(string label, params string[] items)
    {
        var combo = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string item in items) combo.Items.Add(item);
        AutomationProperties.SetName(combo, label);
        return combo;
    }

    Slider AddSlider(StackPanel stack, string label, string hint, int minimum = 0, int maximum = 100, string unit = "%", Action? changed = null)
    {
        var section = new StackPanel { Spacing = 4 };
        var heading = new Grid();
        heading.Children.Add(new TextBlock { Text = label, FontSize = 14 });
        var value = new TextBlock { Text = $"{minimum}{unit}", Margin = new Thickness(0, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 14, Opacity = .7 };
        heading.Children.Add(value); section.Children.Add(heading);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = 1, SmallChange = 1, LargeChange = 10 };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, e) => { value.Text = $"{e.NewValue:0}{unit}"; if (changed != null) changed(); else Preview(); };
        section.Children.Add(slider); section.Children.Add(Hint(hint)); stack.Children.Add(section);
        return slider;
    }

    void SelectColor(string hex) => colorInput.Text = hex;
    void SelectExpandedColor(string hex) => expandedColor.Text = hex;

    Slider AddColorSlider(StackPanel stack, string label)
    {
        var slider = new Slider { Header = label, Minimum = 0, Maximum = 255, StepFrequency = 1, SmallChange = 1, LargeChange = 16 };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            colorInput.Text = $"#{(int)red.Value:X2}{(int)green.Value:X2}{(int)blue.Value:X2}";
        };
        stack.Children.Add(slider);
        return slider;
    }

    internal void RefreshStyle(GlassStyle style)
    {
        syncing = true;
        try
        {
            var material = preferences.Material;
            RefreshDailySettings();
            clockFontSize.Value = preferences.ClockFontSize;
            height.Value = preferences.BarHeight;
            expandedEnabled.IsOn = preferences.Expanded.Enabled;
            expandedMode.SelectedIndex = (int)preferences.Expanded.Mode;
            expandedSpeed.SelectedIndex = (int)preferences.Expanded.Speed;
            expandedAnimation.SelectedIndex = (int)preferences.Expanded.Animation;
            expandedColor.Text = preferences.Expanded.ColorHex;
            expandedError.Visibility = Visibility.Collapsed;
            UpdateExpandedControls();
            preset.SelectedIndex = (int)style;
            basis.SelectedIndex = material.NoBase ? 2 : material.Thin ? 0 : 1;
            tint.Value = Math.Round(material.TintOpacity * 100);
            luminosity.Value = Math.Round(material.LuminosityOpacity * 100);
            automatic.IsOn = material.FollowTheme;
            uint rgb = Convert.ToUInt32(material.TintHex[1..], 16);
            red.Value = (byte)(rgb >> 16); green.Value = (byte)(rgb >> 8); blue.Value = (byte)rgb;
            colorInput.Text = material.TintHex;
            colorPreview.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            colorExpander.IsEnabled = !material.FollowTheme;
            colorError.Visibility = Visibility.Collapsed;
            line.IsOn = material.ShowLine;
        }
        finally { syncing = false; }
    }

    void Preview()
    {
        if (syncing || !interactive || previewQueued) return;
        previewQueued = true;
        DispatcherQueue.TryEnqueue(() => { if (!previewQueued) return; previewQueued = false; ApplyPreview(); });
    }

    void PreviewHeight()
    {
        if (syncing || !interactive || heightQueued) return;
        heightQueued = true;
        DispatcherQueue.TryEnqueue(() => { if (!heightQueued) return; heightQueued = false; ApplyHeightPreview(); });
    }

    void UpdateExpandedControls()
    {
        expandedMode.IsEnabled = expandedEnabled.IsOn;
        expandedSpeed.IsEnabled = expandedAnimation.IsEnabled = expandedEnabled.IsOn;
        expandedCustom.Visibility = expandedMode.SelectedIndex == (int)ExpandedMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        expandedColor.IsEnabled = expandedEnabled.IsOn;
        foreach (var row in expandedCustom.Children.OfType<StackPanel>())
            foreach (var control in row.Children.OfType<Control>()) control.IsEnabled = expandedEnabled.IsOn;
        string hex = MaterialOptions.IsColor(expandedColor.Text) ? expandedColor.Text : preferences.Expanded.ColorHex;
        uint rgb = Convert.ToUInt32(hex[1..], 16);
        expandedColorPreview.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    void PreviewExpanded()
    {
        if (syncing || !interactive || expandedQueued) return;
        expandedQueued = true;
        DispatcherQueue.TryEnqueue(() => { if (!expandedQueued) return; expandedQueued = false; ApplyExpandedPreview(); });
    }

    void ApplyExpandedPreview()
    {
        bool valid = MaterialOptions.IsColor(expandedColor.Text);
        expandedError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (!valid && expandedEnabled.IsOn && expandedMode.SelectedIndex == (int)ExpandedMode.Custom)
        { status.Text = "全屏底色格式有误，请输入完整的 HEX 色值"; return; }
        preferences.SetExpandedAppearance(new ExpandedAppearanceOptions(expandedEnabled.IsOn,
            (ExpandedMode)Math.Max(0, expandedMode.SelectedIndex), valid ? expandedColor.Text.ToUpperInvariant() : preferences.Expanded.ColorHex,
            (AppearanceSpeed)Math.Max(0, expandedSpeed.SelectedIndex), (AppearanceAnimation)Math.Max(0, expandedAnimation.SelectedIndex)));
        UpdateExpandedControls();
        adapt(); dirty = true;
        status.Text = "正在预览，稍后自动保存…";
        saveTimer.Stop(); saveTimer.Start();
    }

    void ApplyHeightPreview()
    {
        preferences.SetHeight((int)Math.Round(height.Value));
        resize();
        heightMoved = true;
        dirty = true;
        status.Text = "正在预览，稍后自动保存…";
        saveTimer.Stop(); saveTimer.Start();
    }

    void ApplyPreview()
    {
        string hex = MaterialOptions.IsColor(colorInput.Text) ? colorInput.Text.ToUpperInvariant() : preferences.Material.TintHex;
        preferences.Preview(new MaterialOptions(basis.SelectedIndex == 0, (float)tint.Value / 100,
            (float)luminosity.Value / 100, automatic.IsOn, hex, line.IsOn, basis.SelectedIndex == 2));
        syncing = true;
        preset.SelectedIndex = (int)GlassStyle.Custom;
        syncing = false;
        apply();
        dirty = true;
        status.Text = "正在预览，稍后自动保存…";
        saveTimer.Stop(); saveTimer.Start();
    }

    internal bool FlushChanges(bool force = false)
    {
        if (!SaveToolbarLayout()) { status.Text = "顶栏布局保存失败，请重试"; return false; }
        if (!SaveUiStyle()) { status.Text = "界面风格保存失败，请重试"; return false; }
        if (expandedQueued) { expandedQueued = false; ApplyExpandedPreview(); }
        if (heightQueued) { heightQueued = false; ApplyHeightPreview(); }
        if (previewQueued) { previewQueued = false; ApplyPreview(); }
        saveTimer.Stop();
        if (force && expandedEnabled.IsOn && expandedMode.SelectedIndex == (int)ExpandedMode.Custom && !MaterialOptions.IsColor(expandedColor.Text))
        {
            status.Text = "全屏底色格式有误，请修正后保存";
            return false;
        }
        if (force && !automatic.IsOn && !MaterialOptions.IsColor(colorInput.Text))
        {
            status.Text = "颜色格式有误，请修正后保存";
            return false;
        }
        if (force) dirty = true;
        if (!dirty) return true;
        try
        {
            preferences.SaveCurrent(); dirty = false;
            status.Text = force ? $"全部设置已保存 · {DateTime.Now:HH:mm:ss}" : "已保存，下次启动继续使用";
            // Keep the slider under the pointer during dragging; re-anchor after the pause.
            if (heightMoved) { heightMoved = false; Reposition(display.Info.Work.Top); }
            return true;
        }
        catch { ShowSaveError(); return false; }
    }

    internal async Task<bool> SaveAllSettings()
    { return SaveGuardOptions() && SaveAiSettings(AiCustomSelected) && await SaveStationSettings() && FlushChanges(force: true); }

    internal void ShowSaveError() => status.Text = "效果已预览，但保存失败；请检查数据目录权限";

    internal void Reposition(int barBottom)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = display.Dpi / 96d;
        var monitor = display.Info;
        int margin = (int)Math.Round(12 * scale);
        int top = Math.Min(Math.Max(monitor.Work.Top, barBottom) + (int)Math.Round(8 * scale), monitor.Work.Bottom - margin - 180);
        int width = Math.Min((int)Math.Round(380 * scale), monitor.Work.Right - monitor.Work.Left - margin * 2);
        int height = Math.Max(120, Math.Min((int)Math.Round(700 * scale), monitor.Work.Bottom - top - margin));
        AppWindow.MoveAndResize(new RectInt32(monitor.Work.Right - width - margin, top, width, height));
    }

    internal void Open(int barBottom)
    {
        Reposition(barBottom);
        ShowPanel();
    }

    protected override void OnHiding() { SaveGuardOptions(); SaveAiSettings(AiCustomSelected); _ = SaveStationSettings(); FlushChanges(); }
    internal void UpdateDisplay(DesktopMonitor monitor)
    { display = monitor; if (IsOpen) Reposition(monitor.Info.Work.Top); }
}
