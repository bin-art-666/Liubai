using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Liubai;

internal sealed class UpdateService
{
    internal static Version CurrentVersion => new(typeof(App).Assembly.GetName().Version!.ToString(3));
    internal static string VersionLabel => CurrentVersion.ToString(3);
    internal UpdateLayout Layout { get; }
    readonly Func<HttpClient> createClient;
    internal UpdateService(Func<HttpClient>? createClient = null, UpdateLayout? layout = null)
    { this.createClient = createClient ?? Client; Layout = layout ?? new(AppContext.BaseDirectory); }
    internal UpdateRelease? Release { get; private set; }
    internal string Status { get; private set; } = "手动检查 GitHub 正式发布；不会后台下载或自动重启。";
    internal string? Diagnostic { get; private set; }
    internal bool Busy { get; private set; }
    internal bool CanCancel => cancellation != null;
    internal void Applying(bool value) { Busy = value; Changed?.Invoke(); }
    internal bool CanInstall => Layout.Supported && !Busy && Release?.Version > CurrentVersion;
    CancellationTokenSource? cancellation;
    internal event Action? Changed;
    internal string? PreviousVersion
    {
        get
        {
            try
            {
                if (!Directory.Exists(Layout.Previous)) return null;
                Layout.EnsureOwned();
                string metadata = Path.Combine(Layout.Root, "previous.json");
                if (!File.Exists(metadata)) return null;
                using var json = JsonDocument.Parse(File.ReadAllText(metadata));
                UpdatePolicy.ValidateRuntime(Layout.Previous);
                if (!UpdatePolicy.Hash(Path.Combine(Layout.Previous, "Liubai.exe")).Equals(json.RootElement.GetProperty("exeSha256").GetString(), StringComparison.OrdinalIgnoreCase)) return null;
                var hashes = json.RootElement.GetProperty("hashes");
                foreach (string name in UpdatePolicy.Required)
                    if (!UpdatePolicy.Hash(Path.Combine(Layout.Previous, name)).Equals(hashes.GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase)) return null;
                return json.RootElement.GetProperty("version").GetString();
            }
            catch { return null; }
        }
    }
    internal string LastResult()
    {
        try
        {
            string path = Path.Combine(Layout.Root, "result.json"); if (!File.Exists(path)) return "";
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("state").GetString() switch
            {
                "success" => "上次更新或回退已完成。", "failed" => "上次操作未完成，已保留原程序或恢复上一版本。可打开维护目录查看记录。",
                "unconfirmed" => "上次启动尚未确认，回退副本已保留；请先确认当前程序可正常使用。", "applying" => "上次更新未完成，请打开维护目录查看记录。", _ => ""
            };
        }
        catch { return "更新记录无法读取，当前程序不会被自动替换。"; }
    }
    static HttpClient Client()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Liubai/" + VersionLabel);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); return client;
    }
    internal void Cancel() => cancellation?.Cancel();
    internal void Abandon(UpdatePlan plan)
    {
        if (plan.Mode != "update") return;
        string stage = Path.Combine(Layout.Root, "stage-" + plan.Id);
        if (stage != plan.Stage || !Guid.TryParseExact(plan.Id, "N", out _)) return;
        try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { }
    }
    void Notify(string status) { Status = status; Changed?.Invoke(); }
    void LogFailure(Exception exception)
    {
        Diagnostic = exception.ToString();
        try
        {
            Directory.CreateDirectory(App.DataDirectory); string path = Path.Combine(App.DataDirectory,"update.log");
            if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path,"");
            File.AppendAllText(path,DateTimeOffset.Now.ToString("O")+" "+Diagnostic+Environment.NewLine);
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    internal async Task Check()
    {
        if (Busy) return; Busy = true; Diagnostic = null; cancellation = new(TimeSpan.FromSeconds(25)); Notify("正在检查正式发布…");
        try
        {
            using var client = createClient(); using var response = await client.GetAsync(UpdatePolicy.Latest, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests) throw new IOException("GitHub 请求暂时受限，请稍后重试。");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new IOException("更新信息过大。");
            using var input = await response.Content.ReadAsStreamAsync(cancellation.Token); using var data = new MemoryStream(); var buffer = new byte[16384]; int count;
            while ((count = await input.ReadAsync(buffer, cancellation.Token)) != 0)
            { if (data.Length + count > 2 * 1024 * 1024) throw new IOException("更新信息过大。"); await data.WriteAsync(buffer.AsMemory(0, count), cancellation.Token); }
            Release = UpdatePolicy.Parse(System.Text.Encoding.UTF8.GetString(data.ToArray()));
            Notify(Release.Version > CurrentVersion ? "发现 " + Release.Tag + "，可查看说明后下载。" : Release.Version == CurrentVersion
                ? "发布版本与当前版本号相同。本地未发布改动不会被替换。" : "当前版本高于最新正式发布，不提供自动降级。");
        }
        catch (OperationCanceledException) { Release = null; Notify("检查已取消或超时，可重新检查。"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { LogFailure(ex); Release = null; Notify("检查未完成：" + ex.Message + " 可重试或打开本机日志目录查看 update.log。"); }
        finally { Busy = false; cancellation.Dispose(); cancellation = null; Changed?.Invoke(); }
    }
    internal async Task<UpdatePlan?> Prepare(bool rollback)
    {
        if (Busy) return null;
        Busy = true; cancellation = new(); Changed?.Invoke();
        string id = Guid.NewGuid().ToString("N"), stage = Path.Combine(Layout.Root, "stage-" + id), zip = Path.Combine(Layout.Root, "download-" + id + ".zip");
        try
        {
            Layout.EnsureOwned();
            using (File.Open(Path.Combine(Layout.Root, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            string target;
            if (rollback)
            {
                target = PreviousVersion ?? throw new IOException("没有可用的回退副本。"); stage = Layout.Previous;
                Notify("正在核对回退副本…");
            }
            else
            {
                var release = Release ?? throw new IOException("请先检查更新。");
                if (release.Version <= CurrentVersion) throw new IOException("不安装相同版本或更旧版本。");
                using var client = createClient(); var uri = new Uri(release.Download);
                HttpResponseMessage? response = null;
                try
                {
                    for (int redirects = 0; redirects <= 5; redirects++)
                    {
                        if (!UpdatePolicy.DownloadHost(uri)) throw new IOException("更新下载跳转来源无效。");
                        response?.Dispose(); response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
                        if ((int)response.StatusCode is >= 300 and < 400)
                        { uri = new Uri(uri, response.Headers.Location ?? throw new IOException("更新下载跳转无效。")); continue; }
                        response.EnsureSuccessStatusCode(); break;
                    }
                    if (response == null || !response.IsSuccessStatusCode) throw new IOException("更新下载跳转次数过多。");
                    if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new IOException("更新包大小不匹配。");
                    using var input = await response.Content.ReadAsStreamAsync(cancellation.Token);
                    using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536]; long received = 0; int read, last = -1;
                        while ((read = await input.ReadAsync(buffer, cancellation.Token)) != 0)
                        {
                            received += read; if (received > release.Size) throw new IOException("更新下载超出预期大小。");
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                            int progress = (int)(received * 100 / release.Size); if (progress != last) { last = progress; Notify($"正在下载 {release.Tag} · {progress}%"); }
                        }
                    }
                    Notify("正在校验并准备更新…");
                    var token = cancellation.Token;
                    await Task.Run(() => { UpdatePolicy.VerifyPackage(zip, release); UpdatePolicy.Extract(zip, stage, token); }, token);
                    target = release.Version.ToString(3);
                }
                finally { response?.Dispose(); }
            }
            UpdatePolicy.ValidateRuntime(stage);
            using var process = Process.GetCurrentProcess();
            return new(1, id, rollback ? "rollback" : "update", Layout.Install, App.DataDirectory, stage,
                process.Id, process.StartTime.ToUniversalTime().Ticks.ToString(), UpdatePolicy.Hash(Path.Combine(stage, "Liubai.exe")), target,
                !Path.GetFullPath(App.DataDirectory).Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai.Bar"), StringComparison.OrdinalIgnoreCase),
                VersionLabel, UpdatePolicy.Required.ToDictionary(name => name, name => UpdatePolicy.Hash(Path.Combine(stage, name))));
        }
        catch (OperationCanceledException) { Notify("已取消，当前程序未更改。"); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or JsonException or ArgumentException)
        { LogFailure(ex); Notify("准备未完成：" + ex.Message); return null; }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch (IOException) { }
            // A completed stage is retained only until installation or explicit cancellation.
            Busy = false; cancellation.Dispose(); cancellation = null; Changed?.Invoke();
        }
    }
    internal async Task LaunchWorker(UpdatePlan plan)
    {
        Layout.EnsureOwned();
        await Task.Run(() => UpdateLayout.Snapshot(plan.DataRoot, Path.Combine(Layout.Root, "settings-" + plan.Id)));
        string worker = Path.Combine(Layout.Root, "worker.ps1"), path = Path.Combine(Layout.Root, "plan-" + plan.Id + ".json");
        File.Copy(Path.Combine(Layout.Install, "Assets", "UpdateWorker.ps1"), worker, true);
        File.WriteAllText(path, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Layout.Root };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", worker, "-PlanPath", path }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("无法启动更新助手。");
        string handshake = Path.Combine(Layout.Root, "started-" + plan.Id + ".json"); long deadline = Environment.TickCount64 + 10000;
        while (!File.Exists(handshake) && !child.HasExited && Environment.TickCount64 < deadline) await Task.Delay(100);
        if (!File.Exists(handshake)) throw new IOException("更新助手未能启动，当前程序保持运行。请打开维护目录查看记录。");
    }
    internal void Commit(UpdatePlan plan) => File.WriteAllText(Path.Combine(Layout.Root, "commit-" + plan.Id + ".json"), JsonSerializer.Serialize(new { id = plan.Id }));
    internal static void MarkReady(string[] arguments, UpdateLayout? target = null)
    {
        int index = Array.IndexOf(arguments, "--maintenance-ready");
        if (index < 0 || index + 1 >= arguments.Length || !Guid.TryParseExact(arguments[index + 1], "N", out _)) return;
        try
        {
            var layout = target ?? new UpdateLayout(AppContext.BaseDirectory); layout.EnsureOwned();
            File.WriteAllText(Path.Combine(layout.Root, "ready-" + arguments[index + 1] + ".json"), JsonSerializer.Serialize(new { id = arguments[index + 1], pid = Environment.ProcessId }));
        }
        catch { /* Worker retains recovery copy if readiness cannot be confirmed. */ }
    }
}
