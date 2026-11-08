using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class AiWindow
{
    internal async Task PreviewViewport(DesktopMonitor monitor,int top)
    { using var lease=KeepOpen();await Open(monitor,top);await Task.Delay(30000); }
    readonly Dictionary<WebView2,string> maskRegistrations=[];
    readonly HashSet<WebView2> maskSurfaces=[];
    AiOptions? cornerPreview;
    internal void PreviewCorners(bool follow,int radius)
    {cornerPreview=options.Data with{FollowGlobalCorners=follow,WebCornerRadius=radius};foreach(var view in browsers.Children.OfType<WebView2>().ToArray())_ = MaskBrowser(view,false);}
    double ViewCornerRadius(WebView2 view)=>AiPolicy.CornerRadius(cornerPreview??options.Data,
        PanelTheme.Value<CornerRadius>("CardRadius").TopLeft,view.ActualWidth,view.ActualHeight);
    void InitializeViewportMask()
    {
        PanelTheme.Changed+=RefreshViewportMasks;
        Closed+=(_,_)=>PanelTheme.Changed-=RefreshViewportMasks;
    }
    void RefreshViewportMasks()
    {
        if(destroyed)return;
        foreach(var view in browsers.Children.OfType<WebView2>().ToArray())_ = MaskBrowser(view);
    }
    async Task MaskBrowser(WebView2 view,bool register=true)
    {
        if(view.CoreWebView2 is not {} core||destroyed)return;
        view.DefaultBackgroundColor=Windows.UI.Color.FromArgb(0,0,0,0);
        void ClipSurface()
        {
            var child=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(view);
            var surface=child??Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(view);
            // WebView's child surface uses physical pixels; XAML's own visual uses DIPs.
            float scale=child!=null?(float)(view.XamlRoot?.RasterizationScale??1):1;
            var geometry=surface.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size=new System.Numerics.Vector2((float)view.ActualWidth*scale,(float)view.ActualHeight*scale);
            geometry.CornerRadius=new System.Numerics.Vector2((float)ViewCornerRadius(view)*scale);
            surface.Clip=surface.Compositor.CreateGeometricClip(geometry);
        }
        if(maskSurfaces.Add(view))
        {
            view.SizeChanged+=(_,_)=>ClipSurface();
            XamlRoot? watched=null;
            void ScaleChanged(XamlRoot root,XamlRootChangedEventArgs args)=>ClipSurface();
            void WatchScale(){if(watched!=null)watched.Changed-=ScaleChanged;watched=view.XamlRoot;if(watched!=null)watched.Changed+=ScaleChanged;}
            view.Loaded+=(_,_)=>WatchScale();
            view.Unloaded+=(_,_)=>{if(watched!=null)watched.Changed-=ScaleChanged;watched=null;};
            WatchScale();
        }
        ClipSurface();
        string radius=ViewCornerRadius(view).ToString(CultureInfo.InvariantCulture);
        // Keep the canvas transparent; only the app's painted surface owns the corners.
        string script=$$"""
            (()=>{
              if(window!==window.top)return;
              window.__lbMaskObserver?.disconnect();
              const radius='{{radius}}px',transparent=c=>c==='transparent'||c==='rgba(0, 0, 0, 0)';
              let target=null;
              function apply(){
                const html=document.documentElement,body=document.body;
                if(!html||!body)return;
                target=document.getElementById('root')||document.getElementById('__next')||body;
                if(!window.__lbMaskColour){
                  const colour=getComputedStyle(target).backgroundColor;
                  window.__lbMaskColour=transparent(colour)?getComputedStyle(body).backgroundColor:colour;
                }
                for(const layer of [html,body]){
                  layer.style.setProperty('clip-path','none','important');
                  layer.style.setProperty('box-shadow','none','important');
                  if(layer!==target)layer.style.setProperty('background-color','transparent','important');
                }
                const fallback=transparent(window.__lbMaskColour)?'white':window.__lbMaskColour;
                target.style.setProperty('background-color','var(--dsw-alias-bg-base,var(--dsr-bg,'+fallback+'))','important');
                target.style.setProperty('border-radius',radius,'important');
                target.style.setProperty('clip-path','inset(0 round '+radius+')','important');
                target.style.setProperty('min-height','100vh','important');
                target.style.setProperty('box-shadow','none','important');
              }
              function connect(){
                const html=document.documentElement,body=document.body;if(!html||!body)return;
                window.__lbMaskObserver?.disconnect();apply();
                const observer=new MutationObserver(()=>{
                  if(target!==body&&!transparent(getComputedStyle(body).backgroundColor)||getComputedStyle(target).clipPath==='none')apply();
                });
                for(const layer of new Set([html,body,target]))observer.observe(layer,{attributes:true,attributeFilter:['style']});
                window.__lbMaskObserver=observer;
              }
              connect();document.addEventListener('DOMContentLoaded',connect,{once:true});
            })();
            """;
        try
        {
            if(register)
            {
                if(maskRegistrations.Remove(view,out string? old))core.RemoveScriptToExecuteOnDocumentCreated(old);
                maskRegistrations[view]=await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            }
            await core.ExecuteScriptAsync(script);
            if(Environment.GetCommandLineArgs().Contains("--preview-ai-panel"))
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"..",".build","ai-mask-state.json"),await core.ExecuteScriptAsync("JSON.stringify([document.documentElement,document.body,document.getElementById('root')].filter(Boolean).map(e=>({tag:e.tagName,bg:getComputedStyle(e).backgroundColor,clip:getComputedStyle(e).clipPath,shadow:getComputedStyle(e).boxShadow})))"));
        }
        catch { /* Closed/navigating pages recover on the next document. */ }
    }
}
