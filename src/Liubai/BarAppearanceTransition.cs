using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Liubai;

// Keep the previous composed frame above the new native material, then fade it away.
// Only one bitmap is retained, in memory; the overlay never intercepts input.
internal sealed class BarAppearanceTransition
{
    readonly Image cover;
    Storyboard? storyboard;
    int version;
    internal bool Active { get; private set; }
    internal BarAppearanceTransition(Image cover) => this.cover = cover;

    internal bool Begin(RectInt32 bounds, AppearanceAnimation mode)
    {
        if (mode == AppearanceAnimation.Off || !AnimationsEnabled()) { Cancel(); return false; }
        try
        {
            var pixels = Capture(bounds);
            Cancel();
            if (pixels == null) return false;
            var bitmap = new WriteableBitmap(bounds.Width, bounds.Height);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
            bitmap.Invalidate();
            cover.Source = bitmap; cover.Opacity = 1; cover.Visibility = Visibility.Visible;
            Active = true;
            return true;
        }
        catch { Cancel(); return false; }
    }
    internal async void Complete(AppearanceAnimation mode, Func<bool> materialReady)
    {
        if (!Active) return;
        int current = version;
        try
        {
            int frames = 0;
            var ready = new TaskCompletionSource();
            void Rendered(object? sender, object args) { if (++frames >= 2) ready.TrySetResult(); }
            CompositionTarget.Rendering += Rendered;
            try { await Task.WhenAny(ready.Task, Task.Delay(120)); }
            finally { CompositionTarget.Rendering -= Rendered; }
            if (!Active || current != version) return;
            long deadline = Environment.TickCount64 + 500;
            while (Active && current == version && !materialReady() && Environment.TickCount64 < deadline) await Task.Delay(15);
            if (!Active || current != version) return;
            var fade = new DoubleAnimation
            {
                From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(mode == AppearanceAnimation.Soft ? 280 : 180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(fade, cover); Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard = new Storyboard(); storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) => { if (current == version) Cancel(); };
            storyboard.Begin();
            // Completion events can be delayed when composition is suspended.
            await Task.Delay(800);
            if (current == version) Cancel();
        }
        catch { if (current == version) Cancel(); }
    }
    internal void Cancel()
    {
        version++; Active = false;
        storyboard?.Stop(); storyboard = null;
        cover.Visibility = Visibility.Collapsed; cover.Source = null; cover.Opacity = 0;
    }
    static bool AnimationsEnabled() => SystemParametersInfo(0x1042, 0, out bool enabled, 0) && enabled;

    internal static byte[]? Capture(RectInt32 bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        if (screen == IntPtr.Zero) return null;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020)) return null;
            GdiFlush();
            byte[] pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important, Colors;
    }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
}
