using System.Runtime.InteropServices;

namespace Liubai;

internal static class WindowsCompositionQueue
{
    // WinUI's Microsoft.UI queue does not initialize the Windows.System queue
    // required by the Windows.UI compositor. Keep our current-thread controller
    // alive for the UI thread, including when no acrylic controller was created.
    [ThreadStatic] static Windows.System.DispatcherQueueController? ownedController;
    [StructLayout(LayoutKind.Sequential)] struct Options
    { public int Size, ThreadType, ApartmentType; }

    internal static void EnsureCurrentThread()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null || ownedController != null) return;
        var options = new Options { Size = Marshal.SizeOf<Options>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var pointer));
        try { ownedController = WinRT.MarshalInspectable<Windows.System.DispatcherQueueController>.FromAbi(pointer); }
        finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
    }
    internal static async void ExitApplication()
    {
        var controller = ownedController; ownedController = null;
        try { if (controller != null) await controller.ShutdownQueueAsync(); }
        finally { Microsoft.UI.Xaml.Application.Current.Exit(); }
    }
    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(Options options, out IntPtr controller);
}
