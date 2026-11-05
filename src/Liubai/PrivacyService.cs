using System.ComponentModel;
using System.Diagnostics;

namespace Liubai;

internal sealed class PrivacyService
{
    readonly string directory;
    string JournalPath => Path.Combine(directory, "privacy-state.json");
    string OptionsPath => Path.Combine(directory, "privacy-options.json");
    internal PrivacyJournal Journal { get; private set; } = new(Entries: []);
    internal PrivacyOptions Options { get; private set; } = new(Devices: []);
    bool selectionConfigured;
    internal string[] SelectedDevices(PrivacyDevice[] available) => PrivacySelection.Resolve(Options, selectionConfigured, available);
    internal bool Busy { get; private set; }
    internal bool Broken { get; private set; }
    internal bool Verified { get; private set; }
    internal IReadOnlyDictionary<string, PrivacyDevice> Observed { get; private set; } = new Dictionary<string, PrivacyDevice>(StringComparer.OrdinalIgnoreCase);
    internal bool BlocksGuard => Busy || Broken || Journal.Active;
    internal string Status { get; private set; } = "未开启；尚未更改设备。";
    internal event Action? Changed;
    bool restoreGuard;
    long guardPauseUntil;
    internal void CancelGuardRestore() => restoreGuard = false;
    internal PrivacyService(string directory)
    {
        this.directory = directory;
        try
        {
            selectionConfigured = File.Exists(OptionsPath);
            Options = PrivacyFiles.Read(OptionsPath, Options);
            ValidateOptions(Options);
            Reload();
        }
        catch { Broken = true; Status = "隐私记录无法读取，已停止自动操作。请在设备管理器检查设备；原记录已保留。"; }
    }
    static void ValidateOptions(PrivacyOptions options)
    {
        if (options.Version != 1 || options.Devices is null || options.Devices.Length > 128 || options.Devices.Any(id => !PrivacyFiles.ValidId(id)))
            throw new InvalidDataException("隐私设备设置无效。");
    }
    internal void SaveSelection(IEnumerable<string> ids)
    {
        if (Busy || Broken || Journal.Active) throw new IOException("请先恢复设备，再修改选择。");
        var options = new PrivacyOptions(Devices: ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()); ValidateOptions(options);
        PrivacyFiles.Write(OptionsPath, options); Options = options; selectionConfigured = true; Changed?.Invoke();
    }
    void Reload()
    {
        Journal = PrivacyFiles.Read(JournalPath, new PrivacyJournal(Entries: [])); PrivacyFiles.Validate(Journal);
        Status = Journal.Active ? "隐私设备记录已保留；点击恢复或在详情中核验。定位需单独在系统中关闭。" : "未开启；设备恢复记录已处理。";
    }
    internal async Task<PrivacyDevice[]> Devices()
    {
        var devices = await Task.Run(() => new PrivacyNative().List());
        Observed = devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        Verified = Journal.Active && Journal.Entries!.All(e => e.State is "disabled" or "restored" && (e.State == "restored" || Observed.TryGetValue(e.Id, out var d) && d.Disabled));
        if (Journal.Active && !Busy) Status = Verified ? "选中设备已核验禁用。定位需单独在系统中关闭。" : "部分设备状态未确认或已变化；请查看设备详情。定位未由留白关闭。";
        Changed?.Invoke(); return devices;
    }
    internal async Task Toggle()
    {
        if (Busy || Broken) return;
        Busy = true; Status = "正在准备，请完成 Windows 管理员授权…"; Changed?.Invoke();
        bool restore = Journal.Active, launched = false;
        try
        {
            if (!string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai.Bar"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("隔离检查环境不执行真实设备操作。");
            if (!restore)
            {
                var devices = await Devices();
                string[] ids = SelectedDevices(devices);
                var selected = ids.Select(id => devices.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (selected.Length == 0) throw new IOException("请先在设置 → 隐私保护中选择摄像头和麦克风。");
                if (selected.Any(d => d is null)) throw new IOException("部分所选设备未连接，请刷新设备列表后重试。");
                var entries = selected.OfType<PrivacyDevice>().Where(d => d.Enabled).Select(d => new PrivacyEntry(d.Id, d.Name, d.Kind)).ToArray();
                if (selected.Any(d => !d!.Enabled && !d.Disabled)) throw new IOException("部分设备状态异常，未更改任何设备，请先检查设备管理器。");
                if (!selectionConfigured)
                {
                    var options = new PrivacyOptions(Devices: ids); ValidateOptions(options);
                    PrivacyFiles.Write(OptionsPath, options); Options = options; selectionConfigured = true;
                }
                // Take the durable snapshot before releasing guard or requesting elevation.
                PrivacyFiles.Write(JournalPath, new PrivacyJournal(Active: true, Entries: entries)); Reload();
                bool wanted = App.Guard.Wanted; guardPauseUntil = App.Guard.PauseDeadline;
                await App.Guard.SetEnabled(false);
                restoreGuard = wanted;
            }
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--privacy-worker"); start.ArgumentList.Add(restore ? "restore" : "enter");
            using var worker = Process.Start(start) ?? throw new IOException("未能启动设备控制程序。");
            launched = true; await worker.WaitForExitAsync(); Reload();
            if (worker.ExitCode != 0) throw new IOException("设备操作未全部完成，请查看详情；恢复记录已保留。");
            if (restore ? Journal.Active : !Journal.Active || Journal.Entries!.Any(e => e.State != "disabled" && e.State != "restored"))
                throw new IOException("无法确认本用户的设备操作结果，恢复记录已保留。请使用当前用户的管理员授权。");
            if (restore && !Journal.Active) Status = "选中设备已恢复。定位设置请自行确认。";
            else if (!restore) Status = "选中设备已禁用并核验。定位需单独在系统中关闭。";
            await Devices();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (!restore && !launched && Journal.Active && Journal.Entries!.All(e => e.State == "planned"))
            { PrivacyFiles.Write(JournalPath, Journal with { Active = false }); Reload(); }
            Status = "已取消管理员授权，设备未更改。";
        }
        catch (Exception ex)
        {
            try { Reload(); } catch { Broken = true; }
            Status = ex is IOException ? ex.Message : "隐私操作失败，原始恢复记录已保留，请检查设备管理器。";
        }
        finally
        {
            Busy = false;
            if (!Broken && !Journal.Active && restoreGuard)
            {
                restoreGuard = false;
                if (guardPauseUntil > Environment.TickCount64) await App.Guard.RestorePaused(TimeSpan.FromMilliseconds(Math.Max(1, guardPauseUntil - Environment.TickCount64)));
                else await App.Guard.SetEnabled(true);
            }
            Changed?.Invoke();
        }
    }
    internal static int Worker(string operation)
    {
        if (operation is not ("enter" or "restore")) return 2;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai.Bar");
        // Only the fixed local profile journal is writable by this short-lived elevated process.
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 3;
        string path = Path.Combine(directory, "privacy-state.json");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return 3;
        using var mutex = new Mutex(false, "Local\\Liubai.Privacy.Operation");
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) return 4;
            var journal = PrivacyFiles.Read(path, new PrivacyJournal(Entries: []));
            var result = PrivacyOperation.Execute(journal, operation == "restore", new PrivacyNative(), value => PrivacyFiles.Write(path, value));
            return result.Entries!.Any(e => e.Error.Length > 0 && e.State != "restored") ? 1 : 0;
        }
        catch { return 1; }
        finally { if (held) mutex.ReleaseMutex(); }
    }
}
