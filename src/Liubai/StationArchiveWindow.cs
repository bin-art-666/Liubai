using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Liubai;

internal static class StationArchivePicker
{
    internal static async Task<string?> Pick(Window owner)
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
}

internal sealed partial class StationWindow
{
    readonly Button archive = new() { Content = "归档" };
    readonly Button cancelArchive = new() { Content = "取消归档", Visibility = Visibility.Collapsed };
    readonly ProgressBar archiveProgress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    CancellationTokenSource? archiveCancellation;

    internal async Task Archive()
    {
        if (busy || destroyed) return;
        var snapshot = CurrentItems(); if (snapshot.Length == 0) return;
        string scope = ActiveQuery.Description; bool filtered = ActiveQuery.Filtered;
        busy = true; using var lease = KeepOpen();
        string? completedDirectory = null;
        try
        {
            string directory = store.ArchiveDirectory;
            if (directory.Length == 0)
            {
                directory = await StationArchivePicker.Pick(this) ?? "";
                if (directory.Length == 0) return;
            }
            var selected = new TextBlock { Text = directory, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 350 };
            var change = new Button { Content = "更改保存位置" };
            change.Click += async (_, _) =>
            {
                change.IsEnabled = false;
                try { string? path = await StationArchivePicker.Pick(this); if (path != null) { directory = path; selected.Text = path; } }
                catch { status.Text = "选择目录失败，请重试。"; }
                finally { change.IsEnabled = true; }
            };
            var remove = new CheckBox { Content = "归档成功后移出中转站", IsChecked = store.RemoveAfterArchive && !store.ReadOnly, IsEnabled = !store.ReadOnly };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"归档当前 {snapshot.Length} 项。范围：{scope}。文件和文件夹会复制到归档目录，原文件不会删除。", TextWrapping = TextWrapping.Wrap, MaxWidth = 350 });
            panel.Children.Add(selected); panel.Children.Add(change); panel.Children.Add(remove);
            panel.Children.Add(new TextBlock { Text = "仅移出复制及校验成功的记录；失败项保留。取消归档时，全部记录保留。", TextWrapping = TextWrapping.Wrap, MaxWidth = 350, Opacity = .7 });
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = filtered ? "归档当前筛选结果？" : "归档全部项目？", Content = panel,
                PrimaryButtonText = "开始归档", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            bool removeSaved = remove.IsChecked == true;
            if (!store.ReadOnly) await store.UpdateOptions(store.Capacity, directory, removeSaved);
            archiveCancellation = new CancellationTokenSource();
            archive.IsEnabled = clear.IsEnabled = false; cancelArchive.Visibility = archiveProgress.Visibility = Visibility.Visible;
            archiveProgress.Value = 0;
            var progress = new Progress<ArchiveProgress>(p =>
            {
                if (destroyed) return;
                archiveProgress.Value = p.Total == 0 ? 0 : p.Completed * 100d / p.Total;
                status.Text = $"正在归档 {p.Completed} / {p.Total}：{p.Name}";
            });
            var token = archiveCancellation.Token;
            var result = await Task.Run(() => StationArchive.Save(snapshot, directory, progress, token));
            completedDirectory = result.Directory;
            if (!destroyed) cancelArchive.IsEnabled = false;
            bool canceled = result.Canceled || token.IsCancellationRequested;
            int removed = 0;
            if (!canceled && !destroyed && removeSaved)
                removed = await store.RemoveArchived(result.Saved.Select(s => s.Item));
            if (!destroyed)
                status.Text = canceled ? $"已取消，原记录全部保留。未完成归档：{result.Directory}"
                    : $"已归档 {result.Saved.Length} 项，移出 {removed} 项" + (result.Failed.Length > 0 ? $"，失败 {result.Failed.Length} 项已保留。" : "。") + $"\n位置：{result.Directory}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!destroyed) status.Text = completedDirectory == null ? "归档未完成，原记录全部保留。请检查目录权限和磁盘空间后重试。"
                : $"归档文件已保存，但记录未能移出，请检查后重试。\n位置：{completedDirectory}";
        }
        finally
        {
            archiveCancellation?.Dispose(); archiveCancellation = null; busy = false;
            if (!destroyed) { cancelArchive.IsEnabled = true; cancelArchive.Visibility = archiveProgress.Visibility = Visibility.Collapsed; Refresh(); }
        }
    }
}
