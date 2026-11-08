using Microsoft.UI.Xaml;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifySearchReturn()
    {
        var bounds=(AppWindow.Position,AppWindow.Size);var item=store.Items.First();
        var original=store.Items.Select(i=>i.Id).ToHashSet();
        await store.Add(Enumerable.Range(0,12).Select(i=>StationStore.Text("返回位置验证 "+i)).ToArray());
        var temporary=store.Items.Where(i=>!original.Contains(i.Id)).ToArray();
        var groups=new List<Guid>();for(int i=0;i<5;i++)groups.Add(await App.WorkStore.Create("横向位置验证 "+i,false));
        await Task.Delay(180);root.UpdateLayout();browserScroll.ChangeView(null,160,null,true);ScrollWorkspaceLabels(120);await Task.Delay(100);
        SelectItem(item.Id,false,false);SelectItem(temporary[0].Id,true,false);var selected=selection.ToArray();Guid? group=workspaceId;
        bool pinned=Pinned,detached=Detached;double offset=browserScroll.VerticalOffset;
        double groupOffset=workspaceScroller.HorizontalOffset;
        if(offset<=0||groupOffset<=0)throw new Exception("Search return fixture not scrolled");
        OpenSearch(_=>Task.CompletedTask);await Task.Delay(120);
        if(!IsOpen||!SearchActive||AppWindow.Position!=bounds.Position||AppWindow.Size!=bounds.Size||Pinned!=pinned||Detached!=detached||searchReturn.Visibility!=Visibility.Visible||searchReturn.BorderThickness.Left!=1)throw new Exception("Search replaced window state or return button style");
        CloseSearch();await Task.Delay(120);
        if(!IsOpen||SearchActive||embeddedSearch?.VerifyReleased()!=true||workspaceId!=group||!selection.SetEquals(selected)||Math.Abs(browserScroll.VerticalOffset-offset)>1||Math.Abs(workspaceScroller.HorizontalOffset-groupOffset)>1)throw new Exception("Search return lost station state");
        OpenSearch(_=>Task.CompletedTask);Hide();
        if(IsOpen||SearchActive)throw new Exception("Outside/global hide reopened the station");
        Open(display,dockTop);pin.IsChecked=pinned;
        await store.RemoveArchived(temporary);foreach(var id in groups)await App.WorkStore.Remove(id);
        return ["Embedded search retains geometry/pin/selection; return restores original station and releases observers; hide closes whole panel"];
    }
}
