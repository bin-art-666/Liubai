using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    void RefreshPanelHighlights()
    {
        if (closing) return;
        var planner = plannerWindow ?? manager?.CurrentPlanner;
        bool plannerHere = planner?.IsOpen == true && planner.DisplayDevice == Display.Device;
        SetPanelHighlight(todo, plannerHere && planner!.Page == PlannerPage.Tasks);
        SetPanelHighlight(quickNote, plannerHere && planner!.Page == PlannerPage.Notes);
        SetPanelHighlight(aiButton, App.Ai.IsOpen && App.Ai.DisplayDevice == Display.Device);
        SetPanelHighlight(shelf, stationWindow?.IsOpen == true);
        SetPanelHighlight(settings, settingsWindow?.IsOpen == true);
        SetPanelHighlight(clockButton, clockWindow?.IsOpen == true);
    }
    static void SetPanelHighlight(Button button, bool active)
    {
        bool dark = button.RequestedTheme == ElementTheme.Dark
            || button.RequestedTheme == ElementTheme.Default && button.ActualTheme == ElementTheme.Dark;
        var color = active ? dark ? Windows.UI.Color.FromArgb(32, 255, 255, 255)
            : Windows.UI.Color.FromArgb(22, 0, 0, 0) : Colors.Transparent;
        if (button.Background is not SolidColorBrush brush || brush.Color != color)
            button.Background = new SolidColorBrush(color);
        AutomationProperties.SetItemStatus(button, active ? "已展开" : "已收起");
    }
}
