namespace Liubai;

internal sealed partial class StationWindow
{
    internal async Task<string[]> VerifySize()
    {
        var checks = new List<string>(); Open(display, display.Info.Work.Top);
        await Task.Delay(200);
        var card = cards.Children.FirstOrDefault();
        string dataPath = Path.Combine(Path.GetDirectoryName(store.WindowLayoutPath)!, "station.json");
        byte[] originalData = File.ReadAllBytes(dataPath);
        int step = (int)Math.Round(40 * display.Dpi / 96d);
        void Resize(StationEdge edge, int delta) { BeginStationResize(1000, edge); if (!ModalActive) throw new Exception("Resize did not hold panel open"); MoveStationResize(1000 + delta); EndStationResize(false); }
        int right = AppWindow.Position.X + AppWindow.Size.Width;
        Resize(StationEdge.Left, -step);
        if (AppWindow.Position.X + AppWindow.Size.Width != right) throw new Exception("Left drag moved right edge");
        int left = AppWindow.Position.X;
        Resize(StationEdge.Right, -step);
        if (AppWindow.Position.X != left || dockLayout.RightOffset <= 0) throw new Exception("Right drag did not preserve left edge");
        int bottom = AppWindow.Position.Y + AppWindow.Size.Height;
        Resize(StationEdge.Top, step);
        if (Math.Abs(AppWindow.Position.Y + AppWindow.Size.Height - bottom) > 1 || dockLayout.TopOffset <= 0) throw new Exception("Top drag moved lower edge");
        int top = AppWindow.Position.Y; Resize(StationEdge.Bottom, -step);
        if (AppWindow.Position.Y != top) throw new Exception("Bottom drag moved upper edge");
        if (cards.Children.FirstOrDefault() != card || !File.ReadAllBytes(dataPath).SequenceEqual(originalData)) throw new Exception("Resizing rebuilt cards or modified items");
        checks.Add("Four edges resize independently with opposite edge fixed, existing cards and item file untouched");
        var saved = dockLayout;
        foreach (StationEdge edge in Enum.GetValues<StationEdge>())
        {
            var before = new StationPanelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            BeginStationResize(1000, edge); MoveStationResize(1100); EndStationResize(true);
            var after = new StationPanelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            if (dockLayout != saved || after != before || ModalActive) throw new Exception("Canceled edge resize changed geometry");
        }
        if (new StationPanelLayout(store.WindowLayoutPath).Data != saved) throw new Exception("Saved panel geometry missing");
        checks.Add("Each edge cancels cleanly and releases modal lease; persisted dimensions include both offsets");
        Hide(); Open(display, display.Info.Work.Top);
        var docked = new StationPanelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        ToggleDetached();
        await VerifyFloatingCaption(true);
        if (stationResizeHosts.Any(h => h.Visibility != Microsoft.UI.Xaml.Visibility.Collapsed)) throw new Exception("Dock grips remain in detached window");
        AppWindow.MoveAndResize(new(AppWindow.Position.X, AppWindow.Position.Y, Math.Max(400, AppWindow.Size.Width - 40), Math.Max(400, AppWindow.Size.Height - 40)));
        await Task.Delay(500); SaveFloatingLayout();
        var floating = panelLayout.Data.Floating;
        ToggleDetached();
        await VerifyFloatingCaption(false);
        if (docked != new StationPanelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height)) throw new Exception("Docked geometry lost after detachment");
        ToggleDetached();
        if (floating?.Width != AppWindow.Size.Width || floating.Height != AppWindow.Size.Height) throw new Exception("Floating geometry lost");
        ToggleDetached();
        var reload = new StationPanelLayout(store.WindowLayoutPath);
        if (reload.Data.Floating != floating || reload.Data.Width != saved.Width || reload.Data.Height != saved.Height || !File.ReadAllBytes(dataPath).SequenceEqual(originalData)) throw new Exception("Floating resize modified dock or items");
        checks.Add("Reopen, detach and dock retain independent sizes; floating bounds survive configuration reload");
        checks.Add("Native caption hit test supports dragging and excludes header buttons; docked panels clear native caption regions");
        return checks.ToArray();
    }
}

public sealed partial class BarWindow
{
    async Task VerifyStationSize()
    {
        string result = "PASS", error = ""; string[] checks = [];
        StationWindow? panel = null;
        try
        {
            await Task.Delay(400);
            if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before verification");
            await App.Station.Add([StationStore.Text("保留测试文字"), StationStore.Text("https://example.com/", "测试链接")]);
            panel = new StationWindow(App.Station, Display); checks = await panel.VerifySize();
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            panel?.Destroy(); Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "station-size-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks })); Close();
        }
    }
}
