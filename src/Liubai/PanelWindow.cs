using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VirtualKey = Windows.System.VirtualKey;

namespace Liubai;

/// <summary>Shared lifecycle for transient feature panels: outside focus, Escape and close dismiss them.</summary>
internal abstract class PanelWindow : Window
{
    long activationVersion;
    bool closed;
    int modalDepth;
    OutsideClickWatcher? outsideClicks;
    internal bool IsOpen { get; private set; }
    internal bool ModalActive => modalDepth > 0;

    protected PanelWindow()
    {
        Activated += (_, args) =>
        {
            long version = ++activationVersion;
            if (args.WindowActivationState != WindowActivationState.Deactivated || !IsOpen || modalDepth > 0) return;
            // Let XAML finish activating a dropdown/owned popup before deciding focus left the panel.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (closed || !IsOpen || version != activationVersion || modalDepth > 0) return;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (!Native.PanelHasFocus(handle)) Hide();
            });
        };
        AppWindow.Closing += (_, args) => { if (!closed) { args.Cancel = true; Hide(); } };
        Closed += (_, _) => { closed = true; IsOpen = false; activationVersion++; outsideClicks?.Dispose(); };
    }

    protected void InitializePanel(FrameworkElement content)
    {
        Content = content;
        content.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Hide();
        };
        AppWindow.Hide();
    }

    protected void ShowPanel()
    {
        activationVersion++;
        IsOpen = true;
        Activate();
        Native.RemoveWindowFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
        outsideClicks?.Dispose();
        outsideClicks = new OutsideClickWatcher(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue,
            () => { if (modalDepth == 0) Hide(); });
    }

    internal void Hide()
    {
        if (!IsOpen || closed || modalDepth > 0) return;
        activationVersion++;
        IsOpen = false;
        outsideClicks?.Dispose();
        outsideClicks = null;
        try { OnHiding(); }
        finally { AppWindow.Hide(); }
    }

    protected virtual void OnHiding() { }
    protected IDisposable KeepOpen()
    {
        modalDepth++; activationVersion++;
        return new ModalLease(() => { modalDepth--; activationVersion++; });
    }
    sealed class ModalLease(Action release) : IDisposable
    {
        Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
    internal void Destroy()
    {
        if (closed) return;
        try { Hide(); }
        finally { closed = true; outsideClicks?.Dispose(); Close(); }
    }
}
