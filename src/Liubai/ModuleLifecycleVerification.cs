namespace Liubai;

internal sealed partial class StationWindow
{
    internal IDisposable ProtectImportFixture() => KeepOpen();
    internal void VerifyReleasedView()
    {
        if (IsOpen || cards.Children.Count != 0 || previewCache.Count != 0 || previewCancellation != null)
            throw new Exception("Hidden station retained preview cards, images or loading tasks");
    }
}

internal sealed partial class PlannerWindow
{
    internal async Task VerifyReleasedView()
    {
        long deadline = Environment.TickCount64 + 3000;
        while (taskList.Children.Count != 0 || noteList.Children.Count != 0 || noteImages.Children.Count != 0)
        {
            if (Environment.TickCount64 > deadline) throw new Exception("Hidden planner retained rendered lists or attachments");
            await Task.Delay(30);
        }
        if (IsOpen) throw new Exception("View release assertion requires a hidden panel");
    }
}

internal sealed partial class SettingsWindow
{
    internal async Task VerifyModuleSettings()
    {
        using var lease = KeepOpen();
        await Task.Delay(500); App.VerifyColdFeatures();
        App.SelectAi("deepseek"); App.VerifyColdFeatures();
        Hide();
    }
}

public partial class App
{
    internal static void VerifyColdFeatures()
    {
        if (CurrentAi != null || CurrentGuard != null || station != null)
            throw new Exception("Toolbar state reads initialized unused feature services or station data");
        HideAi();
        CurrentGuard?.SetSuspended(true); CurrentGuard?.DisplayChanged();
        if (CurrentAi != null || CurrentGuard != null) throw new Exception("Passive lifecycle operations constructed services");
    }
}

public sealed partial class BarWindow
{
    async Task VerifyModules()
    {
        var checks = new List<string>(); string result = "PASS", error = "";
        try
        {
            App.VerifyColdFeatures();
            checks.Add("Cold toolbar: AI/guard services and station data remain uninitialized; hide/display/suspend are passive");
            OpenSettings();
            await settingsWindow!.VerifyModuleSettings();
            checks.Add("Opening all settings reads metadata without creating AI/guard or station data");
            checks.Add("Changing AI provider before first use does not create a web service");
            var store = new WorkStore(Path.Combine(App.DataDirectory, "module-work.json"));
            int stationLoads = 0;
            var planner = new PlannerStore(Path.Combine(App.DataDirectory, "module-planner.json"));
            var station = new StationStore(Path.Combine(App.DataDirectory, "module-station.json"));
            var hub = new WorkHub(store, () => planner, () => { stationLoads++; return station; });
            hub.Attach(planner);
            await store.Create("按需加载检查");
            await planner.AddTask("模块联动", "", null);
            await hub.Flush();
            if (stationLoads != 0) throw new Exception("Planner attachment loaded station data");
            if (store.Data.Scenes.Single().Items.Length != 1) throw new Exception("Lazy attachment missed active scene changes");
            checks.Add("Work tracking attaches one data source without loading another");
            var service = App.Ai;
            if (!ReferenceEquals(service.Options, App.AiOptions) || service.IsOpen || service.Busy) throw new Exception("Service metadata differs or allocated browser before Open");
            App.StopAi();
            if (App.CurrentAi != null) throw new Exception("AI service was not released");
            if (ReferenceEquals(App.Ai, service) || !ReferenceEquals(App.Ai.Options, App.AiOptions)) throw new Exception("AI retry lost options or reused stopped service");
            App.StopAi();
            checks.Add("AI release/recreate retains shared options and creates no browser before Open");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            App.StopAi();
            File.WriteAllText(Path.Combine(App.DataDirectory, "modules-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
