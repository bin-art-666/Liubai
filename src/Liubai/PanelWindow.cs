using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VirtualKey = Windows.System.VirtualKey;

namespace Liubai;

/// <summary>Shared lifecycle for transient feature panels: outside focus, Escape and close dismiss them.</summary>
internal abstract class PanelWindow : Window
{
    internal static event Action? PresentationChanged;
    protected void NotifyPresentationChanged() => PresentationChanged?.Invoke();
    long activationVersion;
    bool closed;
    int modalDepth;
    int dragDepth;
    OutsideClickWatcher? outsideClicks;
    PanelCaption? caption;
    protected void InitializeFloatingCaption(FrameworkElement header, FrameworkElement controls, Func<bool> enabled) => caption = new(this, header, controls, enabled);
    protected void UpdateFloatingCaption() => caption?.Refresh();
    internal Task VerifyFloatingCaption(bool enabled) => caption?.Verify(enabled) ?? throw new InvalidOperationException("Caption not initialized");
    internal bool IsOpen { get; private set; }
    internal bool ModalActive => modalDepth > 0;
    internal bool DragActive => dragDepth > 0;
    bool VisibilityProtected => modalDepth > 0 || dragDepth > 0;
    protected virtual bool DismissOnOutside => true;
    protected virtual bool Frameless => true;

    protected PanelWindow()
    {
        Activated += (_, args) =>
        {
            long version = ++activationVersion;
            if (args.WindowActivationState != WindowActivationState.Deactivated || !IsOpen || VisibilityProtected || !DismissOnOutside) return;
            // Let XAML finish activating a dropdown/owned popup before deciding focus left the panel.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (closed || !IsOpen || version != activationVersion || VisibilityProtected || !DismissOnOutside) return;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (!Native.PanelHasFocus(handle) && !PanelToggleTargets.OwnsForegroundPointer()) Hide();
            });
        };
        AppWindow.Closing += (_, args) => { if (!closed) { args.Cancel = true; Hide(); } };
        Closed += (_, _) => { closed = true; IsOpen = false; activationVersion++; outsideClicks?.Dispose(); caption?.Dispose(); PanelTheme.Unregister(this); NotifyPresentationChanged(); };
    }

    protected void InitializePanel(FrameworkElement content)
    {
        Content = content;
        PanelTheme.Register(this);
        content.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Hide();
        };
        AppWindow.Hide();
    }

    protected void ShowPanel(bool activate = true)
    {
        bool entering = !IsOpen;
        PanelTheme.Refresh(this);
        activationVersion++;
        IsOpen = true;
        // Activate alone is not a reliable way to re-show an AppWindow that was
        // explicitly hidden, especially after a non-activating drag-hover open.
        AppWindow.Show(false);
        if (entering && Content is FrameworkElement content) PanelMotion.Enter(content);
        if (activate) Activate();
        if (Frameless) Native.RemoveWindowFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
        caption?.Queue();
        outsideClicks?.Dispose();
        outsideClicks = new OutsideClickWatcher(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue,
            () => { if (!VisibilityProtected && DismissOnOutside) Hide(); });
        NotifyPresentationChanged();
    }

    internal void Hide()
    {
        if (!IsOpen || closed || VisibilityProtected) return;
        activationVersion++;
        IsOpen = false;
        outsideClicks?.Dispose();
        outsideClicks = null;
        try { OnHiding(); }
        finally
        {
            AppWindow.Hide();
            if (Content is FrameworkElement content) PanelMotion.Reset(content);
            NotifyPresentationChanged();
        }
    }

    protected virtual void OnHiding() { }
    internal void ConfirmIncomingDrop()
    {
        if (closed || !IsOpen) return;
        var lease = KeepOpen(); ShowPanel();
        if (!DispatcherQueue.TryEnqueue(async () => { try { await Task.Delay(350); } finally { lease.Dispose(); } })) lease.Dispose();
    }
    protected IDisposable KeepOpen()
    {
        modalDepth++; activationVersion++;
        return new ModalLease(() => { modalDepth--; activationVersion++; });
    }
    protected IDisposable KeepOpenForDrag()
    {
        dragDepth++; activationVersion++;
        return new ModalLease(() => { dragDepth--; activationVersion++; });
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
