using System.Runtime.InteropServices;

namespace Liubai;

internal static class SessionState
{
    // Prefix of WTSINFOEXW on x64: the level-1 union is 8-byte aligned.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct SessionInfo
    {
        [FieldOffset(0)] public uint Level;
        [FieldOffset(16)] public int Flags;
    }
    internal static bool? Locked
    {
        get
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out var buffer, out int bytes)) return null; // WTS_CURRENT_SESSION, WTSSessionInfoEx
            try
            {
                if (bytes < Marshal.SizeOf<SessionInfo>()) return null;
                var info = Marshal.PtrToStructure<SessionInfo>(buffer);
                return info.Level == 1 && info.Flags is 0 or 1 ? info.Flags == 0 : null;
            }
            finally { WTSFreeMemory(buffer); }
        }
    }
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")] static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
}
