namespace Liubai;

internal sealed class AiService
{
    readonly string directory;
    AiWindow? window;
    internal AiOptionsStore Options { get; }
    internal bool Busy => window?.Initializing == true;
    internal bool IsOpen => window?.IsOpen == true;
    internal bool DockedOpen=>window is {IsOpen:true,Detached:false};
    internal string? DisplayDevice => window?.DisplayDevice;
    internal bool ModalActive => window?.ModalActive == true;
    internal string Status => window?.Status ?? "点击展开 AI 面板，右上角可放大为独立窗口。";
    internal event Action? Changed;
    internal AiService(string directory, AiOptionsStore? options = null)
    {
        this.directory = directory; Options = options ?? new(Path.Combine(directory, "ai.json"));
        Options.Changed += OptionsChanged;
    }
    void OptionsChanged() { window?.ApplyOptions(); Changed?.Invoke(); }
    internal void Select(string provider) => Options.Save(Options.Data with { Provider = provider });
    internal async Task Open(DesktopMonitor display, int? top = null)
    {
        if (SessionState.Locked == true) return;
        window ??= new AiWindow(Options, directory, display, () => Changed?.Invoke());
        await window.Open(display, top ?? display.Info.Work.Top);
    }
    internal void Hide(bool includeDetached = true) { if (includeDetached) window?.Hide(); else window?.HideDocked(); }
    internal async Task PreviewViewport(DesktopMonitor display,int top)
    {if(SessionState.Locked==true)return;window??=new AiWindow(Options,directory,display,()=>Changed?.Invoke());await window.PreviewViewport(display,top);}
    internal Task<bool> ReceiveDrop(Windows.ApplicationModel.DataTransfer.DataPackageView view) => window?.ReceiveDrop(view) ?? throw new IOException("请先打开 AI 面板。");
    internal void UpdateDisplay(DesktopMonitor display) { if (window?.DisplayDevice == display.Device) window.UpdateDisplay(display); }
    internal void Stop() { Options.Changed -= OptionsChanged; window?.Destroy(); window = null; }
}
