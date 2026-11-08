using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal event Action? SearchRequested;
    UnifiedSearchView? embeddedSearch;
    internal UnifiedSearchView? SearchPage=>embeddedSearch;
    internal bool SearchActive=>embeddedSearch?.IsOpen==true;
    double searchReturnOffset,searchReturnGroups;
    internal void OpenSearch(Func<SearchHit,Task> open)
    {
        if(!IsOpen||SearchActive||busy||ModalActive)return;
        CloseQuickLook();searchReturnOffset=browserScroll.VerticalOffset;searchReturnGroups=workspaceScroller.HorizontalOffset;
        if(embeddedSearch==null)
        {
            embeddedSearch=new UnifiedSearchView(async hit=>{
                CloseSearch(false);
                try{await open(hit);}catch{if(IsOpen)OpenSearch(open);throw;}
            },()=>CloseSearch());
            Grid.SetRow(embeddedSearch,1);Grid.SetRowSpan(embeddedSearch,3);root.Children.Add(embeddedSearch);
        }
        foreach(var child in root.Children.OfType<FrameworkElement>().Where(e=>e!=stationHeader&&e!=embeddedSearch))child.Visibility=Visibility.Collapsed;
        embeddedSearch.Visibility=Visibility.Visible;root.AllowDrop=false;embeddedSearch.Open();UpdateStationHeader();UpdateFloatingCaption();
    }
    internal void CloseSearch(bool restore=true)
    {
        if(!SearchActive)return;
        embeddedSearch!.Stop();embeddedSearch.Visibility=Visibility.Collapsed;
        foreach(var child in root.Children.OfType<FrameworkElement>().Where(e=>e!=stationHeader&&e!=embeddedSearch))child.Visibility=Visibility.Visible;
        root.AllowDrop=true;UpdateStationHeader();UpdateBrowserMode();UpdateFloatingCaption();
        if(restore)DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{if(IsOpen&&!SearchActive){root.UpdateLayout();browserScroll.ChangeView(null,searchReturnOffset,null,true);workspaceScroller.ChangeView(searchReturnGroups,null,null,true);var button=stationCommands.Visibility==Visibility.Visible?stationCommands.Children.OfType<Button>().First():stationCommandMore;button.Focus(FocusState.Programmatic);}});
    }
    internal void RevealItem(Guid id)
    {
        revealWorkspaceLabel=true;
        workspaceId=null;ungrouped=false;workspacePage=0;expandedQuery=new();
        querySync=true;search.Text="";periodFilter.SelectedIndex=0;querySync=false;
        var items=CurrentItems();int index=Array.FindIndex(items,i=>i.Id==id);if(index<0)return;
        visibleItems=Math.Max(50,((index/50)+1)*50);ClearSelection();Refresh();SelectItem(id,false,false);
        DispatcherQueue.TryEnqueue(()=>{if(IsOpen&&selectionCards.TryGetValue(id,out var card)){card.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions {AnimationDesired=false});if(selectionGrips.TryGetValue(id,out var grip))grip.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);}});
    }
}
