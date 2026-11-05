using Windows.Foundation;

namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyToolbar()
    {
        string result = "PASS", error = "";
        var checks = new List<string>();
        try
        {
            await Task.Delay(800);
            actions.UpdateLayout();
            var original = CurrentToolbarOrder();
            BeginToolbarDrag(settings, new Point(actions.ActualWidth - 10, 1));
            if (!ToolbarClickBlocked) throw new Exception("Drag did not suppress actions");
            MoveToolbarDrag(new Point(0, 1), true);
            if (CurrentToolbarOrder()[0] != "settings" || toolbarMarker?.Visibility != Microsoft.UI.Xaml.Visibility.Visible) throw new Exception("Preview missing");
            FinishToolbarDrag(true);
            await Task.Delay(100);
            if (new AppearanceStore(Path.Combine(App.DataDirectory, "appearance.json")).ToolbarItems[0] != "settings") throw new Exception("Persistence failed");
            checks.Add("Native layout preview, marker, action suppression and persistence");
            var saved = CurrentToolbarOrder();
            BeginToolbarDrag(settings, new Point(0, 1));
            MoveToolbarDrag(new Point(actions.ActualWidth, 1), true);
            MoveToolbarDrag(new Point(actions.ActualWidth, 1), false);
            if (!CurrentToolbarOrder().SequenceEqual(saved)) throw new Exception("Ctrl release cancellation failed");
            BeginToolbarDrag(settings, new Point(0, 1));
            MoveToolbarDrag(new Point(actions.ActualWidth, 1), true);
            FinishToolbarDrag(false);
            if (!CurrentToolbarOrder().SequenceEqual(saved)) throw new Exception("Canceled drop changed order");
            checks.Add("Ctrl release and canceled drop restore order");
            clockButton.Width = 140; actions.UpdateLayout();
            BeginToolbarDrag(clockButton, new Point(actions.ActualWidth / 2, 1));
            MoveToolbarDrag(new Point(actions.ActualWidth + 10, 1), true);
            if (CurrentToolbarOrder()[^1] != "clock") throw new Exception("Variable-width clock failed");
            FinishToolbarDrag(true);
            await Task.Delay(100);
            preferences.SaveToolbarOrder(original);
            await Task.Delay(350);
            if (!CurrentToolbarOrder().SequenceEqual(original) || ToolbarClickBlocked) throw new Exception("Event synchronization or click recovery failed");
            checks.Add("Variable-width clock, order event synchronization and click recovery");
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            FinishToolbarDrag(false);
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "toolbar-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
