using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    Expander aiSection = null!;
    ComboBox aiProvider = null!;
    TextBox aiName = null!, aiUrl = null!;
    StackPanel aiCustom = null!;
    ToggleSwitch aiPin = null!;
    TextBlock aiStatus = null!;
    bool aiSync;
    string aiObservedProvider = "";
    bool AiCustomSelected => aiProvider.SelectedIndex >= 0 && AiPolicy.Providers[aiProvider.SelectedIndex] == "custom";
    internal void OpenAi() { aiSection.IsExpanded = true; aiSection.StartBringIntoView(); }
    Expander BuildAiSettings()
    {
        var panel = new StackPanel { Spacing = 12 }; var options = App.AiOptions.Data;
        aiObservedProvider = options.Provider;
        aiProvider = Combo("默认 AI 服务", AiPolicy.Names); aiProvider.SelectedIndex = Array.IndexOf(AiPolicy.Providers, options.Provider);
        AutomationProperties.SetName(aiProvider, "默认 AI 服务"); panel.Children.Add(aiProvider);
        aiCustom = new StackPanel { Spacing = 10, Visibility = options.Provider == "custom" ? Visibility.Visible : Visibility.Collapsed };
        aiName = new TextBox { Header = "显示名称", Text = options.CustomName, MaxLength = 40 };
        aiUrl = new TextBox { Header = "AI 网页地址（HTTPS）", Text = options.CustomUrl, MaxLength = 2048, PlaceholderText = "https://…" };
        AutomationProperties.SetName(aiName, "AI 显示名称"); AutomationProperties.SetName(aiUrl, "AI 网页地址");
        aiCustom.Children.Add(aiName); aiCustom.Children.Add(aiUrl);
        var saveCustom = new Button { Content = "保存自定义 AI" }; saveCustom.Click += (_, _) => SaveAiSettings(true); aiCustom.Children.Add(saveCustom); panel.Children.Add(aiCustom);
        aiPin = new ToggleSwitch { Header = "固定 AI 面板", IsOn = options.Pinned, OnContent = "保持展开，独立窗口置顶", OffContent = "点击外部自动收起" }; panel.Children.Add(aiPin);
        var open = new Button { Content = "打开 AI 快捷窗" }; open.Click += async (_, _) => { if (SaveAiSettings(AiCustomSelected)) { Hide(); await App.Ai.Open(display); } }; panel.Children.Add(open);
        aiStatus = Hint(App.AiOptions.Warning ?? App.AiStatus); panel.Children.Add(aiStatus);
        panel.Children.Add(Hint("点击顶部 AI 图标先展开玻璃面板，交互与中转站一致。左边缘调宽度，上下边缘调高度，松开自动保存，Esc 取消本次拖动。右上角依次为展开、固定、关闭；独立窗口尺寸单独保存，调整不重载对话。"));
        panel.Children.Add(Hint("使用 Edge WebView2 内核直接显示 AI 官网，首次需要登录，登录保存在集成面板专用配置中，与普通 Edge 及旧版独立网页窗口分开。各服务页面在本次运行中保留，不随开机预加载。退出留白会关闭面板，未发送草稿能否恢复由网站决定。"));
        panel.Children.Add(Hint("对话、文件上传与登录由对应 AI 网站处理。留白不读取对话，不自动发送剪贴板或屏幕内容，不需要 API Key。未发送草稿能否恢复由网站决定。"));
        aiSection = new Expander { Header = "AI 快捷窗", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        aiProvider.SelectionChanged += (_, _) =>
        {
            if (aiSync || syncing || !interactive) return;
            aiCustom.Visibility = AiCustomSelected ? Visibility.Visible : Visibility.Collapsed;
            if (!AiCustomSelected || aiUrl.Text.Length > 0) SaveAiSettings(AiCustomSelected);
        };
        aiPin.Toggled += (_, _) => { if (!aiSync && !syncing && interactive) SaveAiSettings(false); };
        App.AiChanged += RefreshAiSettings; Closed += (_, _) => App.AiChanged -= RefreshAiSettings;
        if (App.AiOptions.ReadOnly) { aiProvider.IsEnabled = aiPin.IsEnabled = saveCustom.IsEnabled = false; aiName.IsReadOnly = aiUrl.IsReadOnly = true; }
        return aiSection;
    }
    bool SaveAiSettings(bool custom)
    {
        if (App.AiOptions.ReadOnly) return true;
        try
        {
            var data = App.AiOptions.Data;
            if (custom) data = data with { Provider = "custom", CustomName = aiName.Text, CustomUrl = AiPolicy.Https(aiUrl.Text) };
            else if (aiProvider.SelectedIndex >= 0 && !AiCustomSelected) data = data with { Provider = AiPolicy.Providers[aiProvider.SelectedIndex] };
            App.AiOptions.Save(data with { Pinned = aiPin.IsOn }); aiStatus.Text = "AI 设置已保存。"; return true;
        }
        catch (Exception ex) { aiStatus.Text = ex is InvalidDataException ? ex.Message : "AI 设置保存失败，请检查后重试。"; return false; }
    }
    void RefreshAiSettings()
    {
        if (aiStatus is null) return;
        aiSync = true;
        try
        {
            // Keep an unfinished custom URL visible until the user saves or chooses another provider.
            if (aiObservedProvider != App.AiOptions.Data.Provider)
            {
                aiObservedProvider = App.AiOptions.Data.Provider;
                aiProvider.SelectedIndex = Array.IndexOf(AiPolicy.Providers, aiObservedProvider);
                aiCustom.Visibility = AiCustomSelected ? Visibility.Visible : Visibility.Collapsed;
            }
            aiPin.IsOn = App.AiOptions.Data.Pinned; aiStatus.Text = App.AiStatus;
        }
        finally { aiSync = false; }
    }
}
