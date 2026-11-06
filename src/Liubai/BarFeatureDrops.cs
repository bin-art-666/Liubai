using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

public sealed partial class BarWindow
{
    bool featureDropBusy;
    void InitializeFeatureDrops()
    {
        foreach (var button in new[] { todo, quickNote, aiButton })
        {
            button.AllowDrop = true;
            button.DragOver += (_, e) =>
            {
                e.Handled = true;
                e.AcceptedOperation = !featureDropBusy && !closing && !PanelModal && manager?.HasModal != true && DropPayload.Supported(e.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None;
                e.DragUIOverride.Caption = button == todo ? "文字添加到待办" : button == quickNote ? "保存为图文记录" : "暂存到 AI，确认后再交给网站";
                e.DragUIOverride.IsCaptionVisible = true;
                if (button == aiButton && e.AcceptedOperation == DataPackageOperation.Copy) SetPanelHighlight(aiButton, true);
            };
            button.DragLeave += (_, _) => { if (button == aiButton) RefreshPanelHighlights(); };
            button.Drop += async (_, e) =>
            {
                e.Handled = true; var deferral = e.GetDeferral(); bool ownsDrop = false; e.AcceptedOperation = DataPackageOperation.None;
                try
                {
                    if (featureDropBusy || closing || PanelModal || manager?.HasModal == true) return;
                    featureDropBusy = true;
                    ownsDrop = true;
                    if (button == aiButton) { await OpenAiPanel(); e.AcceptedOperation = await App.Ai.ReceiveDrop(e.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None; }
                    else
                    {
                        var panel = await OpenImportPlanner(button == todo ? PlannerPage.Tasks : PlannerPage.Notes);
                        bool saved = await panel.ReceiveDrop(e.DataView); e.AcceptedOperation = saved ? DataPackageOperation.Copy : DataPackageOperation.None;
                        if (saved) panel.ConfirmIncomingDrop();
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { ShowScreenshotResult("拖入未完成：" + ex.Message); e.AcceptedOperation = DataPackageOperation.None; }
                finally { if (ownsDrop) featureDropBusy = false; deferral.Complete(); RefreshPanelHighlights(); }
            };
        }
    }
    async Task<PlannerWindow> OpenImportPlanner(PlannerPage page)
    {
        if (closing || PanelModal || manager?.HasModal == true) throw new IOException("请先完成当前面板中的操作。");
        HidePanels(false); manager?.HideOtherPanels(this);
        plannerWindow ??= manager?.GetPlanner(Display) ?? new PlannerWindow(App.Planner, Display);
        await plannerWindow.Open(page, Display, windowShown ? AppWindow.Position.Y + AppWindow.Size.Height : Display.Info.Work.Top); return plannerWindow;
    }
}
