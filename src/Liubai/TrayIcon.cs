using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Liubai;

internal sealed class TrayIcon : IDisposable
{
    const uint Callback = 0x8001;
    readonly IntPtr window, icon;
    readonly DispatcherQueue dispatcher;
    readonly Action openSettings, toggleBar, quit;
    readonly Func<bool> isVisible;
    readonly Func<GlassStyle> getStyle;
    readonly Action<GlassStyle> setStyle;
    readonly Func<bool> paused;
    readonly Action togglePause;
    readonly Action? openTasks, openNotes;
    readonly WindowProcedure procedure;
    readonly uint explorerCreated = RegisterWindowMessage("TaskbarCreated");
    readonly UIntPtr subclassId = new(0x4C54);
    bool disposed, registered;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct IconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    internal TrayIcon(IntPtr window, DispatcherQueue dispatcher, Action openSettings, Action toggleBar,
        Func<bool> isVisible, Func<GlassStyle> getStyle, Action<GlassStyle> setStyle, Action quit,
        Func<bool>? paused = null, Action? togglePause = null, Action? openTasks = null, Action? openNotes = null)
    {
        this.window = window; this.dispatcher = dispatcher; this.openSettings = openSettings;
        this.toggleBar = toggleBar; this.isVisible = isVisible; this.getStyle = getStyle;
        this.setStyle = setStyle; this.quit = quit;
        this.paused = paused ?? (() => false); this.togglePause = togglePause ?? (() => { });
        this.openTasks = openTasks; this.openNotes = openNotes;
        icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Liubai.ico"), 1, 32, 32, 0x10);
        if (icon == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        procedure = HandleMessage;
        if (!SetWindowSubclass(window, procedure, subclassId, UIntPtr.Zero))
        {
            DestroyIcon(icon);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        AddIcon();
    }

    IconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(), Window = window, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, CallbackMessage = Callback, Icon = icon,
        Tip = "留白 · 点击打开设置", Info = "", InfoTitle = "", Version = 4
    };

    void AddIcon()
    {
        var data = Data();
        registered = Shell_NotifyIcon(0, ref data);
        if (registered) Shell_NotifyIcon(4, ref data);
    }

    IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (!disposed)
        {
            if (message == explorerCreated) AddIcon();
            else if (message == Callback)
            {
                int notification = (int)(lParam.ToInt64() & 0xFFFF);
                // Version 4 delivers both mouse activation and keyboard activation here.
                if (notification is 0x400 or 0x401) dispatcher.TryEnqueue(() => { if (!disposed) openSettings(); });
                else if (notification == 0x007B)
                {
                    int x = (short)(wParam.ToInt64() & 0xFFFF), y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    dispatcher.TryEnqueue(() => { if (!disposed) ShowMenu(x, y); });
                }
            }
        }
        return DefSubclassProc(handle, message, wParam, lParam);
    }

    void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        uint command;
        try
        {
            AppendMenu(menu, 0, 1, "打开设置");
            if (openTasks != null) AppendMenu(menu, 0, 5, "打开待办");
            if (openNotes != null) AppendMenu(menu, 0, 6, "打开随手记");
            AppendMenu(menu, 0, 2, isVisible() ? "隐藏顶栏" : "显示顶栏");
            AppendMenu(menu, 0, 4, paused() ? "恢复颜色跟随" : "暂停颜色跟随");
            AppendMenu(menu, 0x800, 0, null);
            string[] labels = ["轻透玻璃", "柔和磨砂", "浓厚亚克力", "自定义"];
            for (int i = 0; i < labels.Length; i++)
                AppendMenu(menu, (int)getStyle() == i ? 8u : 0u, (uint)(10 + i), labels[i]);
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 3, "退出留白");
            SetForegroundWindow(window);
            command = TrackPopupMenu(menu, 0x102, x, y, 0, window, IntPtr.Zero);
            PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
        if (disposed) return;
        if (command == 1) openSettings();
        else if (command == 2) toggleBar();
        else if (command == 3) quit();
        else if (command == 4) togglePause();
        else if (command == 5) openTasks?.Invoke();
        else if (command == 6) openNotes?.Invoke();
        else if (command is >= 10 and <= 13) setStyle((GlassStyle)(command - 10));
    }

    internal bool IsRegistered => registered;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var data = Data();
        Shell_NotifyIcon(2, ref data);
        registered = false;
        RemoveWindowSubclass(window, procedure, subclassId);
        DestroyIcon(icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("comctl32.dll", SetLastError = true)] static extern bool SetWindowSubclass(IntPtr window, WindowProcedure procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window, WindowProcedure procedure, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
