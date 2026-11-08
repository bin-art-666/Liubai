using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Liubai;

public sealed partial class BarWindow
{
    readonly BarVisibilityPolicy autoVisibility=new();
    readonly WindowColorSampler visibilitySampler=new();
    Func<DesktopMonitor,bool>? visibilityProbe;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? autoHideTimer;
    Storyboard? autoEntrance;
    BarDisplayMode appliedDisplayMode;
    bool autoExpanded,autoExpandedCandidate,autoSuspended;
    long sceneCandidateSince,nextVisibilityProbe;
    internal bool AutoHideActive=>preferences.DisplayMode==BarDisplayMode.AutoHide||preferences.DisplayMode==BarDisplayMode.HideWhenExpanded&&autoExpanded;
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point point);
    void InitializeAutoHide()
    {
        visibilityProbe??=visibilitySampler.ExpandedForVisibility;
        autoHideTimer=DispatcherQueue.CreateTimer();autoHideTimer.Interval=TimeSpan.FromMilliseconds(100);
        autoHideTimer.Tick+=(_,_)=>TickAutoHide();ConfigureAutoHide();
    }
    void ConfigureAutoHide()
    {
        if(autoHideTimer==null||closing)return;
        if(appliedDisplayMode!=preferences.DisplayMode)
        {
            appliedDisplayMode=preferences.DisplayMode;autoExpanded=autoExpandedCandidate=false;nextVisibilityProbe=sceneCandidateSince=0;
            autoVisibility.Reset(preferences.DisplayMode!=BarDisplayMode.AutoHide);
        }
        if(preferences.DisplayMode==BarDisplayMode.Always||!barVisible||autoSuspended||manager?.Suspended==true)autoHideTimer.Stop();
        else autoHideTimer.Start();
        Position();UpdateVisibility();
    }
    bool AutoHideProtected()=>draggedToolbarButton!=null||heldButton!=null||preparingScreenshot
        ||settingsWindow?.IsOpen==true||clockWindow?.IsOpen==true||qrResults?.IsOpen==true
        ||plannerWindow?.IsOpen==true&&plannerWindow.DisplayDevice==Display.Device
        ||stationWindow is {IsOpen:true,Detached:false}
        ||App.CurrentAi is {DockedOpen:true} ai&&ai.DisplayDevice==Display.Device
        ||moreWindow?.IsOpen==true||workSceneWindow?.IsOpen==true||focusWindow?.IsOpen==true||peekWindow?.IsOpen==true||screenshotPreview?.IsOpen==true
        ||surface.XamlRoot!=null&&VisualTreeHelper.GetOpenPopupsForXamlRoot(surface.XamlRoot).Any(p=>p.IsOpen);
    void TickAutoHide()
    {
        if(closing||!barVisible||autoSuspended||manager?.Suspended==true||QrSelectionWindow.Active)return;
        long now=Environment.TickCount64;
        if(preferences.DisplayMode==BarDisplayMode.HideWhenExpanded&&now>=nextVisibilityProbe)
        {
            nextVisibilityProbe=now+300;
            try
            {
                bool expanded=visibilityProbe!(Display);
                {
                    if(expanded!=autoExpandedCandidate){autoExpandedCandidate=expanded;sceneCandidateSince=now;}
                    if(autoExpanded!=autoExpandedCandidate&&now-sceneCandidateSince>=250)
                    {autoExpanded=autoExpandedCandidate;autoVisibility.Reset(!autoExpanded);Position();}
                }
            }
            catch { /* Retain the previous scene while a monitor/window is unavailable. */ }
        }
        if(!GetCursorPos(out var cursor))return;
        EvaluateAutoPointer(cursor,now,AutoHideProtected());
    }
    void EvaluateAutoPointer(Native.Point cursor,long now,bool protectedOperation)
    {
        var bounds=Display.Bounds;var rect=BarRectangle;
        bool inside=windowShown&&cursor.X>=rect.X&&cursor.X<rect.X+rect.Width&&cursor.Y>=rect.Y&&cursor.Y<rect.Y+rect.Height;
        bool edge=BarVisibilityPolicy.HotEdge(cursor.X,cursor.Y,bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,Math.Max(1,(int)Math.Round(2*Display.Dpi/96d)));
        autoVisibility.Update(AutoHideActive,edge,inside,protectedOperation,now);UpdateVisibility();
    }
    void RevealAutoBar()
    {
        autoVisibility.Reveal();UpdateVisibility();
    }
    void SetAutoHideSuspended(bool suspended)
    {
        autoSuspended=suspended;
        if(suspended){autoVisibility.Reset(false);autoHideTimer?.Stop();UpdateVisibility();}
        else {nextVisibilityProbe=0;ConfigureAutoHide();}
    }
    void StopAutoEntrance()
    {
        autoEntrance?.Stop();autoEntrance=null;surface.Opacity=1;
        if(surface.RenderTransform is TranslateTransform translation)translation.Y=0;
    }
    void AnimateAutoReveal()
    {
        StopAutoEntrance();
        if(!AutoHideActive||!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)return;
        var translation=new TranslateTransform();surface.RenderTransform=translation;
        var movement=new DoubleAnimation{From=-6,To=0,Duration=new Duration(TimeSpan.FromMilliseconds(140)),EnableDependentAnimation=true};
        var opacity=new DoubleAnimation{From=0,To=1,Duration=new Duration(TimeSpan.FromMilliseconds(140))};
        Storyboard.SetTarget(movement,translation);Storyboard.SetTargetProperty(movement,"Y");Storyboard.SetTarget(opacity,surface);Storyboard.SetTargetProperty(opacity,"Opacity");
        autoEntrance=new Storyboard();autoEntrance.Children.Add(movement);autoEntrance.Children.Add(opacity);autoEntrance.Begin();
    }
}
