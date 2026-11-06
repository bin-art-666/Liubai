using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

public sealed partial class BarWindow
{
    ToolHost<DataPackageView>? tools;
    PanelLifecycle? panelLifecycle;
    readonly Dictionary<Button, string> toolIds = new();
    readonly Dictionary<Button, Func<FlyoutBase>> toolMenus = new();
    bool ToolHostAvailable => !closing && !openingPlanner && !PanelModal && manager?.HasModal != true && manager?.Suspended != true;
    void ToolFailed(string id, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(App.DataDirectory); string path = Path.Combine(App.DataDirectory,"tools.log");
            if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path,"");
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} {id}: {exception}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (!closing) ShowScreenshotResult("操作未完成，请重试；内容保留。详情见本机 tools.log。");
    }
    void InitializeToolHost()
    {
        tools = new(ToolCatalog.BuiltIn, () => ToolHostAvailable, ToolFailed);
        Task Run(Action action) { action(); return Task.CompletedTask; }
        void Add(string id, Button button, Func<Task> invoke, Func<bool>? active = null,
            Func<DataPackageView,bool>? accepts = null, Func<DataPackageView,Task<bool>>? drop = null, Func<FlyoutBase>? menu = null)
        {
            var definition = ToolCatalog.BuiltIn[id]; toolbarButtons.Add(id,button); toolIds.Add(button,id);
            if (menu != null) toolMenus.Add(button,menu);
            tools.Register(id,new(invoke,active,accepts,drop,definition.Has(ToolCapabilities.Peek) ? () => OpenToolPeek(id) : null));
            button.Click += async (_, _) => { if (!ToolbarClickBlocked) await tools.Invoke(id); };
        }
        Add("screenshot",screenshot,TakeScreenshot,menu:CreateScreenshotMenu);
        Add("qr",qrScan,ScanQr);
        Add("clock",clockButton,()=>Run(()=>{if(clockWindow?.IsOpen==true)clockWindow.Hide();else OpenClock();}),()=>clockWindow?.IsOpen==true);
        Add("todo",todo,()=>OpenPlanner(PlannerPage.Tasks),()=>PlannerActive(PlannerPage.Tasks),DropPayload.Supported,p=>ReceivePlanner("todo",p));
        Add("note",quickNote,()=>OpenPlanner(PlannerPage.Notes),()=>PlannerActive(PlannerPage.Notes),DropPayload.Supported,p=>ReceivePlanner("note",p));
        Add("station",shelf,()=>Run(()=>{stationHoverTimer?.Stop();if(stationWindow?.IsOpen==true||workSceneWindow?.IsOpen==true){stationWindow?.Hide();workSceneWindow?.Hide();}else OpenStation();}),
            ()=>stationWindow?.IsOpen==true||workSceneWindow?.IsOpen==true,p=>!App.Station.ReadOnly&&StationTransfer.Supported(p),async p=>
            {if(!OpenStation(true))return false;bool saved=await stationWindow!.Receive(p);if(saved)stationWindow.ConfirmDrop();return saved;});
        Add("guard",guardButton,()=>App.Guard.SetEnabled(App.CurrentGuard?.Wanted!=true),menu:CreateGuardMenu);
        Add("privacy",privacyButton,async ()=>{if(!App.Privacy.Busy)await App.Privacy.Toggle();});
        Add("ai",aiButton,async ()=>{if(App.CurrentAi?.IsOpen==true)App.HideAi();else await OpenAiPanel();},
            ()=>App.CurrentAi?.IsOpen==true&&App.CurrentAi.DisplayDevice==Display.Device,DropPayload.Supported,async p=>{await OpenAiPanel();return await App.Ai.ReceiveDrop(p);},CreateAiMenu);
        Add("settings",settings,()=>Run(ToggleSettings),()=>settingsWindow?.IsOpen==true);
        tools.Seal();
        foreach (var definition in ToolCatalog.BuiltIn.Items) actions.Children.Add(toolbarButtons[definition.Id]);
        InitializePanelLifecycle();
    }
    bool PlannerActive(PlannerPage page)
    {var planner=plannerWindow??manager?.CurrentPlanner;return planner?.IsOpen==true&&planner.DisplayDevice==Display.Device&&planner.Page==page;}
    async Task<bool> ReceivePlanner(string id,DataPackageView payload)
    {
        var panel=await OpenImportPlanner(id=="todo"?PlannerPage.Tasks:PlannerPage.Notes);
        bool saved=await panel.ReceiveDrop(payload);if(saved)panel.ConfirmIncomingDrop();return saved;
    }
    void OpenToolPeek(string id)
    {peekWindow??=new PeekWindow();peekWindow.Open(id,Display,windowShown?AppWindow.Position.Y+AppWindow.Size.Height:Display.Info.Work.Top);}
    void InitializePanelLifecycle()
    {
        panelLifecycle = new(ToolFailed);
        Task<bool> Saved() => Task.FromResult(true);
        panelLifecycle.Register(new("settings",()=>settingsWindow?.ModalActive==true,_=>settingsWindow?.Hide(),()=>settingsWindow?.SaveAllSettings()??Saved(),()=>settingsWindow?.Destroy()));
        panelLifecycle.Register(new("records",()=>plannerWindow?.ModalActive==true,_=>plannerWindow?.Hide(),()=>manager==null?plannerWindow?.FlushAsync()??Saved():Saved(),()=>{if(manager==null)plannerWindow?.Destroy();}));
        panelLifecycle.Register(new("clock",()=>clockWindow?.ModalActive==true,_=>clockWindow?.Hide(),Saved,()=>clockWindow?.Destroy()));
        panelLifecycle.Register(new("station",()=>stationWindow?.ModalActive==true,all=>{if(all)stationWindow?.Hide();else stationWindow?.HideDocked();},async ()=>{await App.FlushStation();return true;},()=>stationWindow?.Destroy()));
        // AI is application-owned. Monitor removal hides only its docked local panel;
        // application shutdown separately releases the shared browser service.
        panelLifecycle.Register(new("ai",()=>App.CurrentAi?.ModalActive==true,App.HideAi,Saved,()=>{}));
        panelLifecycle.Register(new("qr",()=>qrResults?.ModalActive==true,_=>qrResults?.Hide(),Saved,()=>qrResults?.Destroy()));
        panelLifecycle.Register(new("scene",()=>workSceneWindow?.ModalActive==true,_=>workSceneWindow?.Hide(),Saved,()=>workSceneWindow?.Destroy()));
        panelLifecycle.Register(new("focus",()=>focusWindow?.ModalActive==true,_=>focusWindow?.Hide(),Saved,()=>focusWindow?.Destroy()));
        panelLifecycle.Register(new("screenshot",()=>screenshotPreview?.ModalActive==true,_=>screenshotPreview?.Hide(),Saved,()=>screenshotPreview?.Destroy()));
        panelLifecycle.Register(new("more",()=>moreWindow?.ModalActive==true,_=>moreWindow?.Hide(),Saved,()=>moreWindow?.Destroy()));
    }
    internal async Task<bool> FlushToolPanels()
    { if(tools!=null)await tools.WaitForIdle();return panelLifecycle==null||await panelLifecycle.Save(); }
}
