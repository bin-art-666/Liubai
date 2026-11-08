using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

internal sealed partial class StationWindow
{
    Guid? workspaceId;
    bool ungrouped, workspaceSync;
    int workspacePage;
    int workspaceHoverVersion;
    readonly Grid workspaceGroupRow = new() {ColumnSpacing=6};
    readonly Grid workspaceLabelHost = new() {ColumnSpacing=6};
    readonly GroupStripViewport workspaceScroller=new();
    readonly Button workspaceOverflow=IconButton("\uE712","更多分组");
    bool revealWorkspaceLabel;
    int workspaceLabelVersion;
    int workspaceScrollVersion;
    readonly StackPanel workspaceLabels = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly StackPanel stationCommands = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    Button workspaceDrag = null!;
    readonly ComboBox workspaceTabs = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    readonly TextBlock workspaceSummary = Hint("");
    readonly StackPanel workspaceTools = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    Button createWorkspaceButton=null!, importWorkspaceButton=null!;
    internal event Func<WorkRef, Task>? ItemRequested;
    sealed record WorkspaceChoice(Guid? Id, string Label, bool Ungrouped = false) { public override string ToString() => Label; }
    void InitializeGroupWheel(Grid shell)
    {
        // Wheel routing can follow the focused control. Hit-test its pointer
        // position at the common shell so cards/header/resize focus do not matter.
        shell.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,e)=>{
            if(!IsOpen||SearchActive||workspaceLabelHost.Visibility!=Visibility.Visible)return;
            var pointer=e.GetCurrentPoint(workspaceScroller);var point=pointer.Position;
            if(point.X<0||point.Y<0||point.X>=workspaceScroller.ActualWidth||point.Y>=workspaceScroller.ActualHeight)return;
            e.Handled=true;var wheel=pointer.Properties;
            ScrollWorkspaceLabels(wheel.MouseWheelDelta*(wheel.IsHorizontalMouseWheel?1:-1)*.8);
        }),true);
    }
    void InitializeWorkspace(StackPanel intro, StackPanel footer)
    {
        // Only companion metadata is loaded here; planner and clocks stay lazy until a group is opened.
        workspaceId = App.WorkStore.Data.Active;
        workspaceGroupRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        workspaceGroupRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        workspaceLabelHost.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});workspaceLabelHost.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        workspaceScroller.Content=workspaceLabels;workspaceLabelHost.Children.Add(workspaceScroller);Grid.SetColumn(workspaceOverflow,1);workspaceLabelHost.Children.Add(workspaceOverflow);workspaceGroupRow.Children.Add(workspaceLabelHost);
        workspaceLabels.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var overflowMenu=new MenuFlyout();overflowMenu.Opening+=(_,_)=>RefreshWorkspaceOverflow();workspaceOverflow.Flyout=overflowMenu;
        Grid.SetColumn(workspaceTools,1);workspaceGroupRow.Children.Add(workspaceTools);
        intro.Children.Add(workspaceGroupRow);intro.Children.Add(workspaceTabs);
        workspaceLabelHost.SizeChanged+=(_,e)=>{if(Math.Abs(e.NewSize.Width-e.PreviousSize.Width)>.5)RenderWorkspaceLabels();};
        PanelTheme.Changed+=WorkspaceStyleChanged;
        foreach (string name in new[] { "资料", "待办", "笔记" }) workspaceTabs.Items.Add(name);
        workspaceTabs.SelectedIndex = 0;
        var create = createWorkspaceButton = IconButton("\uE710","新建工作分组"); create.Click += async (_,_)=>await Run(CreateWorkspace); workspaceTools.Children.Add(create);
        var import=importWorkspaceButton=IconButton("\uE8B5","导入分组归档"); import.Click+=async (_,_)=>await Run(ImportWorkspace); workspaceTools.Children.Add(import);
        workspaceDrag=IconButton("\uE8DE","拖出整个分组的资料");workspaceDrag.CanDrag=true;workspaceDrag.Visibility=Visibility.Collapsed;workspaceTools.Children.Add(workspaceDrag);
        IDisposable? lease=null;
        workspaceDrag.DragStarting+=async (_,e)=> { var items=WorkspaceItems().ToArray(); if(busy||items.Length==0) { e.Cancel=true; return; } lease=KeepOpenForDrag(); var deferral=e.GetDeferral(); try { await StationTransfer.FillMany(e.Data,items,store.AttachmentDirectory); e.AllowedOperations=DataPackageOperation.Copy; } catch { e.Cancel=true; lease.Dispose();lease=null;status.Text="分组中有不可用文件，请检查原位置"; } finally { deferral.Complete(); } };
        workspaceDrag.DropCompleted+=(_,_)=> { lease?.Dispose();lease=null; };
        workspaceTabs.SelectionChanged += (_, _) => { if (workspaceSync) return; workspacePage = Math.Max(0,workspaceTabs.SelectedIndex); ClearSelection(); if (IsOpen) Refresh(); };
        App.WorkStore.Changed += WorkspaceChanged;
        RefreshWorkspaces();
    }
    WorkScene? Scene() => App.WorkStore.Data.Scenes.FirstOrDefault(s => s.Id == workspaceId);
    internal void OpenWorkspace(Guid group)
    { if(!App.WorkStore.Data.Scenes.Any(s=>s.Id==group)) return; revealWorkspaceLabel=true;workspaceId=group; ungrouped=false; workspacePage=0; ClearSelection(); Refresh(); }
    async Task HoverWorkspace(Guid group)
    {
        int version=++workspaceHoverVersion; await Task.Delay(450);
        if(version!=workspaceHoverVersion||!IsOpen||destroyed||busy||ModalActive||workspaceId==group||!App.WorkStore.Data.Scenes.Any(s=>s.Id==group))return;
        await Run(async ()=> {OpenWorkspace(group);await App.Work.Store.Activate(group);});
    }
    void WorkspaceChanged() { if (destroyed) return; if (workspaceId != null && Scene() == null) workspaceId = null; if (IsOpen) Refresh(); }
    void DisposeWorkspace()
    { PanelTheme.Changed-=WorkspaceStyleChanged; App.WorkStore.Changed -= WorkspaceChanged; if (workspacePlannerSubscribed) App.Planner.Changed -= WorkspaceChanged; if (workspaceClockSubscribed) { App.Clock.Changed -= WorkspaceChanged; if (App.Clocks != null) App.Clocks.Pulse -= FocusPulse; } }
    bool workspacePlannerSubscribed, workspaceClockSubscribed;
    void RefreshWorkspaces()
    {
        workspaceSync = true;
        try
        {
            RenderWorkspaceLabels();
            workspaceTabs.Visibility = Scene() == null ? Visibility.Collapsed : Visibility.Visible;
            workspaceTabs.SelectedIndex = workspacePage;
            workspaceSummary.Text = Scene()?.Description ?? "";
            workspaceSummary.Visibility=Scene()==null?Visibility.Collapsed:Visibility.Visible;
            if (Scene() is { } active)
            {
                if (!workspacePlannerSubscribed) { App.Planner.Changed += WorkspaceChanged; workspacePlannerSubscribed = true; }
                if (!workspaceClockSubscribed) { App.Clock.Changed += WorkspaceChanged; if (App.Clocks != null) App.Clocks.Pulse += FocusPulse; workspaceClockSubscribed = true; }
                int todo = App.Planner.Data.Tasks.Count(t => !t.Completed && active.Items.Any(r => r.Kind == WorkKind.Task && r.Id == t.Id));
                workspaceSummary.Text = (string.IsNullOrWhiteSpace(active.Description)?"":active.Description+"\n") + $"{active.Items.Count(r=>r.Kind==WorkKind.Station)} 份资料 · {todo} 项未完成待办";
                var tasks = active.Items.Where(r=>r.Kind==WorkKind.Task).Select(r=>r.Id).ToHashSet();
                if (App.WorkStore.Data.Resumes.FirstOrDefault(r=>tasks.Contains(r.TaskId)&&!string.IsNullOrWhiteSpace(r.Text)) is { } resume) workspaceSummary.Text += "\n上次进度："+resume.Text;
                int seconds = (App.WorkStore.Data.Sessions ?? []).Where(s=>tasks.Contains(s.TaskId)).Sum(s=>s.Seconds);
                if (seconds > 0) workspaceSummary.Text += $"\n累计专注 {seconds/60} 分钟";
                FocusPulse();
            }
        }
        finally { workspaceSync = false; }
    }
    void FocusPulse()
    {
        // Update text only: never rebuild thumbnails or editors every second.
        if (!IsOpen || Scene() is not { } scene || App.WorkStore.Data.Focus is not { } focus || !scene.Items.Any(r=>r.Kind==WorkKind.Task&&r.Id==focus.TaskId)) return;
        if (App.Clock.Data.Countdown is { } clock && clock.Id == focus.ClockId)
        {
            string baseText = workspaceSummary.Text.Split("\n专注：")[0];
            double seconds = clock.State == ClockState.Running && clock.EndsAt is { } end ? Math.Max(0,(end-DateTimeOffset.Now).TotalSeconds) : TimeSpan.FromTicks(clock.RemainingTicks).TotalSeconds;
            workspaceSummary.Text = baseText + $"\n专注：{focus.Title} · {clock.State switch { ClockState.Paused=>"已暂停",ClockState.Finished=>"已到时",_=>TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(@"mm\:ss") }}";
        }
    }
    IEnumerable<StationItem> WorkspaceItems()
    {
        if (Scene() is { } scene) { var ids = scene.Items.Where(i=>i.Kind==WorkKind.Station).Select(i=>i.Id).ToHashSet(); return store.Items.Where(i=>ids.Contains(i.Id)); }
        if (ungrouped) { var ids = App.WorkStore.Data.Scenes.SelectMany(s=>s.Items).Where(i=>i.Kind==WorkKind.Station).Select(i=>i.Id).ToHashSet(); return store.Items.Where(i=>!ids.Contains(i.Id)); }
        return store.Items;
    }
    async Task CreateWorkspace()
    {
        var name = new TextBox { PlaceholderText = "例如：论文修改", MaxLength = 80 };
        var items = SelectionItems().Where(i=>selection.Contains(i.Id)).ToArray();
        var dialog=new PanelEntryDialog(root.XamlRoot,"新建工作分组","创建",name,[name],()=>WorkspaceNameError(name.Text),async ()=> {
            workspaceId = await App.Work.Store.Create(name.Text); ungrouped = false;
            await App.Work.Store.Link(workspaceId,items.Select(i=>new WorkRef(WorkKind.Station,i.Id))); ClearSelection(); Refresh();
        });
        await dialog.Show();
    }
    string? WorkspaceNameError(string name,Guid? except=null)
    {
        if(string.IsNullOrWhiteSpace(name))return "请输入分组名称";
        if(App.WorkStore.Data.Scenes.Any(s=>s.Id!=except&&s.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))return "已有同名分组，请换一个名称";
        return null;
    }
    Task ChooseWorkspace(IReadOnlyList<StationItem> items)
    {
        var menu=new MenuFlyout {MenuFlyoutPresenterStyle=GroupMenuStyle()};var session=new GroupMenuSession(this,menu);
        menu.Opening+=(_,_)=>session.Populate(menu.Items,items.ToArray());
        menu.ShowAt(Detached?expandedActions[StationAction.Group]:expand);
        return Task.CompletedTask;
    }
    async Task<bool> ReceiveForWorkspace(DataPackageView view, Guid id)
    {
        workspaceHoverVersion++;
        bool saved=false;
        await Run(async () => {
            if(await StationTransfer.InternalItems(view,store) is {} existing) { await App.Work.Store.Link(id,existing.Select(i=>new WorkRef(WorkKind.Station,i.Id))); saved=true; status.Text=$"已将 {existing.Length} 项加入分组"; return; }
            using var suspension=App.Work.SuspendCollection();
            using var import=await StationTransfer.Read(view,store.AttachmentDirectory);
            await store.Add(import.Inputs); import.Commit();
            var refs=import.Inputs.Select(input=>store.Items.First(i=>i.Kind==input.Kind&&i.Value.Equals(input.Value,input.Kind is StationKind.File or StationKind.Folder?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))).Select(i=>new WorkRef(WorkKind.Station,i.Id));
            await App.Work.Store.Link(id,refs); saved=true; status.Text=$"已将 {import.Inputs.Length} 项加入分组";
        });
        if(saved) ConfirmDrop(); return saved;
    }
    async Task EditWorkspace()
    {
        if (Scene() is not { } scene) { status.Text = "请先选择分组"; return; }
        var name = new TextBox { Text = scene.Name, MaxLength = 80 }; var description = new TextBox { AcceptsReturn = true, Text = scene.Description, PlaceholderText = "分组说明", MaxLength = 2000, TextWrapping = TextWrapping.Wrap };
        var dialog=new PanelEntryDialog(root.XamlRoot,"编辑分组","保存",name,[name,description],()=>WorkspaceNameError(name.Text,scene.Id),async ()=> {
            await App.Work.Store.Rename(scene.Id,name.Text); await App.Work.Store.Describe(scene.Id,description.Text,scene.Pinned);
        },description);
        await dialog.Show();
    }
    async Task DissolveWorkspace()
    {
        if (Scene() is not { } scene) return;
        if (await new ContentDialog { XamlRoot = root.XamlRoot, Title = "解散分组？", Content = "资料、待办、笔记和进度保留，仅解除分组关联。", PrimaryButtonText = "解散", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary) await Remember("已解散分组",()=>App.Work.Store.Remove(scene.Id));
    }
    bool RenderWorkspacePage()
    {
        if (Scene() is not { } scene || workspacePage == 0) return false;
        bool tasks = workspacePage == 1;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = IconButton("\uE710",tasks ? "新建待办" : "新建笔记"); create.Click += async (_, _) => await Run(CreateWorkspaceRecord); actions.Children.Add(create);
        var link = IconButton("\uE8F4","关联已有内容"); link.Click += async (_, _) => await Run(LinkWorkspaceRecords); actions.Children.Add(link); cards.Children.Add(actions);
        var records=scene.Items.Where(r=>r.Kind==(tasks?WorkKind.Task:WorkKind.Note)).ToArray();
        foreach (var reference in records.Take(visibleItems))
        {
            var task = tasks ? App.Planner.Data.Tasks.FirstOrDefault(t=>t.Id==reference.Id) : null;
            var note = !tasks ? App.Planner.Data.Notes.FirstOrDefault(n=>n.Id==reference.Id) : null;
            var row = new StackPanel { Spacing = 6 };
            if (task != null) { var check = new CheckBox { Content = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap }, IsChecked = task.Completed }; check.Click += async (_, _) => await Run(()=>App.Planner.CompleteTask(task.Id,check.IsChecked==true)); row.Children.Add(check); }
            else row.Children.Add(new TextBlock { Text = note?.Text ?? "原记录已移除，可解除关联", TextWrapping = TextWrapping.Wrap, MaxLines = 4 });
            if(task!=null) {
                var sessions=(App.WorkStore.Data.Sessions??[]).Where(s=>s.TaskId==task.Id).ToArray();
                if(sessions.Length>0) row.Children.Add(Hint($"专注 {sessions.Length} 次 · 累计 {sessions.Sum(s=>s.Seconds)/60} 分钟 · 最近 {sessions[0].Ended.ToLocalTime():M/d HH:mm}"));
            }
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var edit = IconButton("\uE70F","打开编辑"); edit.IsEnabled = task != null || note != null; edit.Click += async (_, _) => { if (ItemRequested != null) await Run(()=>ItemRequested(reference)); }; commands.Children.Add(edit);
            if (task != null) { var focus = IconButton("\uE916","开始专注／记录进度"); focus.IsEnabled = !task.Completed; focus.Click += (_, _) => App.RequestFocus(task.Id,display.Device); commands.Children.Add(focus); }
            if(task!=null) {
                var assets=IconButton("\uE8B7","关联任务资料"); assets.Click+=async (_,_)=>await Run(()=>LinkTaskMaterials(task.Id)); commands.Children.Add(assets);
                foreach(var material in (App.WorkStore.Data.TaskAssets??[]).FirstOrDefault(l=>l.TaskId==task.Id)?.StationIds??[]) {
                    var source=store.Items.FirstOrDefault(i=>i.Id==material); if(source==null) continue;
                    var button=new Button { Content=DisplayTitle(source),HorizontalAlignment=HorizontalAlignment.Left }; button.Click+=async (_,_)=>await Run(()=>ItemCommand(source,StationAction.Open)); row.Children.Add(button);
                }
            }
            var unlink = IconButton("\uE74D","解除分组关联，保留原记录"); unlink.Click += async (_, _) => await Run(()=>Remember("已移出分组关联",()=>App.Work.Store.Unlink(scene.Id,reference))); commands.Children.Add(unlink); row.Children.Add(commands); cards.Children.Add(PanelTheme.Card(row));
        }
        if(records.Length>visibleItems) { var more=new Button { Content="继续显示",HorizontalAlignment=HorizontalAlignment.Stretch }; more.Click+=(_,_)=> {visibleItems+=50;Refresh();}; cards.Children.Add(more); }
        return true;
    }
    bool RenderMissingWorkspaceItems()
    {
        if(Scene() is not {} scene || ActiveQuery.Filtered) return false;
        var missing=scene.Items.Where(r=>r.Kind==WorkKind.Station&&!store.Items.Any(i=>i.Id==r.Id)).ToArray();
        foreach(var reference in missing.Take(50)) {
            var row=new StackPanel {Spacing=6};row.Children.Add(Hint("资料记录已移除 · 原分组引用仍保留"));
            if(reference.Asset is {} asset) row.Children.Add(new TextBlock {Text=asset.Title.Length>0?asset.Title:asset.Value,TextWrapping=TextWrapping.Wrap,MaxLines=2});
            var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};
            if(reference.Asset is {} saved) {var restore=IconButton("\uE7A7","恢复资料引用");restore.Click+=async (_,_)=>await Run(async ()=> {
                using var suspension=App.Work.SuspendCollection();var kind=Enum.Parse<StationKind>(saved.Kind);
                await store.Add([new(kind,saved.Value,saved.Title)]);var entry=store.Items.First(i=>i.Kind==kind&&i.Value.Equals(saved.Value,kind is StationKind.File or StationKind.Folder?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal));
                await App.Work.Store.ReplaceStationReference(reference.Id,entry.Id);
            });actions.Children.Add(restore);}
            var unlink=IconButton("\uE74D","解除失效关联");unlink.Click+=async (_,_)=>await Run(()=>Remember("已移出分组关联",()=>App.Work.Store.Unlink(scene.Id,reference)));actions.Children.Add(unlink);row.Children.Add(actions);cards.Children.Add(PanelTheme.Card(row));
        }
        return missing.Length>0;
    }
    async Task LinkTaskMaterials(Guid task)
    {
        if(Scene() is not {} scene) return;
        var current=(App.WorkStore.Data.TaskAssets??[]).FirstOrDefault(l=>l.TaskId==task)?.StationIds??[];
        var rows=WorkspaceItems().Select(i=>new CheckBox { Content=new TextBlock {Text=DisplayTitle(i),TextWrapping=TextWrapping.Wrap,MaxWidth=300},Tag=i.Id,IsChecked=current.Contains(i.Id) }).ToArray();
        var list=new StackPanel { Spacing=6 }; foreach(var row in rows) list.Children.Add(row);
        if(await new ContentDialog {XamlRoot=root.XamlRoot,Title="关联这项任务的资料",Content=new ScrollViewer {Content=list,MaxHeight=350},PrimaryButtonText="保存关联",CloseButtonText="取消"}.ShowAsync()==ContentDialogResult.Primary)
            await App.Work.Store.LinkTaskAssets(task,rows.Where(r=>r.IsChecked==true).Select(r=>(Guid)r.Tag));
    }
    async Task CreateWorkspaceRecord()
    {
        if (Scene() is not { } scene) return;
        bool task = workspacePage == 1;
        var text = new TextBox { PlaceholderText = task ? "任务名称" : "记下内容", MaxLength = task ? 200 : 20000, AcceptsReturn = !task, TextWrapping = TextWrapping.Wrap };
        var dialog=new PanelEntryDialog(root.XamlRoot,task?"新建待办":"新建笔记","保存",text,[text],()=>string.IsNullOrWhiteSpace(text.Text)?"请输入内容":null,async ()=> {
            Guid id;
            if (task) { var before = App.Planner.Data.Tasks.Select(t=>t.Id).ToHashSet(); await App.Planner.AddTask(text.Text,"",null); id = App.Planner.Data.Tasks.First(t=>!before.Contains(t.Id)).Id; }
            else { id = Guid.NewGuid(); await App.Planner.SaveNote(id,text.Text); }
            await App.Work.Store.Link(scene.Id,[new(task ? WorkKind.Task : WorkKind.Note,id)]);
        });
        await dialog.Show();
    }
    async Task LinkWorkspaceRecords()
    {
        if (Scene() is not { } scene) return;
        var kind = workspacePage == 1 ? WorkKind.Task : WorkKind.Note;
        var rows = App.Work.All().Where(r=>r.Kind==kind&&!scene.Items.Any(i=>i.Kind==r.Kind&&i.Id==r.Id)).Select(r=>new CheckBox { Content = new TextBlock { Text = App.Work.Title(r), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 }, Tag = r }).ToArray();
        var panel = new StackPanel { Spacing = 6 }; foreach (var row in rows) panel.Children.Add(row);
        if (await new ContentDialog { XamlRoot = root.XamlRoot, Title = "关联已有内容", Content = new ScrollViewer { Content = panel, MaxHeight = 350 }, PrimaryButtonText = "加入所选", CloseButtonText = "取消" }.ShowAsync() == ContentDialogResult.Primary)
            await App.Work.Store.Link(scene.Id,rows.Where(r=>r.IsChecked==true).Select(r=>(WorkRef)r.Tag));
    }
}
