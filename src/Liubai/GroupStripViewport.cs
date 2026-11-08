using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Liubai;

// A clipped horizontal strip deliberately owns wheel input rather than letting
// ScrollViewer's native vertical-wheel handling consume it before XAML routing.
internal sealed class GroupStripViewport : Grid
{
    readonly TranslateTransform translation=new();
    readonly RectangleGeometry clip=new();
    FrameworkElement? content;
    internal FrameworkElement? Content
    {
        get=>content;
        set {Children.Clear();content=value;if(value!=null){value.RenderTransform=translation;Children.Add(value);}InvalidateMeasure();}
    }
    internal double HorizontalOffset {get;private set;}
    internal double ViewportWidth=>ActualWidth;
    internal double ScrollableWidth=>Math.Max(0,(content?.DesiredSize.Width??0)-ActualWidth);
    internal GroupStripViewport()
    {
        Background=new SolidColorBrush(Colors.Transparent);Clip=clip;
        ManipulationMode=ManipulationModes.TranslateX|ManipulationModes.TranslateRailsX|ManipulationModes.TranslateInertia;
        ManipulationDelta+=(_,e)=>{ChangeView(HorizontalOffset-e.Delta.Translation.X,null,null,true);e.Handled=true;};
    }
    internal bool ChangeView(double? horizontal,double? vertical,float? zoom,bool disableAnimation)
    {
        HorizontalOffset=Math.Clamp(horizontal??HorizontalOffset,0,ScrollableWidth);translation.X=-HorizontalOffset;return true;
    }
    protected override Size MeasureOverride(Size available)
    {
        content?.Measure(new(double.PositiveInfinity,available.Height));
        var desired=content?.DesiredSize??new Size();
        return new(double.IsFinite(available.Width)?Math.Min(available.Width,desired.Width):desired.Width,desired.Height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        content?.Arrange(new Rect(0,0,Math.Max(size.Width,content.DesiredSize.Width),size.Height));
        clip.Rect=new Rect(0,0,size.Width,size.Height);
        HorizontalOffset=Math.Clamp(HorizontalOffset,0,Math.Max(0,(content?.DesiredSize.Width??0)-size.Width));translation.X=-HorizontalOffset;
        return size;
    }
}
