using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task VerifyOutboundLease(Func<bool> targetAllowed, Func<Task> transfer)
    {
        Open(display, display.Info.Work.Top);
        using (KeepOpenForDrag())
        {
            if (ModalActive || !DragActive || !targetAllowed()) throw new Exception("Source drag incorrectly blocks target");
            Hide(); if (!IsOpen) throw new Exception("Source disappeared while dragging");
            using (KeepOpen()) if (!ModalActive || targetAllowed()) throw new Exception("Real modal operation not protected during drag");
            await transfer();
            if (!IsOpen || ModalActive) throw new Exception("Transfer invalidated source drag protection");
        }
        if (DragActive || ModalActive) throw new Exception("Drag lease leaked");
        Hide(); if (IsOpen) throw new Exception("Panel cannot hide after drag");
    }
}

internal sealed partial class AiWindow
{
    internal async Task VerifyStationInbox(DataPackageView view, string sourceFile)
    {
        if (!verification) throw new InvalidOperationException("Local fixtures only");
        async Task Idle() { var until = DateTime.UtcNow.AddSeconds(3); while (ModalActive) { if (DateTime.UtcNow > until) throw new Exception("Drop lease leak"); await Task.Delay(30); } }
        await Idle(); ClearDropInbox();
        if (!await ReceiveDrop(view) || pendingDrop?.Files.Single() != sourceFile) throw new Exception("Station payload did not reach AI inbox");
        if (!File.Exists(sourceFile)) throw new Exception("Source file lost");
        var original = pendingDrop;
        await Idle();
        var incoming = new DataPackage(); incoming.SetText("第二次拖入");
        var operation = ReceiveDrop(incoming.GetView()); await Task.Delay(120);
        static IEnumerable<ContentDialog> Dialogs(DependencyObject parent)
        {
            if (parent is ContentDialog d) yield return d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Dialogs(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(p => p.Child is { } c ? Dialogs(c) : []).Single();
        if (!Equals(dialog.Title, "已有待发送内容") || dialog.DefaultButton != ContentDialogButton.Close || !dialog.IsPrimaryButtonEnabled) throw new Exception("Append/replace/cancel dialog missing");
        dialog.Hide();
        if (await operation || !ReferenceEquals(original, pendingDrop) || pendingDrop.Files.Length != 1) throw new Exception("Cancel replaced original staged file");
        StageDrop(new DropPayload { Text = "追加说明", Files = [sourceFile] }, append: true); RenderDropInbox();
        if (pendingDrop.Files.Length != 1 || pendingDrop.Text != "追加说明") throw new Exception("Append did not deduplicate files or retain text");
        var tooMany = new DropPayload { Files = Enumerable.Range(0, 13).Select(i => sourceFile + i).ToArray() };
        bool rejected = false; try { StageDrop(tooMany, append: true); } catch (IOException) { rejected = true; } finally { tooMany.Dispose(); }
        if (!rejected || pendingDrop.Files.Length != 1 || pendingDrop.Text != "追加说明") throw new Exception("Capacity failure changed existing pending data");
        StageDrop(new DropPayload { Text = "替换后的文字" }, append: false); RenderDropInbox();
        if (pendingDrop?.Text != "替换后的文字" || pendingDrop.Files.Length != 0 || !File.Exists(sourceFile)) throw new Exception("Replace changed source file");
        await Idle();
        string missing = Path.Combine(directory, "missing-station-file.png"); File.Copy(sourceFile, missing);
        var missingPackage = new DataPackage(); missingPackage.SetStorageItems(new[] { await StorageFile.GetFileFromPathAsync(missing) }); File.Delete(missing);
        rejected = false; try { await ReceiveDrop(missingPackage.GetView()); } catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { rejected = true; }
        if (!rejected || pendingDrop?.Text != "替换后的文字") throw new Exception("Missing file replaced original pending text");
        var folderPackage = new DataPackage(); folderPackage.SetStorageItems(new[] { await StorageFolder.GetFolderFromPathAsync(directory) });
        rejected = false; try { await ReceiveDrop(folderPackage.GetView()); } catch (IOException) { rejected = true; }
        if (!rejected || pendingDrop?.Text != "替换后的文字") throw new Exception("Folder replaced pending data");
        string first = Path.Combine(directory, "owned-first.tmp"), second = Path.Combine(directory, "owned-second.tmp");
        File.WriteAllText(first, "temporary"); File.WriteAllText(second, "temporary");
        var a = new DropPayload { Files = [first] }; a.OwnFile(first);
        var b = new DropPayload { Files = [second] }; b.OwnFile(second);
        StageDrop(a, append: false); StageDrop(b, append: true); ClearDropInbox();
        if (File.Exists(first) || File.Exists(second) || !File.Exists(sourceFile)) throw new Exception("Merged payload disposal lost ownership boundaries");
    }
}
