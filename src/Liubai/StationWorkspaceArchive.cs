using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Security.Cryptography;

namespace Liubai;

internal sealed partial class StationWindow
{
    async Task ArchiveWorkspace()
    {
        if (Scene() is not { } scene || busy) return;
        await Run(async () =>
        {
            string? directory = store.ArchiveDirectory.Length == 0 ? await StationArchivePicker.Pick(this) : store.ArchiveDirectory;
            if (directory == null) return;
            if (await new ContentDialog { XamlRoot = root.XamlRoot, Title = "归档整个工作分组？", Content = $"{scene.Name}\n资料、待办、笔记、进度和专注记录将复制到：\n{directory}\n成功后保留当前分组。", PrimaryButtonText = "归档", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close }.ShowAsync() != ContentDialogResult.Primary) return;
            archiveCancellation = new CancellationTokenSource(); cancelArchive.Visibility = archiveProgress.Visibility = Visibility.Visible;
            try
            {
                var progress = new Progress<ArchiveProgress>(p=> { archiveProgress.Value = p.Total == 0 ? 0 : p.Completed*100d/p.Total; status.Text=$"归档 {p.Completed}/{p.Total}：{p.Name}"; });
                string path = await WorkArchive.Save(scene,store,App.Planner,App.Work.Store,directory,progress,archiveCancellation.Token);
                status.Text = "分组已归档，原记录保留。\n"+path;
            }
            catch (OperationCanceledException) { status.Text="归档已取消，原分组保留。未完成目录不可导入。"; }
            finally { archiveCancellation.Dispose(); archiveCancellation=null; cancelArchive.Visibility=archiveProgress.Visibility=Visibility.Collapsed; }
        });
    }
    async Task ImportWorkspace()
    {
        string? rootDirectory = await StationArchivePicker.Pick(this); if (rootDirectory == null) return;
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(rootDirectory)).StartsWith("未完成_",StringComparison.Ordinal)) throw new IOException("不能导入未完成归档。");
        status.Text="正在校验分组归档…";
        var package = await WorkArchive.Read(rootDirectory);
        if (await new ContentDialog { XamlRoot=root.XamlRoot,Title="导入工作分组？",Content=$"{package.Name}\n{package.Items.Length} 份资料、{package.Tasks.Length} 项待办、{package.Notes.Length} 条笔记。\n创建新分组；任务提醒保持关闭。",PrimaryButtonText="导入",CloseButtonText="取消" }.ShowAsync()!=ContentDialogResult.Primary) return;
        using var collection=App.Work.SuspendCollection();
        workspaceId=await WorkArchiveImport.Import(rootDirectory,package,store,App.Planner,App.Work.Store);
        workspacePage=0; ungrouped=false; ClearSelection(); Refresh(); status.Text="分组已导入，提醒关闭，文件副本保存在本机。";
    }
}
