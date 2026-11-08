using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Liubai;

internal sealed partial class AiWindow
{
    async Task<string[]> VerifyProviderStrip()
    {
        var page=current!;var core=page.View.CoreWebView2;string id=options.Data.Provider;int count=pages.Count;
        providerHost.MaxWidth=180;root.UpdateLayout();await Task.Delay(150);
        if(providerViewport.ScrollableWidth<=0||providerLabels.Children.Count!=providerChoices.Count)throw new Exception("AI service strip does not overflow correctly");
        ScrollProviders(10000);double offset=providerViewport.HorizontalOffset;await Task.Delay(60);
        if(offset<=0||options.Data.Provider!=id||current!=page||pages.Count!=count||page.View.CoreWebView2!=core)throw new Exception("Browsing labels changed or initialized a service");
        RefreshProviderOverflow();var menu=(MenuFlyout)providerMore.Flyout;
        if(!menu.Items.OfType<MenuFlyoutItem>().Any(e=>e.Text==providerChoices.Single(c=>c.Id==id).Name))throw new Exception("Overflow omits clipped service");
        menu.ShowAt(providerMore);await Task.Delay(100);
        var entry=menu.Items.OfType<MenuFlyoutItem>().Single(e=>e.Text==providerChoices.Single(c=>c.Id==id).Name);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(entry).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(120);
        if(options.Data.Provider!=id||current!=page||pages.Count!=count||page.View.CoreWebView2!=core)throw new Exception("Selecting current AI recreated its page");
        var selected=providerLabels.Children.OfType<ToggleButton>().Single(b=>((Choice)b.Tag).Id==id);
        double x=selected.TransformToVisual(providerLabels).TransformPoint(new()).X;
        if(selected.IsChecked!=true||x<providerViewport.HorizontalOffset-.5||x+selected.ActualWidth>providerViewport.HorizontalOffset+providerViewport.ViewportWidth+.5)throw new Exception("Selected service was not revealed");
        menu.Hide();providerHost.MaxWidth=double.PositiveInfinity;root.UpdateLayout();await Task.Delay(100);
        return ["AI labels overflow and scroll without switching services, creating pages or reloading WebView", "Native overflow menu reveals selected label in stable order and preserves the current browser"];
    }
}
