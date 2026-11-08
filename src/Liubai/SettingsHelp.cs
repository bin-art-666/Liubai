using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    Expander BuildHelpSettings()
    {
        var help=new Expander {Header="帮助",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        help.Expanding+=(_,_)=>{
            if(help.Content!=null)return;
            var station=new Expander {Header="中转站",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            station.Expanding+=(_,_)=>station.Content??=StationHelpContent();
            help.Content=station;
        };
        return help;
    }
    static StackPanel StationHelpContent()
    {
        var body=new StackPanel {Spacing=14};
        void Section(string title,string text)
        {
            var group=new StackPanel {Spacing=6};group.Children.Add(new TextBlock {Text=title,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
            group.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,Opacity=.85});body.Children.Add(group);
        }
        Section("存入与取出","将文件、图片、文字或链接拖入中转站，也可拖到顶栏图标。拖动卡片内容可交给其他窗口；也可点击打开、复制或所在文件夹。存入后保留在中转站，拖出不会自动移除。");
        Section("分组管理","点击分组标签立即切换，鼠标在标签区域滚动可左右浏览，触控板也支持横向滑动；省略号保留未完整显示的分组。浏览不会改变选择或排序，菜单选中后标签自动滚入视区。新建按钮创建分组，右键标签可改名、置顶、调整顺序或解散；省略号中也可管理当前分组。解散只解除关联，资料、待办和笔记保留。");
        Section("多选与批量操作","点击卡片选择，Ctrl＋点击增减选择，Shift＋点击连续选择。非输入区按 Ctrl+A 选择当前结果，Esc 取消选择。选中后可以批量分组、复制、归档、移除或拖出。右侧卡片按钮的精简／六宫格样式可在中转站设置中调整。");
        Section("预览与搜索","选中一项后按空格快速预览，再按空格或 Esc 收起。支持图片、文字、PDF 首页等，不支持的文件显示缩略图或信息。搜索按钮可查资料、分组、待办和笔记，点击结果定位原内容；非输入区也可按 Ctrl+F。搜索在中转站内部展开，点叉号或 Esc 返回原分组、选择和浏览位置；点击外部按固定状态处理整个面板。");
        Section("归档与导入","归档按钮将当前范围或所选项目保存到指定文件夹；有分组时可归档整个分组。导入按钮可恢复留白导出的分组归档。归档前会确认范围，保存位置与归档后是否移出可在中转站设置中调整。");
        Section("保存与原文件","记录自动保存。普通文件保存原位置引用，移动或删除原文件后需要重新定位；没有本地路径的虚拟附件会自动保存到本机。移除记录和清空不会删除原文件。默认最多保存 200 项，可在中转站设置中调整容量。");
        Section("撤销与快捷键","移除、清空或解除分组关联后，可点击短暂出现的撤销按钮；非输入区按 Ctrl+Z 可撤销本次运行内最近的操作，最多 10 次，以提示为准。输入框保留自身撤销。单行弹窗 Enter 提交、Esc 取消；多行输入 Enter 换行、Ctrl+Enter 保存。");
        Section("窗口与按钮","第一行依次提供搜索、清空、归档、放大／还原、固定、关闭。窗口较窄时，前三项收到“中转站操作”菜单。固定后点击外部不会收起，独立窗口可拖动和调整大小。停靠面板可拖动四边，松开保存尺寸，拖动时 Esc 取消。");
        return body;
    }
}
