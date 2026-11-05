namespace Liubai;

internal sealed class AiService
{
    readonly string directory;
    AiWindow? window;
    internal AiOptionsStore Options { get; }
    internal bool Busy => window?.Initializing == true;
    internal bool IsOpen => window?.IsOpen == true;
    internal bool ModalActive => window?.ModalActive == true;
    internal string Status => window?.Status ?? "点击展开 AI 面板，右上角可放大为独立窗口。";
    internal event Action? Changed;
    internal AiService(string directory)
    {
        this.directory = directory; Options = new(Path.Combine(directory, "ai.json"));
        Options.Changed += () => { window?.ApplyOptions(); Changed?.Invoke(); };
    }
    internal void Select(string provider) => Options.Save(Options.Data with { Provider = provider });
    internal async Task Open(DesktopMonitor display, int? top = null)
    {
        if (SessionState.Locked == true) return;
        window ??= new AiWindow(Options, directory, display, () => Changed?.Invoke());
        await window.Open(display, top ?? display.Info.Work.Top);
    }
    internal void Hide(bool includeDetached = true) { if (includeDetached) window?.Hide(); else window?.HideDocked(); }
    internal void UpdateDisplay(DesktopMonitor display) { if (window?.DisplayDevice == display.Device) window.UpdateDisplay(display); }
    internal void Stop() { window?.Destroy(); window = null; }
}
