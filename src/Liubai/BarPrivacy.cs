using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

public sealed partial class BarWindow
{
    bool privacyLocked;
    bool privacyWarning;
    void InitializePrivacyToolbar()
    {
        privacyButton.Content = PrivacyGlyph(false);
        privacyButton.Click += async (_, _) =>
        {
            if (ToolbarClickBlocked || App.Privacy.Busy) return;
            await App.Privacy.Toggle();
        };
        privacyButton.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            if (!ToolbarClickBlocked) { OpenSettings(); settingsWindow?.OpenPrivacy(); }
        };
        App.Privacy.Changed += RefreshPrivacyToolbar; RefreshPrivacyToolbar();
    }
    UIElement PrivacyGlyph(bool locked, bool warning = false) => CreateVectorIcon(privacyButton,
        (locked ? "M 7 10 L 7 7 C 7 0.5 17 0.5 17 7 L 17 10 " : "M 7 10 L 7 7 C 7 1.5 15 1 17 5 ")
        + "M 5 10 L 19 10 L 19 21 L 5 21 Z " + (warning ? "M 12 13 L 12 15.5 M 12 18 L 12 18.1" : "M 12 14 L 12 17"), "1.4");
    void RefreshPrivacyToolbar()
    {
        if (closing) return;
        bool locked = App.Privacy.Journal.Active;
        bool warning = App.Privacy.Broken || locked && !App.Privacy.Verified;
        if (privacyLocked != locked || privacyWarning != warning) { privacyLocked = locked; privacyWarning = warning; privacyButton.Content = PrivacyGlyph(locked, warning); }
        privacyButton.Opacity = App.Privacy.Busy ? .5 : 1;
        string text = "左键开启／恢复，右键隐私设置 · " + App.Privacy.Status;
        ToolTipService.SetToolTip(privacyButton, text); AutomationProperties.SetHelpText(privacyButton, text);
    }
}
