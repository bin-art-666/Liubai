using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Liubai;

internal sealed partial class SettingsWindow
{
    internal async Task VerifyUpdateSettings(List<string> checks, bool network)
    {
        using var lease = KeepOpen(); long deadline = Environment.TickCount64 + 5000;
        while (!interactive) { if (Environment.TickCount64 > deadline) throw new Exception("Settings did not become interactive"); await Task.Delay(40); }
        if (!checkUpdate.IsEnabled || installUpdate.IsEnabled || App.Updates.Busy || App.Updates.Release != null) throw new Exception("Update check ran automatically or allowed unverified installation");
        checks.Add("Version/update/recovery controls use native settings; checking is manual; installation disabled before check");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","UpdateWorker.ps1"))) throw new Exception("Maintenance helper missing from published runtime");
        checks.Add("Published runtime includes maintenance helper");
        bool previousAutomatic = automatic.IsOn; string previousColor = colorInput.Text;
        syncing = true; automatic.IsOn = false; colorInput.Text = "invalid"; syncing = false;
        bool saved = await SaveAllSettings();
        syncing = true; automatic.IsOn = previousAutomatic; colorInput.Text = previousColor; syncing = false;
        if (saved) throw new Exception("Invalid visible color did not block maintenance save");
        checks.Add("Failed settings validation prevents update/restart rather than silently discarding editor changes");
        if (!await SaveAllSettings()) throw new Exception("Valid settings could not be saved");
        checks.Add("Valid settings flush before restart");
        if (network)
        {
            await App.Updates.Check();
            if (App.Updates.Release == null || App.Updates.Busy) throw new Exception("Live GitHub check failed: " + App.Updates.Status + " " + App.Updates.Diagnostic);
            if (App.Updates.Release.Version <= UpdateService.CurrentVersion && installUpdate.IsEnabled) throw new Exception("Same/older release could replace local changes");
            checks.Add("Live GitHub latest release metadata and SHA-256 parsed; no package downloaded or installed");
        }
    }
}

public sealed partial class BarWindow
{
    async Task VerifyUpdates()
    {
        string result = "PASS", error = ""; var checks = new List<string>();
        try
        {
            await Task.Delay(350);
            if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before update panel verification");
            OpenSettings();
            await settingsWindow!.VerifyUpdateSettings(checks,Environment.GetCommandLineArgs().Contains("--verify-updates-network"));
            string runtime = Path.Combine(App.DataDirectory,"ready-fixture","app");Directory.CreateDirectory(runtime);
            foreach(string name in UpdatePolicy.Required) File.WriteAllText(Path.Combine(runtime,name),"fixture");
            var layout = new UpdateLayout(runtime); string id = Guid.NewGuid().ToString("N");
            UpdateService.MarkReady(["--maintenance-ready",id],layout);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(layout.Root,"ready-"+id+".json")));
            if (json.RootElement.GetProperty("pid").GetInt32()!=Environment.ProcessId) throw new Exception("Startup confirmation has incorrect process identity");
            checks.Add("Readiness handshake written only to owned maintenance root after initialization");
            UpdateService.MarkReady(["--maintenance-ready","../../invalid"],layout);
            if (Directory.GetFiles(layout.Root,"ready-*.json").Length!=1) throw new Exception("Invalid readiness token accepted");
            checks.Add("Invalid startup tokens cannot write a readiness file");
        }
        catch(Exception ex) {result="FAIL";error=ex.ToString();}
        finally
        {
            settingsWindow?.Hide();
            File.WriteAllText(Path.Combine(App.DataDirectory,"updates-check.json"),JsonSerializer.Serialize(new{result,error,checks}));
            Close();
        }
    }
}
