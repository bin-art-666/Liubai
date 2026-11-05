using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace Liubai;

public sealed partial class BarWindow
{
    static IEnumerable<T> StationControls<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in StationControls<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    async Task VerifyStation()
    {
        string result = "PASS", error = ""; var checks = new List<string>(); Window? fixture = null;
        try
        {
            Directory.CreateDirectory(App.DataDirectory);
            string file = Path.Combine(App.DataDirectory, "station-source.txt"), folder = Path.Combine(App.DataDirectory, "station-folder");
            File.WriteAllText(file, "Liubai generated station fixture"); Directory.CreateDirectory(folder);
            await Task.Delay(400);
            if (!OpenStation()) throw new Exception("Station toolbar entry did not open");
            var text = new DataPackage(); text.SetText("中转站测试 · text fixture");
            if (!await stationWindow!.Receive(text.GetView())) throw new Exception("Text receive failed");
            var link = new DataPackage(); link.SetWebLink(new Uri("https://example.com/")); link.SetText("https://example.com/");
            link.Properties.Title = "Example";
            if (!await stationWindow.Receive(link.GetView())) throw new Exception("Web link receive failed");
            var files = new DataPackage();
            files.SetStorageItems(new IStorageItem[] { await StorageFile.GetFileFromPathAsync(file), await StorageFolder.GetFolderFromPathAsync(folder) }, true);
            if (!await stationWindow.Receive(files.GetView()) || App.Station.Items.Count != 4) throw new Exception("File/folder receive failed");
            checks.Add("Real DataPackage input: text, URL, file, folder; no clipboard access");
            var copyButtons = StationControls<Button>((DependencyObject)stationWindow.Content).Where(b => AutomationProperties.GetName(b) == "复制内容").ToArray();
            if (copyButtons.Length != 4 || copyButtons.Any(b => !b.IsEnabled || b.Content is string)) throw new Exception("Copy icon controls missing or disabled");
            checks.Add("Copy actions available after asynchronous import");
            foreach (var item in App.Station.Items)
            {
                var package = new DataPackage(); await StationTransfer.Fill(package, item);
                if (package.RequestedOperation != DataPackageOperation.Copy) throw new Exception("Outgoing operation is not copy");
                var view = package.GetView();
                if (item.Kind is StationKind.File or StationKind.Folder)
                { if ((await view.GetStorageItemsAsync()).Single().Path != item.Value) throw new Exception("Outgoing storage reference mismatch"); }
                else if (await view.GetTextAsync() != item.Value) throw new Exception("Outgoing text mismatch");
            }
            checks.Add("Outgoing native data packages preserve payload and only request Copy");
            if (new StationStore(Path.Combine(App.DataDirectory, "station.json")).Items.Count != 4) throw new Exception("Restart data missing");
            checks.Add("Saved items recovered after reopening store");
            var browser = new DataPackage(); browser.SetWebLink(new Uri("https://example.com/browser"));
            browser.SetDataProvider(StandardDataFormats.StorageItems, _ => throw new InvalidOperationException("Virtual URL has no path"));
            if (!await stationWindow.Receive(browser.GetView()) || App.Station.Items.Last().Kind != StationKind.Link) throw new Exception("Browser URL did not take precedence over virtual storage");
            var textUrl = new DataPackage(); textUrl.SetText("https://example.com/address");
            textUrl.SetDataProvider(StandardDataFormats.StorageItems, _ => throw new InvalidOperationException("Virtual URL has no path"));
            if (!await stationWindow.Receive(textUrl.GetView()) || App.Station.Items.Last().Kind != StationKind.Link) throw new Exception("Text URL fallback did not take precedence");
            checks.Add("Browser URL and text URL take precedence over pathless virtual storage");
            var virtualFile = await StorageFile.CreateStreamedFileAsync("streamed.txt", async stream =>
            {
                try { using var writer = new Windows.Storage.Streams.DataWriter(stream); writer.WriteString("Liubai streamed fixture"); await writer.StoreAsync(); }
                finally { stream.Dispose(); }
            }, null);
            if (virtualFile.Path.Length != 0) throw new Exception("Virtual file fixture unexpectedly has a path");
            var virtualPackage = new DataPackage(); virtualPackage.SetStorageItems(new[] { virtualFile });
            if (!await stationWindow.Receive(virtualPackage.GetView())) throw new Exception("Pathless virtual file receive failed");
            string local = App.Station.Items.Last().Value;
            if (!local.StartsWith(App.Station.AttachmentDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(local) != "Liubai streamed fixture") throw new Exception("Virtual file not saved locally");
            string rollback;
            using (var import = await StationTransfer.Read(virtualPackage.GetView(), App.Station.AttachmentDirectory))
            { rollback = import.Inputs.Single().Value; if (!File.Exists(rollback)) throw new Exception("Rollback fixture missing"); }
            if (File.Exists(rollback) || Directory.Exists(Path.GetDirectoryName(rollback))) throw new Exception("Uncommitted virtual file leaked");
            checks.Add("Pathless streamed file saved locally; uncommitted copy rolled back");
            stationWindow.ConfirmDrop(); await Task.Delay(500);
            if (!stationWindow.IsOpen) throw new Exception("Successful incoming drop did not remain open");
            checks.Add("Successful incoming drop stays open after source-activation grace period; foreground activation also requested");
            await VerifyStationAppearance(checks);
            await VerifyStationOptions(checks);
            if (Environment.GetCommandLineArgs().Contains("--preview-station"))
            {
                // Allow a human/UI driver to give this background-launched verification
                // app foreground input before exercising actual focus transitions.
                await Task.Delay(TimeSpan.FromSeconds(60));
                if (stationWindow.Detached) stationWindow.ToggleDetached();
                stationWindow.Open(Display, BarRectangle.Y + BarRectangle.Height);
            }
            var pin = StationControls<ToggleButton>((DependencyObject)stationWindow.Content).Single(b => AutomationProperties.GetName(b) == "固定中转站");
            pin.IsChecked = true; Activate(); await Task.Delay(350);
            if (!stationWindow.IsOpen) throw new Exception("Pinned station dismissed outside");
            pin.IsChecked = false; stationWindow.Open(Display, BarRectangle.Y + BarRectangle.Height); Activate(); await Task.Delay(350);
            if (stationWindow.IsOpen) throw new Exception("Unpinned station did not dismiss outside");
            checks.Add("Pin and ordinary outside-focus dismissal");
            OpenStation(); stationWindow.ToggleDetached();
            var presenter = (OverlappedPresenter)stationWindow.AppWindow.Presenter;
            if (!stationWindow.Detached || !presenter.IsResizable || !stationWindow.AppWindow.IsShownInSwitchers || presenter.IsAlwaysOnTop) throw new Exception("Detached window mode invalid");
            pin.IsChecked = true;
            if (!presenter.IsAlwaysOnTop) throw new Exception("Detached pin did not enable topmost");
            pin.IsChecked = false; OpenSettings(); await Task.Delay(250);
            if (!stationWindow.IsOpen) throw new Exception("Detached window dismissed when settings opened");
            settingsWindow!.Hide(); stationWindow.ToggleDetached();
            if (stationWindow.Detached || presenter.IsResizable || stationWindow.AppWindow.IsShownInSwitchers || !presenter.IsAlwaysOnTop) throw new Exception("Docked mode not restored");
            Activate(); await Task.Delay(350);
            if (stationWindow.IsOpen) throw new Exception("Re-docked window did not regain outside dismissal");
            checks.Add("Detached window resizes and survives other panels; pin controls topmost; re-docking restores transient behavior");
            OpenStation(true); stationWindow.ScheduleDragDismiss(); await Task.Delay(750);
            if (stationWindow.IsOpen) throw new Exception("Abandoned hover panel remained open");
            checks.Add("Abandoned incoming drag auto-dismisses");
            OpenStation(); OpenSettings(); await Task.Delay(250);
            if (stationWindow.IsOpen || settingsWindow?.IsOpen != true) throw new Exception("Settings/station overlap");
            settingsWindow.Hide(); checks.Add("Settings and station panels remain mutually exclusive");
            if (!File.Exists(file) || File.ReadAllText(file) != "Liubai generated station fixture") throw new Exception("Source file modified");
            checks.Add("Source file intact");
            if (Environment.GetCommandLineArgs().Contains("--interactive-station"))
            {
                // Keep the OS drag gesture in one native window for bounded UI
                // automation; the station's production drop/drag handlers are unchanged.
                OpenStation();
                pin.IsChecked = true;
                var layout = new StackPanel { Padding = new Thickness(30), Spacing = 22, Background = new SolidColorBrush(Colors.White), AllowDrop = true };
                layout.Children.Add(new TextBlock { Text = "把下面的测试内容拖到收纳盒", FontSize = 20, Foreground = new SolidColorBrush(Colors.Black) });
                var source = new Border { CanDrag = true, Padding = new Thickness(20), Background = new SolidColorBrush(Colors.LightGray),
                    Child = new TextBlock { Text = "拖动我：测试文字", Foreground = new SolidColorBrush(Colors.Black), FontSize = 18 } };
                source.DragStarting += (_, e) => { e.AllowedOperations = DataPackageOperation.Copy; e.Data.SetText("实际拖入测试 · mouse drag fixture"); e.Data.RequestedOperation = DataPackageOperation.Copy; };
                layout.Children.Add(source);
                var target = new Border { Padding = new Thickness(24), Background = new SolidColorBrush(Colors.LightCyan), AllowDrop = true,
                    Child = new TextBlock { Text = "从中转站拖到这里，检查原生拖出", Foreground = new SolidColorBrush(Colors.Black), FontSize = 18, TextWrapping = TextWrapping.Wrap } };
                var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                target.DragOver += (_, e) => { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; };
                target.Drop += async (_, e) =>
                {
                    var deferral = e.GetDeferral();
                    try { e.AcceptedOperation = DataPackageOperation.Copy; received.TrySetResult(await e.DataView.GetTextAsync()); }
                    catch (Exception ex) { received.TrySetException(ex); }
                    finally { e.Handled = true; deferral.Complete(); }
                };
                layout.Children.Add(target);
                var stationRoot = (FrameworkElement)stationWindow.Content;
                stationWindow.Content = null;
                var host = new Grid();
                host.ColumnDefinitions.Add(new() { Width = new GridLength(430) });
                host.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                host.Children.Add(layout); Grid.SetColumn(stationRoot, 1); host.Children.Add(stationRoot);
                stationWindow.Content = host;
                var work = Display.Info.Work; double scale = Display.Dpi / 96d;
                stationWindow.AppWindow.MoveAndResize(new RectInt32(work.Left + 40, work.Top + 40,
                    Math.Min((int)(900 * scale), work.Right - work.Left - 80), Math.Min((int)(730 * scale), work.Bottom - work.Top - 80)));
                stationWindow.Activate();
                string actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(180));
                if (actual != "实际拖入测试 · mouse drag fixture" || !App.Station.Items.Any(i => i.Value == actual)) throw new Exception("Real mouse drag payload or retention failed");
                checks.Add("Real OS mouse drag in and out through production handlers (same-window fixture); item retained");
                await Task.Delay(1500);
            }
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            fixture?.Close(); stationWindow?.Hide(); settingsWindow?.Hide();
            File.WriteAllText(Path.Combine(App.DataDirectory, "station-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
