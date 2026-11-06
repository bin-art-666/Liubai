using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    TextBlock updateStatus = null!, updateNotes = null!, updateHistory = null!;
    Button checkUpdate = null!, installUpdate = null!, rollbackUpdate = null!, cancelUpdate = null!;
    string? previousUpdateVersion;
    Expander BuildUpdateSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "留白 " + UpdateService.VersionLabel, FontSize = 18 });
        panel.Children.Add(Hint("本机构建：" + File.GetLastWriteTime(Path.Combine(AppContext.BaseDirectory, "Liubai.dll")).ToString("yyyy-MM-dd HH:mm")));
        updateStatus = Hint(App.Updates.Status); panel.Children.Add(updateStatus);
        checkUpdate = new Button { Content = "检查更新", HorizontalAlignment = HorizontalAlignment.Stretch };
        installUpdate = new Button { Content = "下载更新并重启", HorizontalAlignment = HorizontalAlignment.Stretch };
        rollbackUpdate = new Button { Content = "回退上一版本", HorizontalAlignment = HorizontalAlignment.Stretch };
        cancelUpdate = new Button { Content = "取消下载或检查", Visibility = Visibility.Collapsed };
        foreach (var button in new[] { checkUpdate, installUpdate, rollbackUpdate, cancelUpdate }) panel.Children.Add(button);
        checkUpdate.Click += async (_, _) => { using var lease = KeepOpen(); await App.Updates.Check(); };
        cancelUpdate.Click += (_, _) => App.Updates.Cancel();
        installUpdate.Click += async (_, _) => await RunMaintenance(false);
        rollbackUpdate.Click += async (_, _) => await RunMaintenance(true);
        updateNotes = Hint("检查后在这里显示更新说明。"); updateNotes.IsTextSelectionEnabled = true;
        panel.Children.Add(new Expander { Header = "更新说明", Content = updateNotes, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var release = new Button { Content = "打开 GitHub 发布页" };
        release.Click += (_, _) => OpenMaintenancePath(UpdatePolicy.Repository + "/releases"); panel.Children.Add(release);
        var folder = new Button { Content = "打开维护与备份目录" };
        folder.Click += (_, _) => { try { App.Updates.Layout.EnsureOwned(); OpenMaintenancePath(App.Updates.Layout.Root); } catch { updateStatus.Text = "维护目录不可写，请检查程序目录权限。"; } }; panel.Children.Add(folder);
        var logs = new Button { Content = "打开本机数据与日志目录" };
        logs.Click += (_, _) => OpenMaintenancePath(App.DataDirectory); panel.Children.Add(logs);
        updateHistory = Hint(App.Updates.LastResult()); panel.Children.Add(updateHistory);
        panel.Children.Add(Hint("仅从留白 GitHub 正式发布下载 Windows x64 包，并校验 SHA-256。升级前先保存和备份配置，保留一份程序回退副本；安装失败恢复原程序。回退只替换程序，不覆盖当前待办、笔记和中转站数据。配置备份保存在维护目录，不包含网页登录资料或外部原文件。"));
        panel.Children.Add(Hint("有运行中或暂停的计时／闹钟时暂不重启。升级后摄像头守护仍默认关闭；AI 网页会话会随程序重启，未发送草稿由网站决定能否恢复。程序目录需要可写，更新不会请求管理员权限。"));
        App.Updates.Changed += RefreshUpdates;
        Closed += (_, _) => App.Updates.Changed -= RefreshUpdates;
        RefreshUpdates();
        return new Expander { Header = "版本、更新与恢复", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    void OpenMaintenancePath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { updateStatus.Text = "无法打开，请检查系统默认应用或目录权限。"; }
    }
    void RefreshUpdates()
    {
        if (updateStatus == null) return;
        updateStatus.Text = App.Updates.Status; checkUpdate.IsEnabled = !App.Updates.Busy;
        installUpdate.IsEnabled = App.Updates.CanInstall;
        if (!App.Updates.Busy) previousUpdateVersion = App.Updates.PreviousVersion;
        rollbackUpdate.IsEnabled = !App.Updates.Busy && previousUpdateVersion != null;
        cancelUpdate.Visibility = App.Updates.CanCancel ? Visibility.Visible : Visibility.Collapsed;
        updateNotes.Text = App.Updates.Release?.Notes is { Length: > 0 } notes ? notes : "尚无更新说明。";
        AutomationProperties.SetName(rollbackUpdate, previousUpdateVersion is { } version ? "回退至 " + version : "没有可用的回退副本");
    }
    async Task RunMaintenance(bool rollback)
    {
        if (App.Updates.Busy) return;
        UpdatePlan? plan = null;
        using (KeepOpen())
        {
            if (App.Clock.HasPending) { updateStatus.Text = "请先完成或取消当前计时和闹钟，再更新或回退。"; return; }
            var dialog = new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot,
                Title = rollback ? "回退上一版本并重启？" : "下载更新并重启？",
                Content = rollback ? "将保存当前数据后替换程序。待办、笔记和中转站保留当前内容，旧程序如不支持新数据会停止写入。"
                    : "将从 GitHub 下载并校验更新，保存全部设置和记录，再备份配置、替换程序并重启。AI 页面会关闭，请先处理未发送内容。",
                PrimaryButtonText = rollback ? "保存并回退" : "下载并安装", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (!await SaveAllSettings()) { updateStatus.Text = "设置尚未保存成功，操作已停止。"; return; }
            plan = await App.Updates.Prepare(rollback);
        }
        if (plan == null) return;
        try
        {
            if (!await App.ApplyMaintenance(plan)) { App.Updates.Abandon(plan); updateStatus.Text = "当前有未保存内容、计时或操作中的面板，请处理后重试。当前程序未替换。"; }
        }
        catch (Exception ex) { updateStatus.Text = "重启更新未完成：" + ex.Message; }
    }
}
