using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class StationWindow
{
    const string FolderPath="M 2 6 L 2 15 C 2 16.7 3.3 18 5 18 L 15 18 C 16.7 18 18 16.7 18 15 L 18 7 C 18 5.9 17.1 5 16 5 L 9 5 L 7 3 L 4 3 C 2.9 3 2 3.9 2 5 L 2 6 L 18 6";
    const string GroupPath="M 2 7 L 2 14 C 2 15.7 3.3 17 5 17 L 9 17 M 2 7 L 17 7 L 17 9 M 2 7 L 2 5 C 2 3.9 2.9 3 4 3 L 7 3 L 9 5 L 15 5 C 16.1 5 17 5.9 17 7 M 19 14 C 19 16.8 16.8 19 14 19 C 11.2 19 9 16.8 9 14 C 9 11.2 11.2 9 14 9 C 16.8 9 19 11.2 19 14 Z M 14 11.5 L 14 16.5 M 11.5 14 L 16.5 14";
    static void SetStationIcon(ButtonBase button,string glyph)
    {
        string? data=glyph switch {
            "\uE721"=>"M 14.5 8.5 C 14.5 11.8 11.8 14.5 8.5 14.5 C 5.2 14.5 2.5 11.8 2.5 8.5 C 2.5 5.2 5.2 2.5 8.5 2.5 C 11.8 2.5 14.5 5.2 14.5 8.5 Z M 13 13 L 18 18",
            "\uE740"=>"M 3 11 L 3 17 L 9 17 M 3 17 L 17 3 M 11 3 L 17 3 L 17 9",
            "\uE73F"=>"M 2 9 L 8 9 L 8 3 M 2 15 L 8 9 M 12 17 L 12 11 L 18 11 M 12 11 L 18 5",
            "\uE718" or "\uE77A"=>"M 12 2 L 18 8 L 14 10 L 12 14 L 9 11 L 4 16 M 9 11 L 6 8 L 10 6 L 12 2",
            "\uE8BB"=>"M 3 3 L 17 17 M 17 3 L 3 17",
            "\uE710"=>"M 10 3 L 10 17 M 3 10 L 17 10",
            "\uE712"=>"M 4 10 L 4.1 10 M 10 10 L 10.1 10 M 16 10 L 16.1 10",
            "\uE8B7"=>FolderPath,
            "\uE8F4"=>GroupPath,
            "\uE8B5"=>"M 2 8 L 2 5 C 2 3.9 2.9 3 4 3 L 7 3 L 9 5 L 16 5 C 17.1 5 18 5.9 18 7 L 18 15 C 18 16.7 16.7 18 15 18 L 10 18 M 2 12 L 11 12 M 7 8 L 11 12 L 7 16",
            "\uE8DE"=>"M 3 3 L 10 3 C 11.1 3 12 3.9 12 5 L 12 8 M 12 15 L 12 17 C 12 18.1 11.1 19 10 19 L 3 19 C 1.9 19 1 18.1 1 17 L 1 5 C 1 3.9 1.9 3 3 3 Z M 8 11 L 19 11 M 15 7 L 19 11 L 15 15",
            "\uE946"=>"M 18 10 C 18 14.4 14.4 18 10 18 C 5.6 18 2 14.4 2 10 C 2 5.6 5.6 2 10 2 C 14.4 2 18 5.6 18 10 Z M 10 9 L 10 14 M 10 6 L 10 6.1",
            "\uE70F"=>"M 12 4 L 16 8 M 4 12 L 14 2 L 18 6 L 8 16 L 3 17 L 4 12 M 11 18 L 18 18",
            "\uE74D"=>TrashPath,
            "\uE916"=>"M 6 2 L 14 2 M 10 2 L 10 5 M 16 5 L 18 3 M 10 8 L 10 12 L 13 12 M 17 12 C 17 15.9 13.9 19 10 19 C 6.1 19 3 15.9 3 12 C 3 8.1 6.1 5 10 5 C 13.9 5 17 8.1 17 12 Z",
            "\uE7A7"=>"M 7 4 L 2 9 L 7 14 M 2 9 L 12 9 C 16 9 18 12 18 16",
            _=>null
        };
        if(data==null) {button.Content=Glyph(glyph);return;}
        SetStationPath(button,data);
        if(glyph is "\uE8BB" or "\uE74D") DangerAction(button);
    }
    static void DangerAction(ButtonBase button)
    {
        // Native templates retain disabled/focus/pressed behavior; only hover is red.
        foreach(string prefix in new[]{"Button","ToggleButton"})
        {
            button.Resources[prefix+"ForegroundPointerOver"]=new SolidColorBrush(Windows.UI.Color.FromArgb(255,196,43,53));
            button.Resources[prefix+"BackgroundPointerOver"]=new SolidColorBrush(Windows.UI.Color.FromArgb(28,232,69,80));
        }
    }
}
