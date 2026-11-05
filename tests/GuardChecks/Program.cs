using Liubai;

string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".build", "guard-checks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Reject(Action action) { bool failed = false; try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { failed = true; } Check(failed, "Invalid mutation accepted"); }
try
{
    string path = Path.Combine(root, "guard.json"); var store = new GuardOptionsStore(path);
    Check(store.Data.Mode == GuardAlertMode.Prompt && store.Data.HoldMilliseconds == 1500 && !File.Exists(path), "Defaults/opening should not write");
    Check(!typeof(GuardOptions).GetProperties().Any(p => p.Name == "Enabled"), "Camera activation must never persist across launches");
    var settings = store.Data with { CameraId = "fixture-camera", Message = "自定义提醒\n请按 Enter", Mode = GuardAlertMode.Blackout };
    store.Save(settings); Check(new GuardOptionsStore(path).Data == settings, "Options restart");
    foreach (var invalid in new[] { settings with { Mode = (GuardAlertMode)99 }, settings with { Message = " " }, settings with { Message = new string('x', 201) }, settings with { HoldMilliseconds = 0 }, settings with { HoldMilliseconds = 5001 }, settings with { CameraId = "bad\n" }, settings with { Message = "bad\0" } }) Reject(() => store.Save(invalid));
    Check(store.Data == settings, "Invalid settings changed live state");
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => store.Save(settings with { Message = "must fail" }));
    Check(store.Data == settings && !File.Exists(path + ".tmp"), "Failed persistence changed live state or leaked temporary file");
    store.Save(settings with { Message = "second" });
    byte[] backup = File.ReadAllBytes(path + ".bak"); File.WriteAllText(path, "{broken");
    var recovered = new GuardOptionsStore(path); Check(recovered.Data == settings && !recovered.ReadOnly, "Backup recovery");
    recovered.Save(settings with { Message = "repaired" }); Check(File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "Recovery overwrote good backup");
    File.WriteAllText(path, "{broken"); File.WriteAllText(path + ".bak", "{broken");
    var broken = new GuardOptionsStore(path); Check(broken.ReadOnly, "Both corrupt must be read-only"); Reject(() => broken.Save(settings));
    File.WriteAllText(path, "{\"Version\":99}"); var future = new GuardOptionsStore(path); Check(future.ReadOnly, "Future format accepted"); Reject(() => future.Save(settings));
    var trigger = new GuardTrigger();
    foreach (long now in new long[] { 0, 500, 1000, 1500 }) Check(!trigger.Observe(1, now, 1500), "Single face triggered");
    Check(!trigger.Observe(2, 2000, 1500) && !trigger.Observe(2, 2500, 1500), "Brief visitor triggered");
    trigger.Observe(1, 3000, 1500);
    Check(!trigger.Observe(2, 3500, 1500) && !trigger.Observe(2, 4000, 1500) && !trigger.Observe(2, 4500, 1500) && trigger.Observe(2, 5000, 1500), "Continuous dwell incorrect");
    Check(trigger.Active && !trigger.Observe(2, 5500, 1500), "Alert repeated while active");
    trigger.Dismiss(6000);
    Check(!trigger.Active && trigger.Cooling(35999) && !trigger.Cooling(36000), "Cooldown boundary");
    Check(!trigger.Observe(2, 35999, 1500) && !trigger.Observe(2, 36000, 1500), "Cooldown retained stale candidate");
    trigger.Observe(2, 36500, 1500); trigger.Observe(2, 37000, 1500);
    Check(trigger.Observe(2, 37500, 1500), "Did not rearm after cooldown");
    trigger.Reset(); trigger.Observe(2, 0, 1500); trigger.Observe(2, 500, 1500);
    Check(!trigger.Observe(2, 3000, 1500), "Camera gap falsely completed dwell");
    Check(!trigger.Observe(2, 1000, 1500), "Clock regression retained candidate");
    trigger.Reset(); Check(!trigger.Active && !trigger.Cooling(0), "Reset must clear alert/cooldown");
    trigger.Observe(2, 0, 1500); trigger.Observe(2, 500, 1500); trigger.RestartCandidate();
    Check(!trigger.Observe(2, 1000, 500) && trigger.Observe(2, 1500, 500), "New wait time reused old candidate");
    trigger.Dismiss(2000); trigger.RestartCandidate();
    Check(trigger.Cooling(2500) && !trigger.Observe(2, 3000, 500), "Changing wait time cleared cooldown");
    var pause = new GuardPause(); Check(!pause.Pending && !pause.Active(0), "New pause should be inactive");
    pause.Begin(1000, 600000);
    Check(pause.Pending && pause.Active(600999) && !pause.Active(601000), "Ten-minute pause boundary");
    pause.Begin(2000, 600000); Check(pause.Active(601000) && !pause.Active(602000), "Extending pause reused old expiry");
    pause.Clear(); Check(!pause.Pending && !pause.Active(5000), "Manual resume/off did not clear pause");
    foreach (long invalid in new long[] { 0, -1, 86400001 })
    { bool rejected = false; try { pause.Begin(0, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected && !pause.Pending, "Invalid pause changed deadline"); }
    var customPath = Path.Combine(root, "custom-wait.json"); var customStore = new GuardOptionsStore(customPath);
    customStore.Save(customStore.Data with { HoldMilliseconds = 2500 });
    Check(new GuardOptionsStore(customPath).Data.HoldMilliseconds == 2500, "Custom wait did not persist");
    Console.WriteLine($"PASS: {checks} guard settings, startup privacy, recovery, dwell, stale-frame and cooldown checks");
}
finally
{
    string expected = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".build")) + Path.DirectorySeparatorChar;
    if (root.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) Directory.Delete(root, true);
}
