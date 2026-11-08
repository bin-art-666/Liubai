namespace Liubai;

public sealed partial class BarWindow
{
    void OpenUnifiedSearch()
    {
        if(closing||PanelModal||manager?.HasModal==true)return;
        if(stationWindow?.IsOpen!=true&&!OpenStation())return;
        stationWindow!.OpenSearch(OpenSearchHit);
    }
    async Task OpenSearchHit(SearchHit hit)
    {
        switch(hit.Kind) {
            case SearchKind.Group:
                if(!App.WorkStore.Data.Scenes.Any(s=>s.Id==hit.Id))throw new IOException("分组已移除");
                if(!OpenStation())throw new IOException("中转站暂不可用");await App.Work.Store.Activate(hit.Id);stationWindow!.OpenWorkspace(hit.Id);break;
            case SearchKind.Item:
                if(!App.Station.Items.Any(i=>i.Id==hit.Id))throw new IOException("记录已移除");
                if(!OpenStation())throw new IOException("中转站暂不可用");await App.Work.Store.Activate(null);stationWindow!.RevealItem(hit.Id);break;
            default:
                if(hit.Kind==SearchKind.Task?!App.Planner.Data.Tasks.Any(t=>t.Id==hit.Id):!App.Planner.Data.Notes.Any(n=>n.Id==hit.Id))throw new IOException("记录已移除");
                HidePanels(false);manager?.HideOtherPanels(this);await OpenWorkItem(new(hit.Kind==SearchKind.Task?WorkKind.Task:WorkKind.Note,hit.Id));break;
        }
    }
}
