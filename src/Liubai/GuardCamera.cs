using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;

namespace Liubai;

internal sealed record GuardCameraDevice(string Id, string Name);
internal readonly record struct GuardSample(int Faces, bool Fresh);
internal interface IGuardCamera : IAsyncDisposable
{
    string Description { get; }
    Task<GuardSample> Sample(CancellationToken token);
}
internal interface IGuardCameraFactory
{
    Task<IGuardCamera> Open(string id, CancellationToken token);
}
internal sealed class GuardCameraFactory : IGuardCameraFactory
{
    internal static async Task<GuardCameraDevice[]> Devices(CancellationToken token = default)
        => (await MediaFrameSourceGroup.FindAllAsync().AsTask(token)).Where(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color))
            .Select(g => new GuardCameraDevice(g.Id, g.DisplayName)).ToArray();
    public async Task<IGuardCamera> Open(string id, CancellationToken token)
    {
        if (!FaceTracker.IsSupported || !FaceTracker.IsBitmapPixelFormatSupported(BitmapPixelFormat.Nv12)) throw new InvalidOperationException("本机不支持 Windows 本地人脸跟踪。");
        var groups = await MediaFrameSourceGroup.FindAllAsync().AsTask(token);
        var colors = groups.Where(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color));
        var group = id.Length == 0 ? colors.FirstOrDefault() : colors.FirstOrDefault(g => g.Id == id);
        if (group == null) throw new IOException("没有找到所选摄像头，请检查连接或重新选择。");
        var capture = new MediaCapture(); MediaFrameReader? reader = null;
        try
        {
            // Shared read-only never changes another app's camera resolution/fps.
            await capture.InitializeAsync(new MediaCaptureInitializationSettings { SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video }).AsTask(token);
            var source = capture.FrameSources.Values.Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(s => s.Info.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1).FirstOrDefault()
                ?? throw new IOException("摄像头没有可用的彩色视频流。");
            var format = source.CurrentFormat.VideoFormat;
            double scale = Math.Min(1, Math.Min(320d / format.Width, 240d / format.Height));
            uint width = Math.Max(2u, (uint)(format.Width * scale) / 2 * 2), height = Math.Max(2u, (uint)(format.Height * scale) / 2 * 2);
            reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12, new BitmapSize { Width = width, Height = height }).AsTask(token);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            var tracker = await FaceTracker.CreateAsync().AsTask(token);
            if (await reader.StartAsync().AsTask(token) != MediaFrameReaderStartStatus.Success) throw new IOException("摄像头无法开始读取，可能正被其他程序独占。");
            token.ThrowIfCancellationRequested();
            return new GuardCamera(capture, reader, tracker, $"{group.DisplayName} · {width}×{height} · 最多 2 次/秒");
        }
        catch
        {
            if (reader != null) { try { await reader.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } reader.Dispose(); }
            capture.Dispose(); throw;
        }
    }
}
internal sealed class GuardCamera(MediaCapture capture, MediaFrameReader reader, FaceTracker tracker, string description) : IGuardCamera
{
    TimeSpan? previous;
    FaceTracker? tracker = tracker;
    public string Description => description;
    public async Task<GuardSample> Sample(CancellationToken token)
    {
        using var reference = reader.TryAcquireLatestFrame();
        if (reference?.VideoMediaFrame == null) return new(0, false);
        if (reference.SystemRelativeTime == null) throw new IOException("摄像头缺少画面时间信息，无法可靠检测，请尝试其他设备。");
        if (reference.SystemRelativeTime == previous) return new(0, false);
        previous = reference.SystemRelativeTime;
        // Accessing these WinRT properties creates strong references; explicitly
        // close the bitmap and VideoFrame as well as the outer frame reference.
        using var frame = reference.VideoMediaFrame.GetVideoFrame();
        using var bitmap = frame.SoftwareBitmap;
        if (bitmap == null) return new(0, false);
        var faces = await tracker!.ProcessNextFrameAsync(frame).AsTask(token);
        return new(faces.Count, true);
    }
    public async ValueTask DisposeAsync()
    {
        try { await reader.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        reader.Dispose(); capture.Dispose(); tracker = null;
    }
}
