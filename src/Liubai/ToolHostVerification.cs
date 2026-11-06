using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyToolHost()
    {
        string result="PASS",error="";var checks=new List<string>();
        try
        {
            await Task.Delay(400);
            if(SessionState.Locked==true)throw new IOException("LOCKED: unlock before tool host verification");
            App.VerifyColdFeatures();
            if(toolbarButtons.Count!=ToolCatalog.BuiltIn.Items.Count || toolIds.Count!=toolbarButtons.Count || toolMenus.Count!=3)throw new Exception("Compiled tool bindings incomplete");
            foreach(var definition in ToolCatalog.BuiltIn.Items)
                if(ToolbarLayoutPolicy.Name(definition.Id)!=definition.Name || toolIds[toolbarButtons[definition.Id]]!=definition.Id)throw new Exception("Metadata/UI registration mismatch");
            checks.Add("All built-in entries bind one stable ID; names, ordering and capabilities share the catalog without initializing services");
            foreach(string id in new[]{"todo","note","clock","station","settings"})
            {
                if(!await tools!.Invoke(id)||!tools.Active(id)||AutomationProperties.GetItemStatus(toolbarButtons[id])!="已展开")throw new Exception("Registered panel did not open/highlight: "+id);
                if(!await tools.Invoke(id)||tools.Active(id))throw new Exception("Second invocation did not close: "+id);
            }
            checks.Add("Registered todo/note/clock/station/settings open, highlight and close on second invocation");
            var peer=new ButtonAutomationPeer(clockButton);
            var click=(IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            click.Invoke();long deadline=Environment.TickCount64+3000;
            while(!tools!.Active("clock")){if(Environment.TickCount64>deadline)throw new Exception("Native button event did not route to registered tool");await Task.Delay(20);}
            click.Invoke();deadline=Environment.TickCount64+3000;
            while(tools.Active("clock")){if(Environment.TickCount64>deadline)throw new Exception("Native button event toggled twice or did not close");await Task.Delay(20);}
            checks.Add("Actual native button Invoke routes through exactly one registered click handler");
            await tools!.Invoke("todo");var shared=plannerWindow;
            await tools.Invoke("note");
            if(!ReferenceEquals(shared,plannerWindow)||tools.Active("todo")||!tools.Active("note"))throw new Exception("Shared records group recreated or selected wrong tab");
            await tools.Invoke("note");
            checks.Add("Todo and note share one panel with independent active tab state");
            var task=new DataPackage();task.SetText("接口待办");
            if(!await tools.Drop("todo",task.GetView())||App.Planner.Data.Tasks.Single().Title!="接口待办")throw new Exception("Registered todo drop failed");
            // Incoming-drop grace deliberately protects the panel for 350ms.
            await Task.Delay(450);
            var note=new DataPackage();note.SetText("接口随手记");
            if(!await tools.Drop("note",note.GetView()))throw new Exception("Registered note drop failed");
            await Task.Delay(450);
            var station=new DataPackage();station.SetText("接口中转站");
            if(!await tools.Drop("station",station.GetView())||App.Station.Items.Single().Value!="接口中转站")throw new Exception("Registered station drop failed");
            checks.Add("Explicit DataPackage drops dispatch to records and station; no clipboard or implicit AI sending");
            await Task.Delay(450);
            panelLifecycle!.Hide(false);
            if(!await FlushToolPanels()||App.Planner.Data.Notes.Single().Text!="接口随手记")throw new Exception("Common save barrier did not preserve records");
            stationWindow!.VerifyReleasedView();
            checks.Add("Common hide/save barrier releases station previews and persists shared records");
            if(!tools.Peek("todo")||peekWindow?.IsOpen!=true)throw new Exception("Registered peek did not open");
            FinishPeek();
            if(peekWindow?.IsOpen==true)throw new Exception("Peek did not release its presentation");
            if(App.CurrentAi!=null||App.CurrentGuard!=null)throw new Exception("Passive registration instantiated AI/guard");
            checks.Add("Peek capability uses common presentation without initializing AI or guard; hardware switches not invoked");
        }
        catch(Exception ex){result="FAIL";error=ex.ToString();}
        finally
        {
            panelLifecycle?.Hide(true);await FlushPlanner();
            File.WriteAllText(Path.Combine(App.DataDirectory,"tool-host-check.json"),System.Text.Json.JsonSerializer.Serialize(new{result,error,checks}));
            Close();
        }
    }
}
