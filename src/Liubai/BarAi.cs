using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

public sealed partial class BarWindow
{
    void InitializeAiToolbar()
    {
        aiButton.Content = CreateVectorIcon(aiButton,
            "M 13 4 L 6 4 C 3.8 4 2 5.8 2 8 L 2 14 C 2 16.2 3.8 18 6 18 L 6 22 L 11 18 L 17 18 C 19.2 18 21 16.2 21 14 L 21 12 "
            + "M 18 2 L 19.3 5.7 L 23 7 L 19.3 8.3 L 18 12 L 16.7 8.3 L 13 7 L 16.7 5.7 Z M 7 11 L 10 11", "1.4");
        aiButton.Click += async (_, _) =>
        {
            if (ToolbarClickBlocked || PanelModal || manager?.HasModal == true || manager?.Suspended == true || closing || openingPlanner) return;
            if (App.Ai.IsOpen) App.Ai.Hide(); else await OpenAiPanel();
        };
        var menu = new MenuFlyout();
        foreach (string provider in AiPolicy.BuiltInProviders)
        {
            string id = provider; var item = new MenuFlyoutItem { Text = AiPolicy.Resolve(new(Provider: id)).Name };
            item.Click += (_, _) => { if (!ToolbarClickBlocked) try { App.Ai.Select(id); } catch { OpenAiSettings(); } }; menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var pin = new ToggleMenuFlyoutItem { Text = "固定 AI 面板" };
        pin.Click += (_, _) => { try { App.Ai.Options.Save(App.Ai.Options.Data with { Pinned = pin.IsChecked }); } catch { OpenAiSettings(); } };
        menu.Items.Add(pin);
        var settingsItem = new MenuFlyoutItem { Text = "AI 快捷窗设置" }; settingsItem.Click += (_, _) => OpenAiSettings(); menu.Items.Add(settingsItem);
        menu.Opening += (_, _) => { pin.IsChecked = App.Ai.Options.Data.Pinned; foreach (var item in menu.Items) item.IsEnabled = !App.Ai.Busy; settingsItem.IsEnabled = true; };
        aiButton.ContextFlyout = menu;
        App.Ai.Changed += RefreshAiToolbar; RefreshAiToolbar();
    }
    void OpenAiSettings() { if (!ToolbarClickBlocked) { OpenSettings(); settingsWindow?.OpenAi(); } }
    async Task OpenAiPanel()
    {
        if (closing || openingPlanner || PanelModal || manager?.HasModal == true || manager?.Suspended == true || !Native.MonitorExists(Display.Handle)) return;
        HideUtilityPanels();
        clockWindow?.Hide(); settingsWindow?.Hide(); plannerWindow?.Hide(); qrResults?.Hide(); stationWindow?.HideDocked(); manager?.HideOtherPanels(this);
        await App.Ai.Open(Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    void RefreshAiToolbar()
    {
        if (closing) return;
        string name = AiPolicy.Resolve(App.Ai.Options.Data).Name;
        aiButton.Opacity = App.Ai.Busy ? .5 : 1;
        string text = "AI · " + name + "\n" + App.Ai.Status + "\n右键切换服务或设置";
        ToolTipService.SetToolTip(aiButton, text); AutomationProperties.SetHelpText(aiButton, text);
    }
}
