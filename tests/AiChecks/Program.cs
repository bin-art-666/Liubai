using Liubai;

string root = Path.GetFullPath(Path.Combine(".build", "ai-checks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action)
{ bool rejected = false; try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException) { rejected = true; } Check(rejected, "Invalid mutation accepted"); }
try
{
    string path = Path.Combine(root, "ai.json"); var store = new AiOptionsStore(path);
    Check(store.Data.Provider == "deepseek" && !store.Data.Pinned && !File.Exists(path), "Defaults do not write or preload browser");
    Check(store.Data.DockWidth == 430, "Older/missing settings retain the familiar dock width");
    Check(store.Data.DockHeight == 660 && store.Data.DockTopOffset == 0, "Legacy configurations retain default dock height and top anchor");
    var dock = AiPolicy.DockBounds(430, 660, 40, new(0, 24, 1920, 1200), 24, 144);
    Check(dock.Y == 84 && dock.Height == 990 && dock.Width == 645, "Dock height and top offset follow display scaling");
    dock = AiPolicy.DockBounds(430, 1200, 20000, new(0, 24, 700, 500), 24, 96);
    Check(dock.Y >= 24 && dock.Height >= 360 && dock.Y + dock.Height <= 524, "Saved offset and height remain on screen");
    dock = AiPolicy.DockBounds(430, 660, 40, new(-400, 24, 300, 200), 24, 192);
    Check(dock == new AiBounds(-400, 24, 300, 200), "Tiny work area retains an entirely visible panel");
    Check(AiPolicy.DockWidthPixels(530, 2000, 144) == 795, "Preferred width scales in logical pixels");
    Check(AiPolicy.DockWidthPixels(1000, 700, 192) == 700, "Narrow monitor caps actual width without modifying preference");
    Check(AiPolicy.DockWidthPixels(1, 2000, 96) == 360 && AiPolicy.DockWidthPixels(9999, 2000, 96) == 1000, "Dragging clamps to supported width range");
    Check(AiPolicy.Resolve(store.Data).Url == "https://chat.deepseek.com/", "Official DeepSeek URL");
    Check(AiPolicy.Resolve(new(Provider: "doubao")).Url == "https://www.doubao.com/chat/", "Official Doubao URL");
    Check(AiPolicy.Resolve(new(Provider: "xiaoyi")).Url == "https://xiaoyi.huawei.com/", "Official Xiaoyi URL");
    Check(AiPolicy.Resolve(new(Provider: "chatgpt")).Url == "https://chatgpt.com/", "Official ChatGPT URL");
    Check(AiPolicy.BuiltInProviders.Contains("chatgpt") && !AiPolicy.BuiltInProviders.Contains("custom") && AiPolicy.Providers[^1] == "custom", "ChatGPT is built-in while custom remains distinct");
    string chatPath = Path.Combine(root, "chatgpt.json"); var chatStore = new AiOptionsStore(chatPath);
    chatStore.Save(new(Provider: "chatgpt")); Check(new AiOptionsStore(chatPath).Data.Provider == "chatgpt", "ChatGPT selection survives restart");
    var custom = store.Data with { Provider = "custom", CustomName = "我的 AI", CustomUrl = "https://example.com/chat?q=hello%20world", Pinned = true };
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 359d, 1001d }) Reject(() => store.Save(custom with { DockWidth = invalid }));
    foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 359d, 1201d }) Reject(() => store.Save(custom with { DockHeight = invalid }));
    foreach (double invalid in new[] { double.NaN, -1d, 20001d }) Reject(() => store.Save(custom with { DockTopOffset = invalid }));
    store.Save(custom); var reload = new AiOptionsStore(path);
    Check(reload.Data.Provider == "custom" && reload.Data.Pinned && AiPolicy.Resolve(reload.Data).Name == "我的 AI", "Custom provider and pin survive restart");
    Check(AiPolicy.Resolve(custom).Key == AiPolicy.Resolve(custom with { CustomName = "别名" }).Key, "Renaming does not lose website profile or existing window");
    Check(AiPolicy.Resolve(custom).Key != AiPolicy.Resolve(custom with { CustomUrl = "https://example.com/other" }).Key, "Changed custom URL identifies a distinct window");
    foreach (string url in new[] { "http://example.com", "javascript:alert(1)", "file:///C:/test", "https://user:password@example.com/", "https://example.com/\n--flag", "", "--app=https://example.com" })
        Reject(() => store.Save(custom with { CustomUrl = url }));
    Reject(() => store.Save(custom with { Provider = "missing" }));
    Reject(() => store.Save(custom with { CustomName = "" }));
    Check(AiPolicy.AllowedNavigation("https://example.com/auth"), "HTTPS login redirects remain in panel");
    Check(AiPolicy.AllowedNavigation("about:blank"), "Empty popup can initialize before navigation");
    Check(AiPolicy.AllowedNavigation("blob:https://example.com/document"), "Secure in-memory documents supported");
    Check(!AiPolicy.AllowedNavigation("file:///C:/private.txt"), "Website cannot navigate to local files");
    Check(!AiPolicy.AllowedNavigation("microsoft-edge:https://example.com"), "Website cannot spawn external browser through protocol navigation");
    var fitted = AiPolicy.Fit(new(99999, -99999, 2000, 1500), new(-1280, 0, 1280, 720), 96);
    Check(fitted == new AiBounds(-1280, 0, 1280, 720), "Disconnected monitor geometry clamps to available display");
    fitted = AiPolicy.Fit(null, new(0, 24, 1920, 1000), 96);
    Check(fitted.Width == 900 && fitted.Height == 760 && fitted.Y >= 24, "New window centered below appbar work area");
    string key = AiPolicy.Resolve(custom).Key; var bounds = new AiBounds(100, 100, 900, 700);
    store.SaveBounds(key, bounds); store.Save(store.Data with { Provider = "doubao" });
    store.Save(store.Data with { DockWidth = 620 });
    store.Save(store.Data with { DockHeight = 580, DockTopOffset = 30 });
    Check(new AiOptionsStore(path).Data.DockHeight == 580 && new AiOptionsStore(path).Data.DockTopOffset == 30, "Height and upper edge placement survive restart");
    Check(new AiOptionsStore(path).Data.DockWidth == 620 && store.Data.Layouts![key] == bounds, "Dock width persists independently of floating geometry");
    store.SaveBounds(key, bounds with { Width = 940 }); store.Save(store.Data with { Provider = "xiaoyi" });
    Check(store.Data.DockWidth == 620, "Floating resize and service switching retain dock width");
    store.SaveBounds(key, bounds);
    Check(new AiOptionsStore(path).Data.Layouts![key] == bounds, "Service switching preserves window geometry");
    var before = File.ReadAllText(path);
    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => store.Save(store.Data with { Pinned = false }));
    Check(File.ReadAllText(path) == before && store.Data.Pinned && !File.Exists(path + ".tmp"), "Failed save preserves live and disk settings");
    File.WriteAllText(path, "{broken"); var recovered = new AiOptionsStore(path);
    Check(!recovered.ReadOnly && recovered.Warning != null, "Corrupt primary can recover from valid backup");
    string future = Path.Combine(root, "future.json"); File.WriteAllText(future, "{\"Version\":2}");
    var newer = new AiOptionsStore(future); Check(newer.ReadOnly, "Unknown versions remain read-only"); Reject(() => newer.Save(new()));
    Check(File.ReadAllText(future) == "{\"Version\":2}", "Future record preserved");
    Console.WriteLine($"PASS: {checks} AI configuration, navigation and geometry checks");
}
finally { Directory.Delete(root, true); }
