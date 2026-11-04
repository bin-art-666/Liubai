using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Liubai;

internal static class StartupService
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Liubai";
    static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Liubai.exe");
    static string Sid
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("无法识别当前用户。"); }
    }
    internal static string TaskName => "Liubai-" + Sid;
    static string AccountName { get { using var identity = WindowsIdentity.GetCurrent(); return identity.Name; } }
    internal static string? RegistrationWarning { get; private set; }
    internal static bool Enabled => WithFolder(folder =>
    {
        object? task = FindTask(folder);
        try { return task is not null ? (bool)((dynamic)task).Enabled && StartupDefinition.Matches((string)((dynamic)task).Xml, Executable, Sid, AccountName) : LegacyEnabled(); }
        finally { Release(task); }
    });
    static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
            ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally { Release(folder); Release(service); }
    }
    static object? FindTask(dynamic folder)
    {
        try { return folder.GetTask(TaskName); }
        catch (FileNotFoundException) { return null; }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { return null; }
    }
    static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    static bool LegacyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key?.GetValue(ValueName) is not string value || string.IsNullOrWhiteSpace(value)) return false;
        using var approval = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        return approval?.GetValue(ValueName) is not byte[] state || state.Length == 0 || state[0] is not (3 or 7);
    }
    static void RemoveLegacy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        key?.DeleteValue(ValueName, false);
    }
    static void Register(dynamic folder, bool enabled)
    {
        if (!File.Exists(Executable)) throw new FileNotFoundException("找不到留白程序，请保留完整的程序目录。", Executable);
        var xml = System.Xml.Linq.XDocument.Parse(StartupDefinition.Build(Executable, Sid));
        var ns = xml.Root!.Name.Namespace;
        xml.Root.Element(ns + "Settings")!.Element(ns + "Enabled")!.Value = enabled ? "true" : "false";
        object task = folder.RegisterTask(TaskName, xml.ToString(), 6, null, null, 3, null); // CREATE_OR_UPDATE, INTERACTIVE_TOKEN
        Release(task);
    }
    internal static void RefreshExistingPath()
    {
        // Only migrate an enabled old entry; an explicitly disabled task stays disabled.
        try
        {
            WithFolder(folder =>
            {
                object? task = FindTask(folder);
                try
                {
                    if (task is not null)
                    {
                        if (!StartupDefinition.Matches((string)((dynamic)task).Xml, Executable, Sid, AccountName)) Register(folder, (bool)((dynamic)task).Enabled);
                        RemoveLegacy();
                    }
                    else if (LegacyEnabled()) { Register(folder, true); RemoveLegacy(); }
                    return true;
                }
                finally { Release(task); }
            });
            RegistrationWarning = null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { RegistrationWarning = "开机启动修复未完成，请重新开启开机启动。"; Log("registration-failed", ex); }
    }
    internal static void SetEnabled(bool enabled)
    {
        WithFolder(folder =>
        {
            object? task = FindTask(folder);
            try
            {
                if (enabled) Register(folder, true);
                else if (task is not null) ((dynamic)task).Enabled = false;
                RemoveLegacy(); return true;
            }
            finally { Release(task); }
        });
        RegistrationWarning = null;
    }
    internal static void Log(string stage, Exception? exception = null)
    {
        try
        {
            string path = Path.Combine(App.DataDirectory, "startup.log");
            Directory.CreateDirectory(App.DataDirectory);
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.WriteAllText(path, "");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {stage}" + (exception is null ? "" : $" {exception.GetType().Name} 0x{exception.HResult:X8}") + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { }
    }
}
