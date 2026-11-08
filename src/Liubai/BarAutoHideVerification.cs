using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyAutoHide()
    {
        string result="PASS",error="";var checks=new List<string>();var original=preferences.DisplayMode;
        try
        {
            await Task.Delay(350);if(SessionState.Locked==true)throw new IOException("LOCKED: unlock before verification");
            preferences.SetDisplayMode(BarDisplayMode.AutoHide);ConfigureAutoHide();autoHideTimer!.Stop();autoVisibility.Reset(false);UpdateVisibility();
            await Task.Delay(200);int releasedTop=Display.Info.Work.Top;
            if(windowShown||releasedTop!=originalWorkTop)throw new Exception("Hidden mode did not release its work area");
            var bounds=Display.Bounds;var middle=new Native.Point{X=bounds.Left+(bounds.Right-bounds.Left)/2,Y=bounds.Top};
            EvaluateAutoPointer(middle,0,false);EvaluateAutoPointer(middle,500,false);if(windowShown)throw new Exception("Center third revealed the bar");
            var left=new Native.Point{X=bounds.Left+1,Y=bounds.Top};EvaluateAutoPointer(left,600,false);EvaluateAutoPointer(left,799,false);if(windowShown)throw new Exception("Edge dwell was skipped");
            EvaluateAutoPointer(left,800,false);await Task.Delay(170);
            if(!windowShown||Display.Info.Work.Top!=releasedTop||BarRectangle.Y!=bounds.Top)throw new Exception("Overlay reveal resized the work area");
            var away=new Native.Point{X=middle.X,Y=bounds.Top+300};EvaluateAutoPointer(away,900,false);EvaluateAutoPointer(away,1599,false);if(!windowShown)throw new Exception("Leave delay was skipped");EvaluateAutoPointer(away,1600,false);if(windowShown)throw new Exception("Leave timeout did not hide");
            var right=new Native.Point{X=bounds.Right-2,Y=bounds.Top};EvaluateAutoPointer(right,1700,false);EvaluateAutoPointer(right,1900,false);if(!windowShown)throw new Exception("Right third did not reveal");
            checks.Add("Left/right edge dwell reveals; center is blocked; leave delay hides; overlay never reserves work area");
            OpenSettings();await Task.Delay(150);autoHideTimer.Stop();
            if(!AutoHideProtected())throw new Exception("Open settings did not protect visibility");EvaluateAutoPointer(away,10000,AutoHideProtected());if(!windowShown)throw new Exception("Open panel was hidden");
            var settingsRoot=(Microsoft.UI.Xaml.DependencyObject)settingsWindow!.Content;
            StationControls<Expander>(settingsRoot).Single(c=>c.Header?.ToString()=="日常设置与配置").IsExpanded=true;
            await Task.Delay(150);
            var selector=StationControls<ComboBox>(settingsRoot).Single(c=>AutomationProperties.GetName(c)=="顶栏显示方式");
            selector.SelectedIndex=(int)BarDisplayMode.HideWhenExpanded;await Task.Delay(100);autoHideTimer.Stop();
            if(preferences.DisplayMode!=BarDisplayMode.HideWhenExpanded)throw new Exception("Settings mode did not apply");settingsWindow.Hide();await Task.Delay(100);autoHideTimer.Stop();
            checks.Add("Settings expose all three display modes and keep the bar visible while its panel is open");
            visibilityProbe=_=>true;autoExpandedCandidate=true;sceneCandidateSince=Environment.TickCount64-1000;nextVisibilityProbe=0;TickAutoHide();await Task.Delay(180);
            if(!AutoHideActive||Display.Info.Work.Top!=releasedTop)throw new Exception("Expanded scene did not enter overlay mode");
            visibilityProbe=_=>false;autoExpandedCandidate=false;sceneCandidateSince=Environment.TickCount64-1000;nextVisibilityProbe=0;TickAutoHide();await Task.Delay(180);
            if(AutoHideActive||!windowShown||Display.Info.Work.Top<=releasedTop)throw new Exception("Restored scene did not restore resident reservation");
            checks.Add("Expanded/restore observations release and restore reservation independently of appearance sampling");
            preferences.SetDisplayMode(BarDisplayMode.AutoHide);ConfigureAutoHide();autoHideTimer.Stop();autoVisibility.Reset(false);UpdateVisibility();
            SetVisible(false);EvaluateAutoPointer(left,20000,false);EvaluateAutoPointer(left,21000,false);if(windowShown)throw new Exception("Manual hide was bypassed by an edge");
            SetVisible(true);autoHideTimer.Stop();if(!windowShown)throw new Exception("Tray show did not reveal the bar");
            SetAutoHideSuspended(true);if(windowShown||autoHideTimer.IsRunning)throw new Exception("Suspend left auto-hide active");SetAutoHideSuspended(false);autoHideTimer.Stop();
            checks.Add("Manual hide overrides edge activation; show recovers access; suspension stops visibility polling");
        }
        catch(Exception ex){result="FAIL";error=ex.ToString();}
        finally
        {
            visibilityProbe=visibilitySampler.ExpandedForVisibility;preferences.SetDisplayMode(original);ConfigureAutoHide();autoHideTimer?.Stop();
            File.WriteAllText(Path.Combine(App.DataDirectory,"auto-hide-check.json"),JsonSerializer.Serialize(new{result,error,checks}));Close();
        }
    }
}
