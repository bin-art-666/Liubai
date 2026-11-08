using Microsoft.UI.Windowing;

namespace Liubai;

internal sealed partial class AiWindow
{
    internal async Task<string[]> VerifyPanel()
    {
        if (!verification) throw new InvalidOperationException("Only local fixtures may inspect document state.");
        var checks = new List<string>();
        async Task WaitFor(Func<bool> condition)
        {
            var end = DateTime.UtcNow.AddSeconds(20);
            while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("AI panel fixture timed out: " + Status); await Task.Delay(100); }
        }
        await Open(display, display.Info.Work.Top);
        await WaitFor(() => current?.View.CoreWebView2 != null && !Initializing);
        var original = current!; var core = original.View.CoreWebView2;
        async Task Draft()
        {
            var end = DateTime.UtcNow.AddSeconds(15);
            string actual;
            while (System.Text.Json.JsonSerializer.Deserialize<string>(actual = await core.ExecuteScriptAsync("document.getElementById('draft')?.value ?? ''")) != "保留我的草稿")
            {
                if (DateTime.UtcNow > end) throw new Exception("Local draft missing; value=" + actual + "; source=" + core.Source + "; navigation=" + fixtureNavigation[..Math.Min(512, fixtureNavigation.Length)] + "; status=" + Status + "; body=" + await core.ExecuteScriptAsync("document.body?.innerText ?? ''"));
                await Task.Delay(100);
            }
        }
        await Draft();
        checks.AddRange(await VerifyProviderStrip());
        var savedStyle = PanelTheme.Current;
        await core.ExecuteScriptAsync("document.getElementById('draft').value='风格变化保留对话草稿'");
        try
        {
            foreach (var style in new[] { UiStyleOptions.ForPreset(UiStylePreset.Rounded), UiStyleOptions.ForPreset(UiStylePreset.Compact) })
            {
                PanelTheme.Apply(style); await Task.Delay(80);
                if (!ReferenceEquals(original, current) || !ReferenceEquals(core, current.View.CoreWebView2)
                    || System.Text.Json.JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.getElementById('draft').value")) != "风格变化保留对话草稿")
                    throw new Exception("Live style change recreated browser or reloaded draft");
            }
        }
        finally { PanelTheme.Apply(savedStyle); await core.ExecuteScriptAsync("document.getElementById('draft').value='保留我的草稿'"); }
        checks.Add("Live preset changes retain the same WebView2, page and edited local conversation draft");
        if (Detached || AppWindow.IsShownInSwitchers || !IsOpen) throw new Exception("Panel must initially dock below the bar");
        checks.Add("Real WebView2 local page initializes in docked glass panel without launching Edge windows");
        int originalHeight = AppWindow.Size.Height, originalTop = AppWindow.Position.Y;
        int drag = (int)Math.Round(100 * display.Dpi / 96d);
        BeginDockResize(1000); MoveDockResize(1000 - drag);
        if (!ModalActive || !dockResizing) throw new Exception("Resize must keep panel open");
        EndDockResize(false); await Draft();
        var work = display.Info.Work;
        if (AppWindow.Size.Width != AiPolicy.DockWidthPixels(options.Data.DockWidth, work.Right - work.Left, display.Dpi)
            || AppWindow.Position.X + AppWindow.Size.Width != work.Right || AppWindow.Size.Height != originalHeight || AppWindow.Position.Y != originalTop)
            throw new Exception("Dock resize moved anchor or changed height");
        if (new AiOptionsStore(Path.Combine(directory, "ai-fixture.json")).Data.DockWidth != options.Data.DockWidth) throw new Exception("Dock width persistence failed");
        double savedWidth = options.Data.DockWidth;
        BeginDockResize(1000); MoveDockResize(1000 + drag); EndDockResize(true);
        if (dockWidth != savedWidth || options.Data.DockWidth != savedWidth || ModalActive || !ReferenceEquals(core, current!.View.CoreWebView2)) throw new Exception("Canceled resize changed saved state or browser");
        checks.Add("Dock width drag persists with fixed right edge, stable height and live draft; canceled drag restores width");
        int savedLeft = AppWindow.Position.X, beforeTop = AppWindow.Position.Y;
        BeginDockResize(1000, DockEdge.Bottom); MoveDockResize(1000 - (int)Math.Round(80 * display.Dpi / 96d)); EndDockResize(false); await Draft();
        if (AppWindow.Position.Y != beforeTop || AppWindow.Position.X != savedLeft || options.Data.DockHeight >= AiPolicy.DefaultDockHeight) throw new Exception("Bottom grip did not resize height independently");
        int fixedBottom = AppWindow.Position.Y + AppWindow.Size.Height;
        BeginDockResize(1000, DockEdge.Top); MoveDockResize(1000 + (int)Math.Round(40 * display.Dpi / 96d)); EndDockResize(false); await Draft();
        if (Math.Abs(AppWindow.Position.Y + AppWindow.Size.Height - fixedBottom) > 1 || options.Data.DockTopOffset <= 0) throw new Exception("Top grip did not preserve lower edge");
        double savedHeight = options.Data.DockHeight, savedOffset = options.Data.DockTopOffset;
        BeginDockResize(1000, DockEdge.Top); MoveDockResize(-100000); EndDockResize(true);
        if (dockHeight != savedHeight || dockOffset != savedOffset || ModalActive) throw new Exception("Canceled top drag changed saved dimensions");
        Hide(); await Open(display, display.Info.Work.Top); await Draft();
        var heightReload = new AiOptionsStore(Path.Combine(directory, "ai-fixture.json"));
        if (heightReload.Data.DockHeight != savedHeight || heightReload.Data.DockTopOffset != savedOffset || dockWidth != savedWidth) throw new Exception("Dock dimensions not persisted together");
        checks.Add("Upper and lower grips resize height with fixed opposite edge; canceled drag and reopen preserve dimensions and draft");
        ToggleDetached();
        await VerifyFloatingCaption(true);
        VerifyAiHeader(true);
        if (!Detached || !AppWindow.IsShownInSwitchers || !((OverlappedPresenter)AppWindow.Presenter).IsResizable || DismissOnOutside || dockGrip.Visibility != Microsoft.UI.Xaml.Visibility.Collapsed) throw new Exception("Detach behavior mismatch");
        await Draft(); ToggleDetached(); await Draft();
        await VerifyFloatingCaption(false);
        VerifyAiHeader(false);
        if (Detached || AppWindow.IsShownInSwitchers || !ReferenceEquals(core, current!.View.CoreWebView2) || dockWidth != savedWidth || dockGrip.Visibility != Microsoft.UI.Xaml.Visibility.Visible) throw new Exception("Docking lost browser or dock width");
        checks.Add("Expand and dock preserve the same browser and live draft; switcher and resize behavior follow station");
        Hide(); await Open(display, display.Info.Work.Top); await Draft();
        if (!ReferenceEquals(core, current!.View.CoreWebView2)) throw new Exception("Hiding recreated browser");
        checks.Add("Hide and reopen retain live draft without navigation");
        options.Save(options.Data with { Pinned = true });
        if (DismissOnOutside || !((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop) throw new Exception("Pin behavior mismatch");
        options.Save(options.Data with { Pinned = false }); if (!DismissOnOutside) throw new Exception("Unpin must restore outside dismissal");
        checks.Add("Pin and unpin use shared outside-dismiss behavior");
        SelectProvider("doubao");
        await WaitFor(() => current != original && current?.View.CoreWebView2 != null && !Initializing);
        SelectProvider("deepseek");
        // Returning after a focus dismissal must preserve the same cached conversation too.
        await Open(display, display.Info.Work.Top); await Draft();
        if (!ReferenceEquals(current, original)) throw new Exception("Service switching discarded original page");
        checks.Add("Service switch retains previous provider's browser and draft");
        _ = await core.ExecuteScriptAsync("window.open('about:blank','fixturepopup')");
        await WaitFor(() => popup?.View.CoreWebView2 != null);
        if (back.Visibility != Microsoft.UI.Xaml.Visibility.Visible || providerMore.IsEnabled) throw new Exception("Popup missing back control or service switch was left enabled");
        ClosePopup(); await Draft();
        checks.Add("Website popup stays inside panel; returning preserves main conversation");
        ToggleDetached();
        AppWindow.MoveAndResize(new(AppWindow.Position.X, AppWindow.Position.Y, Math.Max(400, AppWindow.Size.Width - 40), Math.Max(400, AppWindow.Size.Height - 40)));
        await Task.Delay(500); SaveFloating(); var expected = floating;
        ToggleDetached(); ToggleDetached(); await Draft();
        if (AppWindow.Size.Width != expected!.Width || AppWindow.Size.Height != expected.Height) throw new Exception("Floating geometry lost");
        ToggleDetached(); checks.Add("Floating size persists independently of docked dimensions");
        if (Environment.GetCommandLineArgs().Contains("--preview-ai-panel")) { options.Save(options.Data with{Pinned=true});ShowPanel();await Task.Delay(20000); }
        return checks.ToArray();
    }
    void VerifyAiHeader(bool detached)
    {
        if(!ReferenceEquals(aiNavigation.Parent,detached?aiHeader:root)||root.RowDefinitions.Count!=(detached?3:4)||aiHeaderDivider.Visibility!=(detached?Microsoft.UI.Xaml.Visibility.Visible:Microsoft.UI.Xaml.Visibility.Collapsed))throw new Exception("AI header layout did not follow detached state");
        foreach(var button in new Microsoft.UI.Xaml.Controls.Primitives.ButtonBase[]{expand,pin})
            if(button.Width!=PanelTheme.Value<double>("ActionSize")||button.Height!=button.Width||button.BorderThickness.Left!=1||button.Content is not Microsoft.UI.Xaml.Controls.Viewbox)throw new Exception("AI window buttons differ from station framing/vector sizes");
        if(providerHost.Parent!=aiNavigation)throw new Exception("Moving header replaced the original provider strip");
    }
}

public sealed partial class BarWindow
{
    async Task VerifyAiPanel()
    {
        string result = "PASS", error = ""; string[] checks = [];
        AiWindow? panel = null;
        try
        {
            await Task.Delay(500);
            if (SessionState.Locked == true) throw new IOException("LOCKED: unlock before native verification");
            var store = new AiOptionsStore(Path.Combine(App.DataDirectory, "ai-fixture.json"));
            // A scheduled fixture has no user foreground grant while WebView2 starts.
            // Pin only its initial load; VerifyPanel still explicitly tests unpin/outside behavior.
            store.Save(store.Data with { Pinned = true });
            panel = new AiWindow(store, App.DataDirectory, Display, () => { }, verification: true);
            store.Changed += panel.ApplyOptions;
            checks = await panel.VerifyPanel();
            store.Changed -= panel.ApplyOptions;
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            panel?.Destroy(); Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "ai-panel-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
