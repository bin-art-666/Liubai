using Microsoft.UI.Xaml;

namespace Liubai;

public partial class App : Application
{
    Mutex? instance;
    BarManager? manager;
    BarWindow? verificationBar;
    internal static string DataDirectory { get; private set; } = "";

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
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
        instance = new Mutex(true, "Local\\Liubai.GlassBar", out bool first);
        if (!first) { Exit(); return; }
        if (index < 0 && !arguments.Any(a => a.StartsWith("--verify-", StringComparison.Ordinal))) StartupService.RefreshExistingPath();
        var preferences = new AppearanceStore(Path.Combine(DataDirectory, "appearance.json"));
        if (arguments.Contains("--verify-expanded") || arguments.Contains("--verify-shell"))
        {
            verificationBar = new BarWindow(preferences);
            verificationBar.Start(arguments.Contains("--show-settings"));
        }
        else
        {
            manager = new BarManager(preferences, Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            manager.Start(arguments.Contains("--show-settings"));
            if (arguments.Contains("--verify-desktop")) _ = manager.VerifyDesktop();
        }
    }
}
