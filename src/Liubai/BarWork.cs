namespace Liubai;

public sealed partial class BarWindow
{
    WorkSceneWindow? workSceneWindow;
    FocusWindow? focusWindow;
    void OpenWorkScene()
    {
        if (closing || PanelModal || manager?.HasModal == true) return;
        HidePanels(false); manager?.HideOtherPanels(this);
        workSceneWindow ??= new WorkSceneWindow(OpenWorkItem);
        workSceneWindow.Open(Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    async Task OpenWorkItem(WorkRef item)
    {
        if (item.Kind == WorkKind.Station)
        {
            var entry = App.Station.Items.FirstOrDefault(s => s.Id == item.Id) ?? (item.Asset is { } asset ? new StationItem(item.Id, Enum.Parse<StationKind>(asset.Kind), asset.Value, asset.Title, DateTimeOffset.Now)
                : throw new IOException("该暂存项已移除。"));
            if (entry.Kind is StationKind.File or StationKind.Folder)
            {
                var storage = await StationTransfer.Storage(entry);
                bool success = storage is Windows.Storage.StorageFolder folder ? await Windows.System.Launcher.LaunchFolderAsync(folder) : await Windows.System.Launcher.LaunchFileAsync((Windows.Storage.StorageFile)storage);
                if (!success) throw new IOException("无法打开原位置，请检查文件是否已移动。");
            }
            else if (entry.Kind == StationKind.Link) { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(entry.Value))) throw new IOException("无法打开链接。"); }
            else { var package = new Windows.ApplicationModel.DataTransfer.DataPackage(); package.SetText(entry.Value); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); }
            return;
        }
        plannerWindow ??= manager?.GetPlanner(Display) ?? new PlannerWindow(App.Planner, Display);
        await plannerWindow.OpenWorkItem(item, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top);
    }
    void HideUtilityPanels() { moreWindow?.Hide(); moreHover?.Stop(); workSceneWindow?.Hide(); focusWindow?.Hide(); screenshotPreview?.Hide(); FinishPeek(); }
}
