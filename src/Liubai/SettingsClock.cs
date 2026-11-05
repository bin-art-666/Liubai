using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    Slider clockFontSize = null!;
    readonly TextBlock clockFontSample = new()
    {
        Text = "04:55", FontSize = AppearanceStore.DefaultClockFontSize, TextLineBounds = TextLineBounds.Tight,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
    };
    Expander BuildClockSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        clockFontSize = AddSlider(panel, "计时数字大小", "调整顶栏剩余时间及暂停符号的大小，默认 18。",
            AppearanceStore.MinimumClockFontSize, AppearanceStore.MaximumClockFontSize, " px", PreviewClockFont);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(clockFontSample, FontNumeralAlignment.Tabular);
        panel.Children.Add(new Border { Child = clockFontSample, Height = 40, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128)) });
        var reset = new Button { Content = "恢复默认字号" };
        reset.Click += (_, _) => clockFontSize.Value = AppearanceStore.DefaultClockFontSize;
        panel.Children.Add(reset);
        return new Expander { Header = "时钟设置", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void PreviewClockFont()
    {
        if (clockFontSize == null) return;
        clockFontSample.FontSize = clockFontSize.Value;
        if (syncing || !interactive) return;
        preferences.SetClockFontSize((int)Math.Round(clockFontSize.Value));
        dirty = true; status.Text = "正在预览，稍后自动保存…";
        saveTimer.Stop(); saveTimer.Start();
    }
}
