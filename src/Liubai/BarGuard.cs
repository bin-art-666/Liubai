using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Shapes;

namespace Liubai;

public sealed partial class BarWindow
{
    Ellipse guardDot = null!;
    Border guardGlyphHost = null!;
    UIElement guardNormalGlyph = null!, guardPausedGlyph = null!, guardWarningGlyph = null!;
    void InitializeGuardToolbar()
    {
        const string shield = "M 12 2 C 9 4 6 4 4 4 L 4 11 C 4 16 7 19 12 22 C 17 19 20 16 20 11 L 20 4 C 18 4 15 4 12 2 Z ";
        guardNormalGlyph = CreateVectorIcon(guardButton, shield + "M 8 11 L 11 14 L 16 9", "1.4");
        guardPausedGlyph = CreateVectorIcon(guardButton, shield + "M 9.5 8.5 L 9.5 14.5 M 14.5 8.5 L 14.5 14.5", "1.4");
        guardWarningGlyph = CreateVectorIcon(guardButton, shield + "M 12 8 L 12 12 M 12 15 L 12 15.1", "1.4");
        guardGlyphHost = new Border { Width = 20, Height = 20, Child = guardNormalGlyph };
        var content = new Grid { Width = 20, Height = 20, IsHitTestVisible = false }; content.Children.Add(guardGlyphHost);
        guardDot = new Ellipse { Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -1, -1), Visibility = Visibility.Collapsed };
        guardDot.SetBinding(Shape.FillProperty, new Binding { Source = guardButton, Path = new PropertyPath("Foreground") }); content.Children.Add(guardDot);
        guardButton.Content = content;
        guardButton.ContextFlyout = CreateGuardMenu();
        App.GuardChanged += RefreshGuardToolbar; RefreshGuardToolbar();
    }
    MenuFlyout CreateGuardMenu()
    {
        var menu = new MenuFlyout();
        var pause = new MenuFlyoutItem { Text = "暂停 10 分钟", Icon = new SymbolIcon(Symbol.Pause) };
        var resume = new MenuFlyoutItem { Text = "立即恢复", Icon = new SymbolIcon(Symbol.Play) };
        var toggle = new MenuFlyoutItem(); bool enableFromMenu = false;
        menu.Items.Add(pause); menu.Items.Add(resume); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(toggle);
        menu.Opening += (_, _) =>
        {
            pause.IsEnabled = (App.CurrentGuard?.Wanted == true); resume.IsEnabled = (App.CurrentGuard?.Wanted == true) && (App.CurrentGuard?.ManuallyPaused == true);
            enableFromMenu = !(App.CurrentGuard?.Wanted == true); toggle.Text = enableFromMenu ? "开启守护" : "关闭守护";
        };
        pause.Click += async (_, _) => { if (!ToolbarClickBlocked) await App.Guard.PauseFor(TimeSpan.FromMinutes(10)); };
        resume.Click += async (_, _) => { if (!ToolbarClickBlocked) await App.Guard.Resume(); };
        toggle.Click += async (_, _) => { if (!ToolbarClickBlocked) await App.Guard.SetEnabled(enableFromMenu); };
        return menu;
    }
    void RefreshGuardToolbar()
    {
        if (closing) return;
        guardDot.Visibility = (App.CurrentGuard?.State ?? GuardState.Off) == GuardState.Running ? Visibility.Visible : Visibility.Collapsed;
        SetGuardGlyph((App.CurrentGuard?.ManuallyPaused == true), (App.CurrentGuard?.Wanted == true) && (App.CurrentGuard?.State ?? GuardState.Off) is GuardState.Unavailable or GuardState.Paused);
        string status = "屏幕守护 · " + App.GuardStatus;
        ToolTipService.SetToolTip(guardButton, status); AutomationProperties.SetHelpText(guardButton, status);
        if (reservation is not null) UpdateVisibility();
    }
    void SetGuardGlyph(bool paused, bool warning)
    {
        var glyph = paused ? guardPausedGlyph : warning ? guardWarningGlyph : guardNormalGlyph;
        if (!ReferenceEquals(guardGlyphHost.Child, glyph)) guardGlyphHost.Child = glyph;
    }
}
