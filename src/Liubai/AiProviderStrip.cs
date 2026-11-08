using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Liubai;

internal sealed partial class AiWindow
{
    readonly Grid providerHost=new(){ColumnSpacing=6};
    readonly GroupStripViewport providerViewport=new();
    readonly StackPanel providerLabels=new(){Orientation=Orientation.Horizontal,Spacing=6};
    readonly Button providerMore=StationWindow.IconButton("\uE712","更多 AI 服务");
    int providerScrollVersion,providerRenderVersion;
    protected override bool UsesPanelWheel=>true;
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Native.Point point);
    void InitializeProviderStrip()
    {
        providerHost.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});providerHost.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        providerViewport.Content=providerLabels;providerHost.Children.Add(providerViewport);Grid.SetColumn(providerMore,1);providerHost.Children.Add(providerMore);
        AutomationProperties.SetName(providerHost,"AI 面板服务");
        var menu=new MenuFlyout();providerMore.Flyout=menu;menu.Opening+=(_,_)=>RefreshProviderOverflow();
        providerViewport.SizeChanged+=(_,_)=>QueueProviderReveal();
        providerHost.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,e)=>{
            var p=e.GetCurrentPoint(providerViewport);if(p.Position.X<0||p.Position.Y<0||p.Position.X>=providerViewport.ActualWidth||p.Position.Y>=providerViewport.ActualHeight)return;
            ScrollProviders(p.Properties.MouseWheelDelta*(p.Properties.IsHorizontalMouseWheel?1:-1)*.8);e.Handled=true;
        }),true);
        PanelTheme.Changed+=ProviderStyleChanged;
        Closed+=(_,_)=>{providerRenderVersion++;PanelTheme.Changed-=ProviderStyleChanged;};
    }
    void ProviderStyleChanged()=>RenderProviderStrip();
    bool ProviderEnabled=>!options.ReadOnly&&popup==null;
    void RefreshProviderEnabled(){providerMore.IsEnabled=ProviderEnabled;foreach(var button in providerLabels.Children.OfType<ToggleButton>())button.IsEnabled=ProviderEnabled;UpdateAiHeaderLayout();}
    void RenderProviderStrip()
    {
        if(destroyed)return;
        providerLabels.Children.Clear();
        foreach(var choice in providerChoices)
        {
            var label=new TextBlock{Text=choice.Name,FontSize=14,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis};
            label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            var button=new ToggleButton{Content=label,Padding=new Thickness(10,0,10,0),Tag=choice,IsChecked=choice.Id==options.Data.Provider};
            PanelTheme.FramedIconStyle(button,choice.Name);button.Width=Math.Clamp(Math.Ceiling(label.DesiredSize.Width)+24,56,150);
            AutomationProperties.SetItemStatus(button,button.IsChecked==true?"当前 AI 服务":"切换 AI 服务");
            button.Click+=(_,_)=>SelectProvider(choice.Id);
            button.GotFocus+=(_,_)=>RevealProvider(button);
            providerLabels.Children.Add(button);
        }
        QueueProviderReveal();
        RefreshProviderEnabled();
    }
    void SelectProvider(string id)
    {
        if(options.ReadOnly||popup!=null||destroyed)return;
        try {if(options.Data.Provider!=id)options.Save(options.Data with{Provider=id});}
        catch {SetStatus("AI 服务保存失败，请重试。");}
        // Restore toggle state even for the already-selected label or a failed save.
        foreach(var button in providerLabels.Children.OfType<ToggleButton>())button.IsChecked=((Choice)button.Tag).Id==options.Data.Provider;
        QueueProviderReveal();
    }
    void QueueProviderReveal()
    {
        int render=++providerRenderVersion,scroll=providerScrollVersion;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{
            if(destroyed||render!=providerRenderVersion||scroll!=providerScrollVersion)return;
            providerHost.UpdateLayout();
            if(providerLabels.Children.OfType<ToggleButton>().FirstOrDefault(b=>((Choice)b.Tag).Id==options.Data.Provider) is {} selected)RevealProvider(selected);
            RefreshProviderOverflow();
        });
    }
    void RevealProvider(FrameworkElement label)
    {
        double x=label.TransformToVisual(providerLabels).TransformPoint(new()).X;
        if(x<providerViewport.HorizontalOffset)providerViewport.ChangeView(x,null,null,true);
        else if(x+label.ActualWidth>providerViewport.HorizontalOffset+providerViewport.ViewportWidth)providerViewport.ChangeView(x+label.ActualWidth-providerViewport.ViewportWidth,null,null,true);
    }
    void ScrollProviders(double delta)
    {if(destroyed)return;providerScrollVersion++;providerViewport.ChangeView(providerViewport.HorizontalOffset+delta,null,null,true);}
    void RefreshProviderOverflow()
    {
        if(providerMore.Flyout is not MenuFlyout menu)return;menu.Items.Clear();
        foreach(var button in providerLabels.Children.OfType<ToggleButton>())
        {
            double x=button.TransformToVisual(providerLabels).TransformPoint(new()).X;
            if(button.ActualWidth>0&&x>=providerViewport.HorizontalOffset-.5&&x+button.ActualWidth<=providerViewport.HorizontalOffset+providerViewport.ViewportWidth+.5)continue;
            var choice=(Choice)button.Tag;var entry=new MenuFlyoutItem{Text=choice.Name,IsEnabled=ProviderEnabled};
            entry.Click+=(_,_)=>SelectProvider(choice.Id);menu.Items.Add(entry);
        }
        if(menu.Items.Count==0)menu.Items.Add(new MenuFlyoutItem{Text="全部 AI 服务已显示",IsEnabled=false});
    }
    protected override bool HandlePanelWheel(Native.Point point,int delta,bool horizontal)
    {
        if(!IsOpen||popup!=null||providerViewport.ActualWidth<=0||Content is not FrameworkElement shell||shell.XamlRoot==null)return false;
        var position=providerViewport.TransformToVisual(shell).TransformPoint(new());var origin=new Native.Point();if(!ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this),ref origin))return false;
        double scale=shell.XamlRoot.RasterizationScale,x=point.X-origin.X-position.X*scale,y=point.Y-origin.Y-position.Y*scale;
        if(x<0||y<0||x>=providerViewport.ActualWidth*scale||y>=providerViewport.ActualHeight*scale)return false;
        DispatcherQueue.TryEnqueue(()=>{if(IsOpen&&!destroyed&&popup==null)ScrollProviders(delta*(horizontal?1:-1)*.8);});return true;
    }
}
