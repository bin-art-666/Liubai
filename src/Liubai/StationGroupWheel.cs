using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Liubai;

internal sealed partial class StationWindow
{
    protected override bool UsesPanelWheel=>true;
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Native.Point point);
    protected override bool HandlePanelWheel(Native.Point point,int delta,bool horizontal)
    {
        if(!IsOpen||SearchActive||busy||workspaceLabelHost.Visibility!=Visibility.Visible||workspaceScroller.ActualWidth<=0||workspaceScroller.ActualHeight<=0||Content is not FrameworkElement shell||shell.XamlRoot==null)return false;
        var position=workspaceScroller.TransformToVisual(shell).TransformPoint(new());
        var origin=new Native.Point();if(!ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this),ref origin))return false;
        double scale=shell.XamlRoot.RasterizationScale;
        double x=point.X-origin.X-position.X*scale,y=point.Y-origin.Y-position.Y*scale;
        if(x<0||y<0||x>=workspaceScroller.ActualWidth*scale||y>=workspaceScroller.ActualHeight*scale)return false;
        DispatcherQueue.TryEnqueue(()=>{if(IsOpen&&!SearchActive&&!busy)ScrollWorkspaceLabels(delta*(horizontal?1:-1)*.8);});
        return true;
    }
}
