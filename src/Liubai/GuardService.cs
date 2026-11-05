using Microsoft.UI.Dispatching;

namespace Liubai;

internal enum GuardState { Off, Starting, Running, Paused, Unavailable, Stopping }
internal sealed class GuardService
{
    readonly DispatcherQueue dispatcher;
    readonly GuardOptionsStore options;
    readonly IGuardCameraFactory factory;
    readonly SemaphoreSlim transitions = new(1);
    readonly GuardTrigger trigger = new();
    readonly GuardPause pause = new();
    DispatcherQueueTimer? pauseTimer;
    DateTimeOffset pauseDisplayUntil;
    readonly GuardAlertPresenter alerts = new();
    CancellationTokenSource? cancellation;
    Task run = Task.CompletedTask;
    int generation;
    bool suspended, stopped;
    volatile bool alertShowing;
    string cameraId;
    int holdMilliseconds;
    long samples, processingTicks;
    int lastFaces;
    internal bool Wanted { get; private set; }
    internal bool ManuallyPaused => pause.Pending;
    internal long PauseDeadline => pause.Pending ? pause.Until : 0;
    internal GuardState State { get; private set; }
    internal string Status { get; private set; } = "已关闭（摄像头未开启）";
    internal bool AlertVisible => alerts.Visible;
    internal bool AlertBlackout => alerts.Blackout;
    internal int AlertWindowCount => alerts.Count;
    internal long Samples => Interlocked.Read(ref samples);
    internal double MeanProcessingMilliseconds => Samples == 0 ? 0 : Interlocked.Read(ref processingTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency / Samples;
    internal int LastFaces => Volatile.Read(ref lastFaces);
    internal event Action? Changed;
    internal GuardService(GuardOptionsStore options, DispatcherQueue dispatcher, IGuardCameraFactory? factory = null)
    {
        this.options = options; this.dispatcher = dispatcher; this.factory = factory ?? new GuardCameraFactory();
        cameraId = options.Data.CameraId; holdMilliseconds = options.Data.HoldMilliseconds; suspended = SessionState.Locked == true;
        options.Changed += OptionsChanged;
    }
    void OptionsChanged()
    {
        if (holdMilliseconds != options.Data.HoldMilliseconds) { holdMilliseconds = options.Data.HoldMilliseconds; trigger.RestartCandidate(); }
        if (cameraId != options.Data.CameraId)
        { cameraId = options.Data.CameraId; if (Wanted) _ = Restart(); }
        Changed?.Invoke();
    }
    internal Task SetEnabled(bool value)
    {
        if (stopped) return Task.CompletedTask;
        if (!value) App.Privacy.CancelGuardRestore();
        if (value && App.Privacy.BlocksGuard) { SetState(GuardState.Off, "隐私模式期间无法开启摄像头，请先恢复设备。"); return Task.CompletedTask; }
        pause.Clear(); pauseTimer?.Stop(); Wanted = value; return Restart();
    }
    internal Task RestorePaused(TimeSpan duration)
    {
        if (stopped || App.Privacy.BlocksGuard) return Task.CompletedTask;
        Wanted = true; return PauseFor(duration);
    }
    internal Task PauseFor(TimeSpan duration)
    {
        if (!Wanted || stopped) return Task.CompletedTask;
        long milliseconds = checked((long)duration.TotalMilliseconds);
        pause.Begin(Environment.TickCount64, milliseconds); pauseDisplayUntil = DateTimeOffset.Now.AddMilliseconds(milliseconds);
        if (pauseTimer == null)
        {
            pauseTimer = dispatcher.CreateTimer(); pauseTimer.IsRepeating = false;
            pauseTimer.Tick += async (_, _) =>
            {
                if (stopped || !Wanted || !pause.Pending) return;
                long remaining = pause.Until - Environment.TickCount64;
                if (remaining > 0) { pauseTimer.Interval = TimeSpan.FromMilliseconds(remaining); pauseTimer.Start(); }
                else await Resume();
            };
        }
        pauseTimer.Stop(); pauseTimer.Interval = TimeSpan.FromMilliseconds(milliseconds); pauseTimer.Start();
        return Restart();
    }
    internal Task Resume()
    {
        if (stopped || !Wanted || !pause.Pending) return Task.CompletedTask;
        pause.Clear(); pauseTimer?.Stop(); return Restart();
    }
    internal void SetSuspended(bool value)
    {
        if (stopped || suspended == value && (value || !Wanted || State != GuardState.Paused)) return;
        suspended = value; _ = Restart();
    }
    internal void DisplayChanged() { if (alerts.Visible) alerts.Refresh(); }
    Task Restart()
    {
        int epoch = ++generation;
        cancellation?.Cancel(); alerts.Hide(); alertShowing = false; trigger.Reset();
        SetState(!Wanted ? GuardState.Stopping : suspended || pause.Pending ? GuardState.Paused : GuardState.Starting,
            !Wanted ? "正在释放摄像头…" : pause.Pending ? $"暂停至 {pauseDisplayUntil:HH:mm}，正在释放摄像头…" : suspended ? "锁屏或睡眠，检测暂停" : "正在准备摄像头…");
        return Reconcile(epoch);
    }
    async Task Reconcile(int epoch)
    {
        await transitions.WaitAsync();
        try
        {
            await run;
            cancellation?.Dispose(); cancellation = null;
            if (epoch != generation) return;
            if (!Wanted || stopped) { SetState(GuardState.Off, "已关闭（摄像头未开启）"); return; }
            if (pause.Active(Environment.TickCount64)) { SetState(GuardState.Paused, $"已暂停至 {pauseDisplayUntil:HH:mm}（摄像头已释放）"); return; }
            if (pause.Pending) { pause.Clear(); pauseTimer?.Stop(); }
            if (suspended || SessionState.Locked == true) { SetState(GuardState.Paused, "锁屏或睡眠，检测暂停"); return; }
            SetState(GuardState.Starting, "正在准备摄像头…");
            cancellation = new CancellationTokenSource();
            run = Run(epoch, options.Data.CameraId, cancellation.Token);
        }
        finally { transitions.Release(); }
    }
    async Task Run(int epoch, string device, CancellationToken token)
    {
        try
        {
            // Initialize on the UI apartment for Windows camera permission handling;
            // only the bounded two-Hz inference loop runs on the thread pool.
            using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token);
            preparation.CancelAfter(TimeSpan.FromSeconds(15));
            await using var camera = await factory.Open(device, preparation.Token);
            token.ThrowIfCancellationRequested();
            await Task.Run(async () =>
            {
                long freshAt = Environment.TickCount64; string? lastStatus = null;
                string runningStatus = "检测中 · " + camera.Description;
                while (!token.IsCancellationRequested)
                {
                    bool cooling = trigger.Cooling(Environment.TickCount64), alert = trigger.Active || alertShowing;
                    if (cooling || alert)
                    {
                        freshAt = Environment.TickCount64;
                        string text = alert ? "已显示守护提醒，按 Enter 解除" : "解除后冷却 30 秒，人脸分析暂停";
                        if (text != lastStatus) { lastStatus = text; Post(epoch, () => SetState(GuardState.Running, text)); }
                        await Task.Delay(1000, token); continue;
                    }
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var sample = await camera.Sample(token);
                    if (sample.Fresh)
                    {
                        freshAt = Environment.TickCount64;
                        Interlocked.Increment(ref samples); Interlocked.Add(ref processingTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
                        Volatile.Write(ref lastFaces, sample.Faces);
                        string text = runningStatus;
                        if (text != lastStatus) { lastStatus = text; Post(epoch, () => SetState(GuardState.Running, text)); }
                        if (trigger.Observe(sample.Faces, Environment.TickCount64, options.Data.HoldMilliseconds)) Post(epoch, ShowDetectedAlert);
                    }
                    else if (Environment.TickCount64 - freshAt > 5000) throw new IOException("摄像头没有新画面，检测已暂停，请重新开启。");
                    await Task.Delay(500, token);
                }
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            string message = ex is UnauthorizedAccessException ? "无法访问摄像头，请在 Windows 中检查摄像头权限后重新开启。"
                : ex is IOException or InvalidOperationException ? ex.Message : "摄像头暂不可用，请检查权限、连接或占用情况后重新开启。";
            Post(epoch, () => { trigger.Reset(); alerts.Hide(); alertShowing = false; SetState(GuardState.Unavailable, message); });
        }
    }
    void Post(int epoch, Action action) => dispatcher.TryEnqueue(() => { if (!stopped && epoch == generation) action(); });
    void SetState(GuardState state, string status) { State = state; Status = status; Changed?.Invoke(); }
    void ShowDetectedAlert()
    {
        if (!Wanted || suspended || !trigger.Active) return;
        if (SessionState.Locked == true) { SetSuspended(true); return; }
        ShowAlert();
    }
    void ShowAlert()
    {
        try { alerts.Show(options.Data, Dismiss); alertShowing = alerts.Visible; Changed?.Invoke(); }
        catch { alertShowing = false; trigger.Dismiss(Environment.TickCount64); SetState(GuardState.Unavailable, "提醒窗口未能显示，请重新开启守护。"); cancellation?.Cancel(); }
    }
    internal void TestAlert()
    {
        if (stopped || alerts.Visible || SessionState.Locked == true) return;
        ShowAlert();
    }
    internal void Dismiss()
    {
        alerts.Hide(); alertShowing = false; trigger.Dismiss(Environment.TickCount64);
        if (!Wanted) SetState(GuardState.Off, "已关闭（摄像头未开启）");
        else Changed?.Invoke();
    }
    internal async Task Stop()
    {
        if (stopped) return;
        Wanted = false; stopped = true; pause.Clear(); pauseTimer?.Stop(); options.Changed -= OptionsChanged;
        await Restart();
    }
}
