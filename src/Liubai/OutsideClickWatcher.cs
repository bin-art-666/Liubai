using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Liubai;

// Watches button-down events only while a panel is open. Never consumes or records input.
internal sealed class OutsideClickWatcher : IDisposable
{
    delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);
    readonly HookProcedure procedure;
    IntPtr hook;
    bool disposed;

    internal OutsideClickWatcher(IntPtr panel, DispatcherQueue dispatcher, Action dismiss)
    {
        procedure = (code, message, data) =>
        {
            if (code >= 0 && message.ToInt64() is 0x0201 or 0x0204 or 0x0207 or 0x020B)
            {
                // POINT is the first member of MSLLHOOKSTRUCT; no other input data is needed.
                var point = Marshal.PtrToStructure<Native.Point>(data);
                if (!Native.IsPanelWindow(panel, WindowFromPoint(point)))
                    dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => { if (!disposed) dismiss(); });
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        hook = SetWindowsHookEx(14, procedure, GetModuleHandle(null), 0); // WH_MOUSE_LL
        // Window activation still provides dismissal if Windows cannot install this observer.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        GC.KeepAlive(procedure);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetWindowsHookEx(int id, HookProcedure procedure, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
