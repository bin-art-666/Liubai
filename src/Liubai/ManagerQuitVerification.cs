using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

public sealed partial class BarWindow
{
    internal async Task VerifyCancelClockQuit()
    {
        long end=Environment.TickCount64+4000;ContentDialog? dialog=null;
        static IEnumerable<ContentDialog> Find(DependencyObject root)
        {
            if(root is ContentDialog match)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var found in Find(VisualTreeHelper.GetChild(root,i)))yield return found;
        }
        while(dialog==null)
        {
            if(Environment.TickCount64>end)throw new Exception("Manager exit gate prevented clock confirmation from opening");
            if(clockWindow?.Content is FrameworkElement { XamlRoot: not null } root)
                dialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Where(p=>p.Child!=null).SelectMany(p=>Find(p.Child!)).SingleOrDefault();
            if(dialog==null)await Task.Delay(30);
        }
        if(dialog.Title as string!="仍有闹钟或计时未结束"||dialog.DefaultButton!=ContentDialogButton.Close)throw new Exception("Wrong clock exit confirmation");
        dialog.Hide();
    }
}

internal sealed partial class BarManager
{
    internal async Task VerifyManagerQuit()
    {
        string result="PASS",error="";var checks=new List<string>();
        try
        {
            await Task.Delay(400);if(SessionState.Locked==true)throw new IOException("LOCKED: unlock before manager quit verification");
            await App.Clock.StartCountdown(120,"退出流程隔离验证",DateTimeOffset.Now);
            var owner=bars.Values.First(b=>b.Display.Primary);
            Quit();await owner.VerifyCancelClockQuit();
            checks.Add("Real multi-monitor manager can open active-clock confirmation before applying exit input gate");
            long end=Environment.TickCount64+4000;
            while(confirmingQuit){if(Environment.TickCount64>end)throw new Exception("Quit cancellation did not finish");await Task.Delay(30);}
            if(quitting||App.Clock.Data.Countdown?.State!=ClockState.Running||bars.Count==0)throw new Exception("Cancel quit stopped countdown or released live bars");
            checks.Add("Cancel exit preserves running countdown and live tool hosts");
            await App.Clock.CancelCountdown();
            checks.Add("Cancel fixture countdown then exit through common save/release barrier");
        }
        catch(Exception ex){result="FAIL";error=ex.ToString();}
        finally
        {
            await App.Clock.CancelCountdown();
            File.WriteAllText(Path.Combine(App.DataDirectory,"manager-quit-check.json"),System.Text.Json.JsonSerializer.Serialize(new{result,error,checks}));
            Quit();
        }
    }
}
