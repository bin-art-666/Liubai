using Microsoft.UI.Xaml;

namespace Liubai;

public partial class App : Application
{
    Mutex? instance;
    BarManager? manager;
    BarWindow? verificationBar;
    internal static string DataDirectory { get; private set; } = "";
    static PlannerStore? planner;
    static StationStore? station;
    internal static StationStore Station => station ??= new StationStore(Path.Combine(DataDirectory, "station.json"));
    internal static Task FlushStation() => station?.Flush() ?? Task.CompletedTask;
    internal static PlannerStore Planner => planner ??= new PlannerStore(Path.Combine(DataDirectory, "planner.json"));
    static ScreenshotOptionsStore? screenshots;
    internal static ScreenshotOptionsStore Screenshots => screenshots ??= new(Path.Combine(DataDirectory, "screenshot.json"));
    internal static ReminderService? Reminders { get; private set; }
    internal static ReminderService StartReminders(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Guid, bool> open, bool verification = false)
        => Reminders ??= new ReminderService(Planner, dispatcher, open, verification);

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--startup")) StartupService.Log("failed", e.Exception);
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(Path.Combine(DataDirectory, "error.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n");
            }
            catch { /* Logging must not mask the original exception. */ }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--data-dir");
        DataDirectory = index >= 0 && index + 1 < arguments.Length
            ? Path.GetFullPath(arguments[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai.Bar");
        bool startupLaunch = arguments.Contains("--startup");
        if (startupLaunch) StartupService.Log("launch");
        instance = new Mutex(true, "Local\\Liubai.GlassBar", out bool first);
        if (!first) { if (startupLaunch) StartupService.Log("already-running"); Exit(); return; }
        if (index < 0 && !arguments.Any(a => a.StartsWith("--verify-", StringComparison.Ordinal))) StartupService.RefreshExistingPath();
        var preferences = new AppearanceStore(Path.Combine(DataDirectory, "appearance.json"));
        if (arguments.Contains("--verify-expanded") || arguments.Contains("--verify-shell") || arguments.Contains("--verify-planner") || arguments.Contains("--verify-reminders") || arguments.Contains("--verify-screenshot") || arguments.Contains("--verify-qr") || arguments.Contains("--verify-station"))
        {
            verificationBar = new BarWindow(preferences);
            verificationBar.Start(arguments.Contains("--show-settings"));
        }
        else
        {
            manager = new BarManager(preferences, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            manager.Start(arguments.Contains("--show-settings"));
            if (startupLaunch) StartupService.Log("ready");
            if (arguments.Contains("--verify-desktop")) _ = manager.VerifyDesktop();
            else if (index < 0)
                StartReminders(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), id => manager.RequestReminder(id));
        }
    }
}
