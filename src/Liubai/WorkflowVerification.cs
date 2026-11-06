using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyWorkflow()
    {
        string result = "PASS", error = ""; var checks = new List<string>();
        PlannerWindow? planner = null; AiWindow? ai = null; PeekWindow? peek = null; ScreenshotPreviewWindow? preview = null; FocusWindow? focus = null; WorkSceneWindow? sceneWindow = null;
        AiOptionsStore? aiOptions = null;
        try
        {
            await Task.Delay(450); if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before workflow verification");
            var work = App.Work; Guid scene = await work.Store.Create("原生流程测试");
            await App.Planner.SaveDraft(new("保留原草稿", "未提交的备注"));
            planner = new PlannerWindow(App.Planner, Display); await planner.Open(PlannerPage.Tasks, Display, Display.Info.Work.Top);
            var text = new DataPackage(); text.SetText("拖入的待办\n完整说明");
            if (!await planner.ReceiveDrop(text.GetView()) || App.Planner.Data.Tasks.Single().Title != "拖入的待办" || App.Planner.Data.Draft.Title != "保留原草稿")
                throw new Exception("Dropped task replaced composer draft");
            await work.Flush();
            if (!work.Store.Data.Scenes.Single().Items.Contains(new(WorkKind.Task, App.Planner.Data.Tasks.Single().Id))) throw new Exception("Active scene did not capture new task");
            checks.Add("Text drop creates task while preserving unfinished draft; new task joins active scene");
            byte[] png;
            using (var stream = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                var pixels = Enumerable.Repeat((byte)240, 64 * 48 * 4).ToArray();
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 64, 48, 96, 96, pixels); await encoder.FlushAsync();
                png = new byte[(int)stream.Size]; using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)png.Length); reader.ReadBytes(png);
            }
            string original = Path.Combine(App.DataDirectory, "original.png"); await File.WriteAllBytesAsync(original, png);
            var image = new DataPackage(); image.SetStorageItems(new[] { await StorageFile.GetFileFromPathAsync(original) });
            await planner.Open(PlannerPage.Notes, Display, Display.Info.Work.Top);
            if (!await planner.ReceiveDrop(image.GetView())) throw new Exception("Image note import failed");
            Guid noteId = App.Planner.Data.ActiveNote!.Value; var attachment = work.Store.Data.Images.Single(i => i.NoteId == noteId);
            File.Delete(original);
            if (!File.Exists(attachment.Path) || attachment.Path == original || !WorkImages.Managed(attachment.Path)) throw new Exception("Image note did not retain independent managed image");
            using (var stream = await (await StorageFile.GetFileFromPathAsync(attachment.Path)).OpenReadAsync())
            { var decoder = await BitmapDecoder.CreateAsync(stream); if (decoder.PixelWidth != 64 || decoder.PixelHeight != 48) throw new Exception("Managed image invalid"); }
            await work.Flush();
            if (!work.Store.Data.Scenes.Single().Items.Contains(new(WorkKind.Note, noteId))) throw new Exception("Image note missing from scene");
            checks.Add("Native image drop persists decoded PNG and note independently of original file; scene links same note");
            Guid other = await work.Store.Create("另一个现场"); int count = work.Store.Data.Scenes.Single(s => s.Id == scene).Items.Length;
            await App.Station.Add([StationStore.Text("https://example.com/", "资料链接")]); await work.Flush();
            if (work.Store.Data.Scenes.Single(s => s.Id == scene).Items.Length != count || work.Store.Data.Scenes.Single(s => s.Id == other).Items.Length != 1) throw new Exception("Scene isolation failed");
            var linked = work.Store.Data.Scenes.Single(s => s.Id == other).Items.Single();
            await App.Station.Remove(linked.Id);
            if (new WorkStore(Path.Combine(App.DataDirectory, "workspaces.json")).Data.Scenes.Single(s => s.Id == other).Items.Single().Asset?.Value != "https://example.com/") throw new Exception("Scene lost resource when station record removed");
            var reload = new WorkStore(Path.Combine(App.DataDirectory, "workspaces.json"));
            if (reload.Data.Active != other || reload.Data.Scenes.Length != 2 || reload.Data.Images.Length != 1) throw new Exception("Companion metadata did not reload");
            sceneWindow = new WorkSceneWindow(_ => Task.CompletedTask); sceneWindow.Open(Display, Display.Info.Work.Top); sceneWindow.Hide();
            checks.Add("Scene switching isolates new content; scene selection and image references survive reload; native scene panel opens");
            Guid task = App.Planner.Data.Tasks.Single().Id; await App.Clock.StartCountdown(60, "原生专注测试", DateTimeOffset.Now);
            Guid clock = App.Clock.Data.Countdown!.Id; await work.Store.SetFocus(new(task, clock, "拖入的待办"));
            await Task.Delay(120); RefreshFocusBar();
            if (focusEntry.Visibility != Visibility.Visible || focusProgress.Visibility != Visibility.Visible) throw new Exception("Focus progress not visible");
            await App.Clock.PauseCountdown(DateTimeOffset.Now); var remaining = ClockPolicy.Remaining(App.Clock.Data.Countdown!, DateTimeOffset.Now, TimeZoneInfo.Local);
            await Task.Delay(100);
            if (ClockPolicy.Remaining(App.Clock.Data.Countdown!, DateTimeOffset.Now, TimeZoneInfo.Local) != remaining) throw new Exception("Paused focus clock moved");
            focus = new FocusWindow(); focus.Open(task, Display, Display.Info.Work.Top); focus.Hide();
            await App.Clock.CancelCountdown(); await work.Store.Resume(task, "从下一页继续"); RefreshFocusBar();
            if (App.Planner.Data.Tasks.Single().Completed || focusEntry.Visibility != Visibility.Collapsed || new WorkStore(Path.Combine(App.DataDirectory, "workspaces.json")).Data.Resumes[0].Text != "从下一页继续")
                throw new Exception("Focus continuation changed task or lost progress");
            checks.Add("Focus reuses countdown, pauses stably, renders top progress and saves continuation without completing task");
            byte[] before = File.ReadAllBytes(Path.Combine(App.DataDirectory, "planner.json"));
            peek = new PeekWindow(); peek.Open("todo", Display, Display.Info.Work.Top); peek.Hide(); peek.Open("clock", Display, Display.Info.Work.Top); peek.Hide();
            if (!File.ReadAllBytes(Path.Combine(App.DataDirectory, "planner.json")).SequenceEqual(before)) throw new Exception("Read-only preview changed planner");
            heldButton = todo; ShowHeldPreview();
            if (!peekFired || peekWindow?.IsOpen != true || !ToolbarClickBlocked) throw new Exception("Held preview did not suppress normal click");
            FinishPeek();
            if (peekFired || peekWindow?.IsOpen == true || !ToolbarClickBlocked) throw new Exception("Release did not close preview and suppress follow-up click");
            await Task.Delay(300);
            if (ToolbarClickBlocked) throw new Exception("Long-press suppression did not expire");
            preview = new ScreenshotPreviewWindow(_ => Task.CompletedTask);
            await preview.Open(new(PreviewBytes: png, SavedPath: attachment.Path), Display, Display.Info.Work.Top);
            await preview.VerifyRecognition(); preview.Hide();
            checks.Add("Read-only peek does not change planner; screenshot preview decodes bounded local PNG and closes");
            aiOptions = new AiOptionsStore(Path.Combine(App.DataDirectory, "ai-fixture.json"));
            aiOptions.Save(aiOptions.Data with { Pinned = true });
            ai = new AiWindow(aiOptions, App.DataDirectory, Display, () => { }, verification: true);
            aiOptions.Changed += ai.ApplyOptions;
            await ai.VerifyPanel(); await ai.VerifyDropIsolation(attachment.Path);
            checks.Add("AI local fixture preserves draft; staging does not expose text/files, explicit confirmation transfers only to fixture, provider change blocks transfer; no external AI request");
            await App.Station.Add([new(StationKind.File, attachment.Path)]);
            var source = App.Station.Items.Single(i => i.Value == attachment.Path);
            stationWindow = new StationWindow(App.Station, Display);
            await stationWindow.VerifyOutboundLease(() => !PanelModal, async () =>
            {
                var package = new DataPackage(); await StationTransfer.Fill(package, source);
                await ai.VerifyStationInbox(package.GetView(), attachment.Path);
                if (!App.Station.Items.Any(i => i.Id == source.Id) || !File.Exists(attachment.Path)) throw new Exception("Internal AI transfer changed station record");
            });
            checks.Add("Station drag remains visible without modal blocking; true modal protection still blocks target; original record and file retained");
            checks.Add("AI existing inbox supports append/replace/cancel; missing files, folders and capacity reject without losing prior pending data; merged temporary ownership cleaned");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            await App.Clock.CancelCountdown(); if (ai != null && aiOptions != null) aiOptions.Changed -= ai.ApplyOptions;
            ai?.Destroy(); stationWindow?.Destroy(); stationWindow = null; planner?.Destroy(); focus?.Destroy(); sceneWindow?.Destroy(); peek?.Destroy(); preview?.Destroy(); await App.FlushWork();
            File.WriteAllText(Path.Combine(App.DataDirectory, "workflow-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}

internal sealed partial class AiWindow
{
    internal async Task VerifyDropIsolation(string file)
    {
        if (!verification) throw new InvalidOperationException("Local fixtures only");
        async Task WaitForIdle()
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (ModalActive) { if (DateTime.UtcNow > until) throw new Exception("Drop protection lease did not end"); await Task.Delay(30); }
        }
        var core = current!.View.CoreWebView2;
        string before = await core.ExecuteScriptAsync("document.getElementById('draft').value");
        var text = new DataPackage(); text.SetText("待发送文本"); await WaitForIdle();
        if (!await ReceiveDrop(text.GetView())) throw new Exception("First AI payload not accepted");
        if (await ReceiveDrop(text.GetView())) throw new Exception("Busy AI drop falsely acknowledged");
        if (pendingDrop == null || await core.ExecuteScriptAsync("document.getElementById('draft').value") != before) throw new Exception("AI staging changed web draft before confirmation");
        await TransferDrop();
        string after = System.Text.Json.JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.getElementById('draft').value")) ?? "";
        if (pendingDrop != null || !after.EndsWith("待发送文本") || !after.Contains("保留我的草稿")) throw new Exception("Confirmed text did not append without replacing draft: " + Status);
        await core.ExecuteScriptAsync("(()=>{const f=document.createElement('input');f.type='file';f.id='fixture-file';document.body.append(f);})()");
        var package = new DataPackage(); package.SetStorageItems(new[] { await StorageFile.GetFileFromPathAsync(file) }); await WaitForIdle();
        if (!await ReceiveDrop(package.GetView()) || pendingDrop?.Files.Length != 1) throw new Exception("Local file payload not staged");
        if (await core.ExecuteScriptAsync("document.getElementById('fixture-file').files.length") != "0") throw new Exception("Staging exposed file to web");
        await TransferDrop();
        if (pendingDrop != null || await core.ExecuteScriptAsync("document.getElementById('fixture-file').files.length") != "1") throw new Exception("Confirmed file did not reach single local file input: " + Status);
        await WaitForIdle(); if (!await ReceiveDrop(text.GetView())) throw new Exception("Third AI payload not accepted");
        options.Save(options.Data with { Provider = "doubao" }); await TransferDrop();
        if (pendingDrop == null) throw new Exception("Provider change consumed original pending text");
        ClearDropInbox();
    }
}
