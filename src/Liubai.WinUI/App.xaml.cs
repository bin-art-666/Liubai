using Microsoft.UI.Xaml;

namespace Liubai.Modern;

public partial class App : Application
{
    internal static bool Inspect;
    internal static bool IsExiting;
    internal static string DataFolder = "";
    internal static void Trace(string message)
    {
        if (!Inspect) return;
        try { Directory.CreateDirectory(DataFolder); File.AppendAllText(Path.Combine(DataFolder, "ui-check.log"), DateTime.Now.ToString("O") + " [" + Environment.ProcessId + "] " + message + Environment.NewLine); } catch { }
    }
    private System.Threading.Mutex? mutex;
    private DesktopBar? bar;
    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Trace("Process exit (managed cleanup)");
        UnhandledException += (_, e) =>
        {
            Trace("Unhandled exception: " + e.Exception);
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
            }
            catch { }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Trace("Launch callback");
        mutex = new System.Threading.Mutex(true, "Local\\Liubai.DesktopBar", out bool first);
        if (!first) { Trace("Duplicate instance exit"); Exit(); return; }
        var arguments = Environment.GetCommandLineArgs();
        Inspect = arguments.Contains("--inspect");
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai");
        int index = Array.IndexOf(arguments, "--data-dir");
        if (index >= 0 && index + 1 < arguments.Length) folder = arguments[index + 1];
        DataFolder = folder; Trace("Starting UI check");
        bar = new DesktopBar(new TodoStore(Path.Combine(folder, "todos.json")), arguments.Contains("--inspect"));
    }
}
