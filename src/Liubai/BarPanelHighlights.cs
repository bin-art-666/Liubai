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
        if (tools != null)
            foreach (var definition in ToolCatalog.BuiltIn.Items.Where(tool => tool.Kind == ToolKind.Panel))
                SetPanelHighlight(toolbarButtons[definition.Id], tools.Active(definition.Id));
        SetPanelHighlight(focusEntry, focusWindow?.IsOpen == true);
        if (moreButton != null) SetPanelHighlight(moreButton, moreWindow?.IsOpen == true);
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
