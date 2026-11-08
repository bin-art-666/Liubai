using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class PlannerWindow
{
    void AddGroupLinks(StackPanel parent, WorkKind kind, Guid id)
    {
        foreach(var group in App.WorkStore.Data.Scenes.Where(s=>s.Items.Any(i=>i.Kind==kind&&i.Id==id)))
        {
            var link=new HyperlinkButton {Content=group.Name,Padding=new Thickness(0),FontSize=12,HorizontalAlignment=HorizontalAlignment.Left};
            link.Click+=(_,_)=>App.RequestWorkspace(group.Id,display.Device); parent.Children.Add(link);
        }
    }
}
