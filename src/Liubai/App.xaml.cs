using Microsoft.UI.Xaml;

namespace Liubai;

public partial class App : Application
{
    Mutex? instance;
    BarManager? manager;
    BarWindow? verificationBar;
    internal static string DataDirectory { get; private set; } = "";
    static UiStyleStore? uiStyle;
    internal static UiStyleStore UiStyle => uiStyle ??= new(Path.Combine(DataDirectory, "ui-style.json"));
    static ToolbarLayoutStore? toolbarLayout;
    internal static ToolbarLayoutStore ToolbarLayout => toolbarLayout ??= new(Path.Combine(DataDirectory, "toolbar-layout.json"));
    static PlannerStore? planner;
    static StationStore? station;
    static ClockStore? clock;
    internal static ClockStore Clock => clock ??= new ClockStore(Path.Combine(DataDirectory, "clock.json"));
    internal static ClockService? Clocks { get; private set; }
    static readonly Queue<string> pendingClockActions = new();
    internal static void DispatchClockAction(string argument)
    {
        if (!ClockPolicy.TryParse(argument, out _)) return;
        if (Clocks != null) _ = Clocks.Handle(argument);
        else if (pendingClockActions.Count < 32) pendingClockActions.Enqueue(argument);
    }
    internal static ClockService StartClocks(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Guid, bool> open, bool initiallySuspended = false)
    {
        if (Clocks == null)
        {
            var reminders = Reminders ?? throw new InvalidOperationException("Notification service is not initialized.");
            Clocks = new ClockService(Clock, reminders.ClockNotifications, dispatcher, open, initiallySuspended);
            while (pendingClockActions.TryDequeue(out var argument)) _ = Clocks.Handle(argument);
        }
        return Clocks;
    }
    internal static StationStore Station
    {
        get { if (station == null) { station = new(Path.Combine(DataDirectory, "station.json")); Work.Attach(station); } return station; }
    }
    internal static Task FlushStation() => station?.Flush() ?? Task.CompletedTask;
    static GuardOptionsStore? guardOptions;
    internal static GuardOptionsStore GuardOptions => guardOptions ??= new(Path.Combine(DataDirectory, "guard.json"));
    internal static GuardService Guard => guardModule.Get();
    internal static Task StopGuard() => CurrentGuard?.Stop() ?? Task.CompletedTask;
    static PrivacyService? privacy;
    internal static PrivacyService Privacy => privacy ??= new(DataDirectory);
    internal static AiService Ai => aiModule.Get();
    internal static void StopAi() => aiModule.Release(service => service.Stop());
    internal static PlannerStore Planner
    {
        get { if (planner == null) { planner = new(Path.Combine(DataDirectory, "planner.json")); Work.Attach(planner); } return planner; }
    }
    static WorkHub? work;
    static WorkStore? workStore;
    internal static WorkStore WorkStore => workStore ??= new(Path.Combine(DataDirectory, "workspaces.json"));
    internal static WorkHub Work => work ??= new(WorkStore, () => Planner, () => Station);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StationStore,StationHistory> histories=new();
    internal static StationHistory HistoryFor(StationStore source)=>histories.GetValue(source,s=>new(s,Work));
    internal static Task FlushWork() => work?.Flush() ?? workStore?.Flush() ?? Task.CompletedTask;
    internal static event Action<Guid, string>? FocusRequested;
    internal static void RequestFocus(Guid task, string display) => FocusRequested?.Invoke(task, display);
    internal static event Action<Guid,string>? WorkspaceRequested;
    internal static void RequestWorkspace(Guid group,string display)=>WorkspaceRequested?.Invoke(group,display);
    static ScreenshotOptionsStore? screenshots;
    internal static ScreenshotOptionsStore Screenshots => screenshots ??= new(Path.Combine(DataDirectory, "screenshot.json"));
    internal static ReminderService? Reminders { get; private set; }
    internal static ReminderService StartReminders(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Guid, bool> open, bool verification = false, bool initiallySuspended = false)
        => Reminders ??= new ReminderService(Planner, dispatcher, open, verification, initiallySuspended);

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
        int workerIndex = Array.IndexOf(arguments, "--privacy-worker");
        if (workerIndex >= 0)
        {
            Environment.ExitCode = workerIndex + 1 < arguments.Length ? PrivacyService.Worker(arguments[workerIndex + 1]) : 2;
            Exit(); return;
        }
        int index = Array.IndexOf(arguments, "--data-dir");
        DataDirectory = index >= 0 && index + 1 < arguments.Length
            ? Path.GetFullPath(arguments[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai.Bar");
        bool startupLaunch = arguments.Contains("--startup");
        if (startupLaunch) StartupService.Log("launch");
        instance = new Mutex(true, "Local\\Liubai.GlassBar", out bool first);
        if (!first) { if (startupLaunch) StartupService.Log("already-running"); Exit(); return; }
        PanelTheme.Apply(UiStyle.Data);
        if (index < 0 && !arguments.Any(a => a.StartsWith("--verify-", StringComparison.Ordinal))) StartupService.RefreshExistingPath();
        var preferences = new AppearanceStore(Path.Combine(DataDirectory, "appearance.json"));
        if (arguments.Contains("--verify-station-experience") || arguments.Contains("--verify-auto-hide") || arguments.Contains("--verify-workbench") || arguments.Contains("--verify-tool-host") || arguments.Contains("--verify-updates") || arguments.Contains("--verify-updates-network") || arguments.Contains("--verify-modules") || arguments.Contains("--verify-toolbar-layout") || arguments.Contains("--verify-ui-style") || arguments.Contains("--verify-workflow") || arguments.Contains("--verify-station-browser") || arguments.Contains("--verify-station-size") || arguments.Contains("--verify-ai-panel") || arguments.Contains("--verify-privacy") || arguments.Contains("--verify-guard") || arguments.Contains("--verify-toolbar") || arguments.Contains("--verify-expanded") || arguments.Contains("--verify-shell") || arguments.Contains("--verify-planner") || arguments.Contains("--verify-reminders") || arguments.Contains("--verify-screenshot") || arguments.Contains("--verify-qr") || arguments.Contains("--verify-station") || arguments.Contains("--verify-clock"))
        {
            verificationBar = new BarWindow(preferences);
            verificationBar.Start(arguments.Contains("--show-settings"));
        }
        else
        {
            manager = new BarManager(preferences, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            manager.Start(arguments.Contains("--show-settings"));
            if(arguments.Contains("--preview-ai-panel"))_ = Ai.PreviewViewport(Native.Monitors().First(m=>m.Primary),Native.PrimaryMonitor().Work.Top);
            if (startupLaunch) StartupService.Log("ready");
            if (arguments.Contains("--verify-manager-quit")) _ = manager.VerifyManagerQuit();
            else if (arguments.Contains("--verify-desktop")) _ = manager.VerifyDesktop();
            else if (index < 0)
            {
                StartReminders(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), id => manager.RequestReminder(id), initiallySuspended: manager.Suspended);
                StartClocks(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), id => manager.RequestClock(id), manager.Suspended);
            }
        }
        UpdateService.MarkReady(arguments);
    }
}
