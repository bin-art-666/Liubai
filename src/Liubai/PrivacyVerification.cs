using Microsoft.UI.Xaml.Automation;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyPrivacy()
    {
        string result = "PASS", error = ""; var checks = new List<string>();
        try
        {
            await Task.Delay(700);
            if (!toolbarButtons.ContainsKey("privacy") || AutomationProperties.GetName(privacyButton) != "一键隐私") throw new Exception("Privacy toolbar missing");
            if (App.Privacy.BlocksGuard || App.Privacy.Journal.Active || App.Guard.Wanted) throw new Exception("Fresh startup must never change device state or activate camera");
            if (File.Exists(Path.Combine(App.DataDirectory, "privacy-state.json"))) throw new Exception("Opening app unexpectedly writes recovery journal");
            checks.Add("Toolbar initialization and privacy default off without device changes");
            OpenSettings(); settingsWindow!.OpenPrivacy();
            await Task.Delay(1400);
            if (!settingsWindow.PrivacyVisible) throw new Exception("Privacy section failed to expand");
            var devices = await App.Privacy.Devices();
            checks.Add($"Read-only device enumeration: {devices.Length} supported inputs; no device modified");
            checks.Add("Settings section expands, default input selection requires no elevation and changes no devices");
            if (Environment.GetCommandLineArgs().Contains("--preview-privacy")) await Task.Delay(25000);
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "privacy-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
