using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class AiWindow
{
    readonly Grid aiNavigation=new(){ColumnSpacing=8};
    readonly Border aiHeaderDivider=new(){Width=1,Height=22,Opacity=.25,Margin=new Thickness(6,0,6,0),VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Microsoft.UI.Colors.Gray),Visibility=Visibility.Collapsed};
    Grid aiHeader=null!;
    bool aiHeaderDetached;
    void InitializeAiHeaderLayout()
    {
        aiHeader.SizeChanged+=(_,_)=>UpdateAiHeaderLayout();
        aiNavigation.SizeChanged+=(_,_)=>UpdateFloatingCaption();
        PanelTheme.Changed+=UpdateAiHeaderLayout;
        Closed+=(_,_)=>PanelTheme.Changed-=UpdateAiHeaderLayout;
        UpdateAiHeaderLayout();
    }
    void UpdateAiHeaderLayout()
    {
        if(destroyed||aiHeader==null)return;
        if(aiHeaderDetached!=Detached)
        {
            if(Detached)
            {
                root.Children.Remove(aiNavigation);
                foreach(var child in root.Children.OfType<FrameworkElement>().Where(c=>c!=aiHeader))Grid.SetRow(child,Grid.GetRow(child)-1);
                root.RowDefinitions.RemoveAt(1);
                Grid.SetRow(aiNavigation,0);Grid.SetColumn(aiNavigation,1);aiHeader.Children.Add(aiNavigation);
            }
            else
            {
                aiHeader.Children.Remove(aiNavigation);
                foreach(var child in root.Children.OfType<FrameworkElement>().Where(c=>c!=aiHeader))Grid.SetRow(child,Grid.GetRow(child)+1);
                root.RowDefinitions.Insert(1,new(){Height=GridLength.Auto});
                Grid.SetRow(aiNavigation,1);Grid.SetColumn(aiNavigation,0);root.Children.Add(aiNavigation);
            }
            aiHeaderDetached=Detached;
        }
        aiHeader.ColumnDefinitions[0].Width=Detached?GridLength.Auto:new GridLength(1,GridUnitType.Star);
        aiHeader.ColumnDefinitions[1].Width=Detached?new GridLength(1,GridUnitType.Star):new GridLength(0);
        aiHeader.Children[0].SetValue(FrameworkElement.MaxWidthProperty,Detached?(aiHeader.ActualWidth<600?110d:180d):double.PositiveInfinity);
        aiHeaderDivider.Visibility=Detached?Visibility.Visible:Visibility.Collapsed;
        double total=providerLabels.Children.OfType<FrameworkElement>().Sum(b=>b.Width+providerLabels.Spacing)+providerMore.Width+providerHost.ColumnSpacing+(back.Visibility==Visibility.Visible?back.Width+8:0);
        aiNavigation.MaxWidth=Detached?Math.Max(60,total):double.PositiveInfinity;
        aiNavigation.HorizontalAlignment=Detached?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        QueueProviderReveal();UpdateFloatingCaption();
    }
}
