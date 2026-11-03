using Microsoft.Win32;

namespace Liubai;

internal static class StartupService
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Liubai";
    internal static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string value && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }
    static string Command => "\"" + (Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Liubai.exe")) + "\"";
    internal static void RefreshExistingPath()
    {
        // Repair an already enabled registration after moving/updating the formal copy.
        // A missing registration stays disabled; diagnostics never call this method.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            if (key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, Command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { /* Startup registration must not prevent the desktop bar from opening. */ }
    }
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
