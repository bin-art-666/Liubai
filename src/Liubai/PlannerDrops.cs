using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace Liubai;

internal sealed partial class PlannerWindow
{
    readonly StackPanel noteImages = new() { Spacing = 8 };
    void InitializeDrops(FrameworkElement root)
    {
        root.AllowDrop = true;
        root.DragOver += (_, e) =>
        {
            e.Handled = true; e.AcceptedOperation = !busy && !store.ReadOnly && DropPayload.Supported(e.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None;
            e.DragUIOverride.Caption = page == PlannerPage.Tasks ? "文字存为待办" : "保存为图文记录"; e.DragUIOverride.IsCaptionVisible = true;
        };
        root.Drop += async (_, e) =>
        {
            e.Handled = true; var deferral = e.GetDeferral();
            try { e.AcceptedOperation = await ReceiveDrop(e.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None; }
            finally { deferral.Complete(); }
        };
    }
    internal async Task<bool> ReceiveDrop(DataPackageView view)
    {
        if (busy || destroyed || store.ReadOnly || ModalActive) return false;
        bool accepted = false;
        await Run(async () =>
        {
            using var lease = KeepOpen(); if (!await FlushAsync()) return;
            if (page == PlannerPage.Tasks)
            {
                string text = await DropPayload.ReadText(view);
                string title = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First().Trim();
                // Import a task without touching an unfinished composer draft.
                await store.ImportTask(title[..Math.Min(200, title.Length)], text);
                taskFilter.SelectedIndex = 2; taskSearch.Text = ""; status.Text = "文字已添加为待办，原草稿保留。";
            }
            else
            {
                using var payload = await DropPayload.Read(view);
                if (payload.Files.Length == 0 && payload.Bitmap is null && string.IsNullOrWhiteSpace(payload.Text)) throw new IOException("没有可保存的内容。");
                Guid id = await WorkImages.CreateNote(payload.Text, payload.Files, payload.Bitmap);
                LoadNote(id); status.Text = "图文记录已保存到本机。";
            }
            await App.Work.Flush(); if (App.Work.LastError != null) status.Text += " 现场关联未完成，请到工作现场重试加入已有内容。";
            RefreshLists(); accepted = true;
        });
        return accepted;
    }
    void RefreshNoteImages()
    {
        noteImages.Children.Clear(); if (noteId is not { } id) return;
        foreach (var attachment in App.Work.Store.Data.Images.Where(i => i.NoteId == id))
        {
            if (!WorkImages.Managed(attachment.Path) || !File.Exists(attachment.Path)) { noteImages.Children.Add(Hint("图片附件不可用，文字记录仍保留。")); continue; }
            var image = new Image { MaxHeight = 220, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left,
                Source = new BitmapImage(new Uri(attachment.Path)) { DecodePixelWidth = 700 } };
            ToolTipService.SetToolTip(image, "图片已保存在本机，原文件移动不影响此记录"); noteImages.Children.Add(image);
        }
    }
}
