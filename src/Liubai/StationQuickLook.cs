using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Liubai;

internal sealed partial class StationWindow
{
    readonly Grid quickLookLayer=new() {Visibility=Visibility.Collapsed,Margin=new Thickness(8)};
    readonly StackPanel quickLookBody=new() {Spacing=10};
    readonly TextBlock quickLookTitle=PanelTheme.Title("快速预览");
    StationItem? quickLookItem;
    CancellationTokenSource? quickLookCancellation;
    int quickLookVersion;
    bool QuickLookOpen=>quickLookLayer.Visibility==Visibility.Visible;
    void InitializeQuickLook(Grid shell)
    {
        var grid=PanelTheme.Layout();grid.RowDefinitions.Add(new() {Height=GridLength.Auto});grid.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new() {Height=GridLength.Auto});
        var close=IconButton("\uE8BB","收起快速预览");close.Click+=(_,_)=>CloseQuickLook();grid.Children.Add(PanelTheme.Header(quickLookTitle,close));
        var scroll=new ScrollViewer {Content=quickLookBody,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var actions=new StackPanel {Spacing=8};
        var open=new Button {Content="用默认程序打开",HorizontalAlignment=HorizontalAlignment.Left};
        open.Click+=async (_,_)=>{if(quickLookItem is {} item)await Run(()=>OpenItem(item));};actions.Children.Add(open);Grid.SetRow(actions,2);grid.Children.Add(actions);
        var surface=PanelTheme.Card(grid);quickLookLayer.Children.Add(surface);shell.Children.Add(quickLookLayer);
        void Theme()=>surface.Background=new SolidColorBrush(quickLookLayer.ActualTheme==ElementTheme.Dark?Windows.UI.Color.FromArgb(248,30,32,36):Windows.UI.Color.FromArgb(248,246,249,251));
        quickLookLayer.ActualThemeChanged+=(_,_)=>Theme();Theme();
        quickLookLayer.KeyDown+=(_,e)=>{if(e.Key is VirtualKey.Space or VirtualKey.Escape){e.Handled=true;CloseQuickLook();}};
        Closed+=(_,_)=>CloseQuickLook();
    }
    async Task ToggleQuickLook()
    {
        if(QuickLookOpen){CloseQuickLook();return;}
        if(selection.Count!=1)return;
        var item=store.Items.FirstOrDefault(i=>selection.Contains(i.Id));if(item==null)return;
        await ShowQuickLook(item);
    }
    async Task ShowQuickLook(StationItem item)
    {
        CloseQuickLook();quickLookItem=item;quickLookTitle.Text=DisplayTitle(item);quickLookLayer.Visibility=Visibility.Visible;quickLookBody.Children.Add(Hint("正在读取预览…"));
        var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(8));quickLookCancellation=cancellation;int version=++quickLookVersion;
        try {
            var element=await QuickLookContent(item,cancellation.Token);
            if(version!=quickLookVersion||cancellation.IsCancellationRequested||destroyed)return;
            quickLookBody.Children.Clear();quickLookBody.Children.Add(element);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException) {
            if(version==quickLookVersion&&!destroyed){quickLookBody.Children.Clear();quickLookBody.Children.Add(Hint("暂时无法预览，可以打开原文件查看。"));}
        }
    }
    void CloseQuickLook()
    {quickLookVersion++;quickLookCancellation?.Cancel();quickLookCancellation?.Dispose();quickLookCancellation=null;quickLookItem=null;quickLookLayer.Visibility=Visibility.Collapsed;quickLookBody.Children.Clear();}
    void RefreshQuickLook()
    {if(quickLookItem is {} previous&&store.Items.FirstOrDefault(i=>i.Id==previous.Id)!=previous)CloseQuickLook();}
    static TextBlock PreviewText(string text)=>new() {Text=text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FontSize=14};
    static async Task<FrameworkElement> QuickLookContent(StationItem item,CancellationToken token)
    {
        var body=new StackPanel {Spacing=10};
        if(item.Kind is StationKind.Text or StationKind.Link){body.Children.Add(PreviewText(item.Value));return body;}
        body.Children.Add(PreviewText(item.Value));
        if(item.Kind==StationKind.Folder) {
            var names=await Task.Run(()=>Directory.EnumerateFileSystemEntries(item.Value).Take(41).Select(Path.GetFileName).ToArray(),token).WaitAsync(token);
            body.Children.Add(Hint("文件夹内容（最多显示 40 项，不读取子目录）"));foreach(var name in names.Take(40))body.Children.Add(PreviewText(name??""));if(names.Length>40)body.Children.Add(Hint("还有其他项目…"));return body;
        }
        var file=await StorageFile.GetFileFromPathAsync(item.Value).AsTask(token);
        var properties=await file.GetBasicPropertiesAsync().AsTask(token);body.Children.Add(Hint($"{properties.Size:N0} 字节 · 修改于 {properties.DateModified.ToLocalTime():yyyy-MM-dd HH:mm}"));
        if(QuickLookPolicy.TextFile(item.Value)) {
            var text=await QuickLookPolicy.ReadText(item.Value,token);body.Children.Add(PreviewText(text.Text));if(text.Truncated)body.Children.Add(Hint("仅显示前 20,000 个字符"));return body;
        }
        if(file.ContentType.StartsWith("image/",StringComparison.OrdinalIgnoreCase)&&properties.Size<=64*1024*1024) {
            using var stream=await file.OpenReadAsync().AsTask(token);var bitmap=await DecodePreview(stream,token);
            body.Children.Add(new Image {Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=600});return body;
        }
        if(Path.GetExtension(item.Value).Equals(".pdf",StringComparison.OrdinalIgnoreCase)&&properties.Size<=16*1024*1024) {
            try {
                byte[] bytes;
                // Decode an in-memory snapshot so the PDF engine never retains the original file handle.
                await using(var source=new FileStream(item.Value,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,81920,true)) {
                    if(source.Length>16*1024*1024)throw new InvalidDataException("PDF 过大");bytes=new byte[(int)source.Length];await source.ReadExactlyAsync(bytes,token);
                }
                using var buffer=new MemoryStream(bytes,false);using var input=buffer.AsRandomAccessStream();
                var document=await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(input).AsTask(token);
                if(document.PageCount>0) {
                    using var page=document.GetPage(0);
                    if(!double.IsFinite(page.Size.Width)||!double.IsFinite(page.Size.Height)||page.Size.Width<=0||page.Size.Height<=0)throw new InvalidDataException("PDF 页面尺寸无效");
                    double scale=1200d/Math.Max(page.Size.Width,page.Size.Height);
                    using var output=new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(output,new Windows.Data.Pdf.PdfPageRenderOptions {DestinationWidth=(uint)Math.Max(1,page.Size.Width*scale),DestinationHeight=(uint)Math.Max(1,page.Size.Height*scale)}).AsTask(token);
                    output.Seek(0);var bitmap=await DecodePreview(output,token);
                    body.Children.Add(Hint($"PDF 第 1 页 · 共 {document.PageCount} 页"));body.Children.Add(new Image {Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=650});return body;
                }
            }
            catch(Exception ex) when(ex is not OperationCanceledException and not OutOfMemoryException) {body.Children.Add(Hint("未能读取 PDF 首页，下面显示系统提供的信息。"));}
        }
        using var thumbnail=await file.GetThumbnailAsync(ThumbnailMode.SingleItem,512,ThumbnailOptions.ResizeThumbnail).AsTask(token);
        if(thumbnail!=null){var bitmap=new BitmapImage {DecodePixelWidth=512};await bitmap.SetSourceAsync(thumbnail).AsTask(token);bool content=thumbnail.Type==ThumbnailType.Image;
            body.Children.Add(new Image {Source=bitmap,Stretch=Stretch.Uniform,MaxHeight=content?480:96,Width=content?double.NaN:96,HorizontalAlignment=content?HorizontalAlignment.Stretch:HorizontalAlignment.Left});}
        body.Children.Add(Hint("文档显示系统缩略图或文件信息；打开可查看完整内容。"));return body;
    }
    static async Task<WriteableBitmap> DecodePreview(Windows.Storage.Streams.IRandomAccessStream stream,CancellationToken token)
    {
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);
        double scale=Math.Min(1,1200d/Math.Max(decoder.PixelWidth,decoder.PixelHeight));uint width=Math.Max(1,(uint)(decoder.PixelWidth*scale)),height=Math.Max(1,(uint)(decoder.PixelHeight*scale));
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,new BitmapTransform {ScaledWidth=width,ScaledHeight=height},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb).AsTask(token);
        bool rotated=decoder.OrientedPixelWidth!=decoder.PixelWidth;var bitmap=new WriteableBitmap((int)(rotated?height:width),(int)(rotated?width:height));
        using(var target=bitmap.PixelBuffer.AsStream())await target.WriteAsync(pixels.DetachPixelData(),token);bitmap.Invalidate();return bitmap;
    }

}
