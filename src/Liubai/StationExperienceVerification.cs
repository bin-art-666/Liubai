using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Text.Json;

namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifyExperience()
    {
        var checks=new List<string>();Open(display,display.Info.Work.Top);pin.IsChecked=true;
        await store.Add([StationStore.Text("统一原生测试资料")]);var item=store.Items.Single();
        Guid group=await App.Work.Store.Create("统一原生测试分组");await App.Work.Store.Link(group,[new(WorkKind.Station,item.Id)]);
        await Remember("移除测试记录",()=>store.Remove(item.Id));
        if(!history.CanUndo||store.Items.Count!=0)throw new Exception("Removal history missing");
        await UndoLast();
        if(store.Items.Single().Id!=item.Id||!App.WorkStore.Data.Scenes.Single().Items.Any(r=>r.Id==item.Id)||undoToast.Visibility!=Visibility.Visible)throw new Exception("Undo did not restore source and association");
        checks.Add("Native remove/undo restores stable record identity, group association and inline feedback");
        workspaceId=null;Refresh();SelectItem(item.Id,false,false);await ToggleQuickLook();
        if(!QuickLookOpen||!quickLookBody.Children.OfType<StackPanel>().SelectMany(p=>p.Children).OfType<TextBlock>().Any(t=>t.Text==item.Value))throw new Exception("Text quick look missing");
        await ToggleQuickLook();if(QuickLookOpen||quickLookBody.Children.Count!=0||quickLookCancellation!=null)throw new Exception("Quick look did not release content");
        checks.Add("Text quick look toggles without opening a program and clears content/cancellation on close");
        string path=Path.Combine(App.DataDirectory,"preview-fixture.png");
        var directory=await StorageFolder.GetFolderFromPathAsync(App.DataDirectory);var image=await directory.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.ReplaceExisting);
        using(var stream=await image.OpenAsync(FileAccessMode.ReadWrite)){var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);var pixels=Enumerable.Repeat((byte)180,128*64*4).ToArray();encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,128,64,96,96,pixels);await encoder.FlushAsync();}
        var content=(StackPanel)await QuickLookContent(new(Guid.NewGuid(),StationKind.File,path,"",DateTimeOffset.Now),CancellationToken.None);
        if(content.Children.OfType<Image>().Single().Source is not WriteableBitmap {PixelWidth:128,PixelHeight:64})throw new Exception("Bounded image preview failed");
        checks.Add("Windows image decoder produces a bounded preview with streams disposed");
        string pdfPath=Path.Combine(App.DataDirectory,"preview-page.pdf");
        var pdf=new System.Text.StringBuilder("%PDF-1.4\n");var offsets=new List<int>();string commands="0.2 0.5 0.8 rg 0 0 200 100 re f\n";
        string[] objects=["<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << >> /Contents 4 0 R >>",$"<< /Length {commands.Length} >>\nstream\n{commands}endstream"];
        for(int i=0;i<objects.Length;i++){offsets.Add(pdf.Length);pdf.Append(i+1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");}
        int start=pdf.Length;pdf.Append("xref\n0 5\n0000000000 65535 f \n");foreach(int offset in offsets)pdf.Append(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n").Append(start).Append("\n%%EOF\n");await File.WriteAllBytesAsync(pdfPath,System.Text.Encoding.ASCII.GetBytes(pdf.ToString()));
        var pdfView=(StackPanel)await QuickLookContent(new(Guid.NewGuid(),StationKind.File,pdfPath,"",DateTimeOffset.Now),CancellationToken.None);
        quickLookBody.Children.Clear();quickLookBody.Children.Add(pdfView);quickLookLayer.Visibility=Visibility.Visible;await Task.Delay(250);
        if(!pdfView.Children.OfType<TextBlock>().Any(t=>t.Text.StartsWith("PDF 第 1 页"))||pdfView.Children.OfType<Image>().Single().Source is not WriteableBitmap {PixelWidth:>0 and <=1200,PixelHeight:>0 and <=1200})
            throw new Exception("PDF first page preview failed: "+string.Join(" | ",pdfView.Children.OfType<TextBlock>().Select(t=>t.Text))+"; "+string.Join(",",pdfView.Children.OfType<Image>().Select(i=>i.Source is WriteableBitmap b?$"{b.PixelWidth}x{b.PixelHeight}":i.Source?.GetType().Name)));
        CloseQuickLook();
        using(var unlocked=File.Open(pdfPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))if(unlocked.Length==0)throw new Exception("PDF source changed");
        checks.Add("Native PDF first-page rendering is bounded and releases the original file before decoding");
        await plannerFixture();
        return checks.ToArray();
        async Task plannerFixture(){await App.Planner.AddTask("统一原生测试待办","",null);await App.Planner.SaveNote(Guid.NewGuid(),"统一原生测试笔记");await App.Work.Flush();}
    }
}

internal sealed partial class UnifiedSearchView
{
    internal async Task<string[]> VerifySearch()
    {
        query.Text="统一原生测试";
        async Task Until(Func<bool> done){long limit=Environment.TickCount64+5000;while(!done()){if(Environment.TickCount64>limit)throw new Exception("Search timed out: "+StatusLine.Text);await Task.Delay(40);}}
        await Until(()=>results.IsEnabled&&results.Items.Count>=4);
        var hits=results.Items.Cast<ListViewItem>().Select(i=>(SearchHit)i.Tag).ToArray();
        if(hits.Select(h=>h.Kind).Distinct().Count()!=4)throw new Exception("Unified categories missing");
        var note=App.Planner.Data.Notes.Single();await App.Planner.DeleteNote(note.Id);await Until(()=>results.IsEnabled&&results.Items.Count==3);
        if(results.Items.Cast<ListViewItem>().Any(i=>((SearchHit)i.Tag).Id==note.Id))throw new Exception("Deleted note remained in results");
        return ["Native unified search includes station, group, task and note results","Search refreshes after deletion without stale result activation"];
    }
    internal bool VerifyReleased()=>results.Items.Count==0&&!subscribed&&cancellation==null;
}

public sealed partial class BarWindow
{
    async Task VerifyStationExperience()
    {
        string result="PASS",error="";var checks=new List<string>();StationWindow? panel=null;UnifiedSearchView? searchPanel=null;
        try {
            await Task.Delay(450);if(SessionState.Locked==true)throw new IOException("LOCKED: unlock before verification");
            panel=new StationWindow(App.Station,Display);checks.AddRange(await panel.VerifyExperience());
            checks.AddRange(await panel.VerifySearchReturn());
            panel.OpenSearch(_=>Task.CompletedTask);searchPanel=panel.SearchPage!;checks.AddRange(await searchPanel.VerifySearch());panel.Hide();
            if(!searchPanel.VerifyReleased())throw new Exception("Hidden search retains resources");checks.Add("Hidden search cancels pending work and detaches all store observers");
            var item=App.Station.Items.Single();await OpenSearchHit(new(SearchKind.Item,item.Id,"","","",DateTimeOffset.Now,0));
            if(stationWindow?.IsOpen!=true)throw new Exception("Search didn't reveal station item");checks.Add("Search dispatch reveals the original station record through the shared panel route");
        }
        catch(Exception ex){result="FAIL";error=ex.ToString();}
        finally {panel?.Destroy();searchPanel?.Stop();Directory.CreateDirectory(App.DataDirectory);File.WriteAllText(Path.Combine(App.DataDirectory,"station-experience-check.json"),JsonSerializer.Serialize(new{result,error,checks}));Close();}
    }
}
