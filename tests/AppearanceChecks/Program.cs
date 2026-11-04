using Liubai;

string directory = Path.Combine(Path.GetTempPath(), "Liubai-AppearanceChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "appearance.json");
int count = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    count++;
}

try
{
    File.WriteAllText(path, "{\"Style\":0}");
    var store = new AppearanceStore(path);
    Check(store.Style == GlassStyle.Light && store.Material.Thin && store.Material.TintOpacity == .08f,
        "Legacy preset must retain its original appearance.");
    var custom = new MaterialOptions(true, .37f, .63f, false, "#7CAEDD", false);
    store.Preview(custom);
    Check(File.ReadAllText(path) == "{\"Style\":0}", "Preview must not write on every slider tick.");
    store.SaveCurrent();
    var restored = new AppearanceStore(path);
    Check(restored.Style == GlassStyle.Custom && restored.Material == custom, "Custom options must survive restart.");
    restored.Save(GlassStyle.Acrylic);
    restored = new AppearanceStore(path);
    Check(restored.Material.TintOpacity == .45f && restored.Custom == custom, "Presets must preserve custom settings.");
    restored.Save(GlassStyle.Custom);
    Check(new AppearanceStore(path).Material == custom, "Returning to custom must restore all settings.");
    foreach (var invalid in new[] { custom with { TintOpacity = float.NaN }, custom with { LuminosityOpacity = 1.01f }, custom with { TintHex = "#INVALID" } })
    {
        bool rejected = false;
        try { restored.Preview(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && restored.Material == custom, "Invalid input must be rejected without changing the preview.");
    }
    File.WriteAllText(path, "{\"Style\":3,\"Custom\":{\"TintOpacity\":9}}");
    Check(new AppearanceStore(path).Custom == custom, "Invalid saved values must recover from the last valid backup.");
    var extremes = custom with { TintOpacity = 0, LuminosityOpacity = 1 };
    restored.Preview(extremes); restored.SaveCurrent();
    Check(new AppearanceStore(path).Material == extremes, "Slider endpoints must round-trip exactly.");
    Check(store.BarHeight == 36, "Legacy appearance must use the existing 36px height.");
    var beforeMaterial = restored.Material;
    restored.SetHeight(64);
    restored.SaveCurrent();
    Check(new AppearanceStore(path).BarHeight == 64 && restored.Material == beforeMaterial,
        "Height must persist independently of material options.");
    restored.Save(GlassStyle.Light);
    Check(new AppearanceStore(path).BarHeight == 64, "Changing presets must preserve height.");
    foreach (int height in new[] { AppearanceStore.MinimumHeight, AppearanceStore.MaximumHeight })
    {
        restored.SetHeight(height); restored.SaveCurrent();
        Check(new AppearanceStore(path).BarHeight == height, "Height endpoints must round-trip.");
    }
    foreach (int height in new[] { AppearanceStore.MinimumHeight - 1, AppearanceStore.MaximumHeight + 1 })
    {
        bool rejected = false;
        try { restored.SetHeight(height); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && restored.BarHeight == AppearanceStore.MaximumHeight, "Out-of-range heights must not change settings.");
    }
    File.WriteAllText(path, "{\"Style\":0,\"BarHeight\":999}");
    Check(new AppearanceStore(path).BarHeight == AppearanceStore.MinimumHeight, "Invalid saved height must fall back to the valid backup.");
    Check(store.Expanded.Enabled && store.Expanded.Mode == ExpandedMode.Automatic, "Legacy settings must enable the default automatic expanded appearance.");
    var expanded = new ExpandedAppearanceOptions(true, ExpandedMode.Custom, "#AADDCC", AppearanceSpeed.Responsive, AppearanceAnimation.Soft);
    restored.SetExpandedAppearance(expanded); restored.SaveCurrent();
    Check(new AppearanceStore(path).Expanded == expanded, "Expanded appearance must survive restart.");
    restored.Save(GlassStyle.Frosted);
    Check(new AppearanceStore(path).Expanded == expanded, "Daily presets must preserve expanded appearance.");
    foreach (var invalid in new[] { expanded with { Mode = (ExpandedMode)99 }, expanded with { ColorHex = "#WRONG!" },
        expanded with { Speed = (AppearanceSpeed)99 }, expanded with { Animation = (AppearanceAnimation)99 } })
    {
        bool rejected = false;
        try { restored.SetExpandedAppearance(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && restored.Expanded == expanded, "Invalid expanded appearance must be rejected.");
    }
    Check(WindowColorAnalysis.WhiteForeground(0) && !WindowColorAnalysis.WhiteForeground(0xFFFFFF), "Black/white surfaces must get opposite icon colors.");
    Check(!WindowColorAnalysis.WhiteForeground(0x9AE5C7), "Light custom color must use dark icons.");
    Check(WindowColorAnalysis.Representative(Enumerable.Repeat(0x223344u, 60).Concat(Enumerable.Repeat(0xFFFFFFu, 20))) == 0x223344,
        "Sparse text must not override the dominant background.");
    Check(WindowColorAnalysis.Representative(new uint[] { 0, 0xFFFFFF }) == null, "Insufficient samples must retain the last stable color.");
    var tracker = new ExpandedAppearanceTracker();
    var darkWindow = new WindowObservation(true, 10, 0x202020);
    Check(tracker.Observe(darkWindow, 0) == null && tracker.Observe(darkWindow, 100) == null, "Window transitions must wait for stability.");
    Check(tracker.Observe(darkWindow, 300) is { IsExpanded: true, IsDark: true }, "Stable dark window must commit.");
    Check(WindowColorAnalysis.Background(new(), tracker.Current) == 0, "Automatic dark mode must be pure black.");
    tracker.Observe(new(true, 10, 0x808080), 400); tracker.Observe(new(true, 10, 0x808080), 700);
    Check(tracker.Current.IsDark, "Midrange brightness must preserve the current dark classification.");
    var lightWindow = new WindowObservation(true, 10, 0xFAFAFA);
    tracker.Observe(lightWindow, 800); tracker.Observe(lightWindow, 1100);
    Check(!tracker.Current.IsDark && WindowColorAnalysis.Background(new(), tracker.Current) == 0xFFFFFF, "Light window must commit to pure white.");
    var kept = tracker.Current;
    Check(tracker.Observe(new(false, Hold: true), 2000) == null && tracker.Current == kept, "Settings and temporary popups must preserve the stable appearance.");
    tracker.Observe(new(true, 11, null), 2100); tracker.Observe(new(true, 11, null), 2400);
    Check(tracker.Current.Color == kept.Color, "Unavailable capture must retain the last stable color.");
    tracker.Observe(new(false), 2500); tracker.Observe(new(false), 2800);
    Check(!tracker.Current.IsExpanded, "Restoring the window must leave the expanded scene.");
    Check(WindowColorAnalysis.Background(expanded, kept) == 0xAADDCC, "Custom color must ignore window brightness.");
    Check(WindowColorAnalysis.Background(new(true, ExpandedMode.FollowWindow), kept) == kept.Color, "Follow-window mode must use the sampled RGB color.");
    Check(store.Expanded.Speed == AppearanceSpeed.Balanced && store.Expanded.Animation == AppearanceAnimation.Standard,
        "Legacy settings must use balanced switching and standard animation.");
    string legacyPath = Path.Combine(directory, "legacy-expanded.json");
    File.WriteAllText(legacyPath, "{\"Style\":0,\"Expanded\":{\"Enabled\":true,\"Mode\":2,\"ColorHex\":\"#121212\"}}");
    var legacyExpanded = new AppearanceStore(legacyPath).Expanded;
    Check(legacyExpanded.Mode == ExpandedMode.FollowWindow && legacyExpanded.Speed == AppearanceSpeed.Balanced
        && legacyExpanded.Animation == AppearanceAnimation.Standard, "Existing expanded-mode JSON must migrate without changing its mode.");
    foreach (var speed in Enum.GetValues<AppearanceSpeed>())
    {
        var timing = AppearanceTiming.For(speed);
        var fast = new ExpandedAppearanceTracker { Speed = speed };
        fast.Observe(darkWindow, 0);
        Check(fast.Observe(darkWindow, timing.Scene - 1) == null, "Scene changes must wait for the configured stability period.");
        Check(fast.Observe(darkWindow, timing.Scene) != null, "Scene gate and color confirmation must not add two waits.");
        var other = new WindowObservation(true, 20, 0xFAFAFA);
        fast.Observe(other, 1000);
        Check(fast.Observe(other, 1000 + timing.Source) is { Window: 20 }, "Foreground changes must use the faster source confirmation.");
        var content = other with { Color = 0x101010 };
        fast.Observe(content, 2000);
        Check(fast.Observe(content, 2000 + timing.Content - 1) == null, "In-window content must not flicker on a single capture.");
        Check(fast.Observe(content, 2000 + timing.Content) is { IsDark: true }, "Stable in-window color must eventually update.");
        fast.Observe(new(false), 3000);
        fast.Observe(new(false, Frame: new(0, 1, 800, 600)), 3000 + timing.Scene - 10);
        Check(fast.Observe(new(false, Frame: new(0, 1, 800, 600)), 3000 + timing.Scene) == null,
            "Restore geometry must settle before committing.");
        Check(fast.Observe(new(false, Frame: new(0, 1, 800, 600)), 3000 + timing.Scene + timing.Quiet) is { IsExpanded: false },
            "Settled geometry must commit without restarting the scene timer.");
        fast.Observe(darkWindow, 4000); fast.Invalidate();
        Check(fast.Observe(darkWindow, 5000) == null, "A newer native event must invalidate a stale candidate.");
        Check(fast.Observe(new(false), 5010) == null && !fast.Current.IsExpanded,
            "Rapid maximize/restore must discard the superseded scene.");
    }
    Check(store.AllDisplays && !store.FollowPaused && store.Presets.Count == 0, "Legacy configuration must migrate to all displays and active following.");
    restored.Preview(custom); restored.SetHeight(64); restored.SetExpandedAppearance(expanded);
    restored.SetAllDisplays(false); restored.SetFollowPaused(true); restored.SaveCurrent();
    Check(!new AppearanceStore(path).AllDisplays && new AppearanceStore(path).FollowPaused, "Display scope and pause must persist.");
    restored.SavePreset("  办公  ");
    var savedPreset = restored.Presets.Single();
    Check(savedPreset.Name == "办公" && savedPreset.Custom == custom && savedPreset.Expanded == expanded && savedPreset.BarHeight == 64,
        "Personal presets must include the complete appearance and trim their name.");
    restored.SetHeight(48); restored.SavePreset("办公");
    Check(restored.Presets.Count == 1 && restored.Presets[0].Id == savedPreset.Id && restored.Presets[0].BarHeight == 48,
        "Saving the same name must replace its preset without duplicating it.");
    restored.Save(GlassStyle.Light); restored.SetHeight(20); restored.ApplyPreset(savedPreset.Id);
    Check(restored.Style == GlassStyle.Custom && restored.Custom == custom && restored.BarHeight == 48
        && !restored.AllDisplays && restored.FollowPaused, "Applying appearance must preserve the display scope and pause setting.");
    string exported = Path.Combine(directory, "export.json");
    File.WriteAllText(exported, "old export"); restored.Export(exported);
    Check(!File.Exists(exported + ".bak") && !File.ReadAllText(exported).Contains("Startup"), "Portable export must omit machine startup settings and extra backup files.");
    var imported = new AppearanceStore(Path.Combine(directory, "imported.json")); imported.Import(exported);
    Check(imported.Style == restored.Style && imported.Custom == custom && imported.BarHeight == 48
        && imported.Presets.Single() == restored.Presets.Single() && !imported.AllDisplays && imported.FollowPaused,
        "Configuration export/import must round-trip appearance, general options and personal presets.");
    var validConfig = System.Text.Json.JsonSerializer.Deserialize<AppearanceConfiguration>(File.ReadAllText(exported))!;
    foreach (var bad in new[] { validConfig with { Version = 999 }, validConfig with { BarHeight = 0 },
        validConfig with { Presets = [savedPreset, savedPreset] }, validConfig with { Presets = [savedPreset with { Name = "" }] },
        validConfig with { Expanded = expanded with { Animation = (AppearanceAnimation)999 } } })
    {
        string incoming = Path.Combine(directory, "invalid-import.json");
        File.WriteAllText(incoming, System.Text.Json.JsonSerializer.Serialize(bad));
        string before = File.ReadAllText(path);
        bool rejected = false; try { restored.Import(incoming); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == before && restored.BarHeight == 48 && restored.Custom == custom,
            "Invalid imports must leave both disk and live settings unchanged.");
    }
    string tooLarge = Path.Combine(directory, "too-large.json"); File.WriteAllText(tooLarge, new string(' ', 1024 * 1024 + 1));
    bool oversizedRejected = false; try { restored.Import(tooLarge); } catch (InvalidDataException) { oversizedRejected = true; }
    Check(oversizedRejected, "Oversized imports must be rejected before parsing.");
    foreach (string name in new[] { "", new string('x', 41) })
    {
        bool rejected = false; try { restored.SavePreset(name); } catch (ArgumentException) { rejected = true; }
        Check(rejected && restored.Presets.Count == 1, "Invalid preset names must not alter saved presets.");
    }
    restored.ResetDefaults();
    Check(restored.Style == GlassStyle.Frosted && restored.BarHeight == 36 && restored.AllDisplays && !restored.FollowPaused
        && restored.Presets.Count == 1, "Reset defaults must preserve personal presets and restore all application defaults.");
    restored.DeletePreset(savedPreset.Id);
    Check(restored.Presets.Count == 0 && new AppearanceStore(path).Presets.Count == 0, "Deleting a selected preset must persist.");
    string blockedPath = Path.Combine(directory, "directory-instead-of-file"); Directory.CreateDirectory(blockedPath);
    var blocked = new AppearanceStore(blockedPath);
    bool failedWrite = false; try { blocked.Import(exported); } catch (IOException) { failedWrite = true; }
    Check(failedWrite && blocked.Style == GlassStyle.Frosted && blocked.Presets.Count == 0,
        "A failed disk replacement must not partially apply an imported configuration.");
    var screen = new WindowGeometry(0, 0, 1920, 1080);
    var work = new WindowGeometry(0, 48, 1920, 1040);
    Check(ExpandedWindowPolicy.IsExpanded(work, screen, work, 192, true), "A genuinely maximized window must enter the dedicated scene.");
    Check(!ExpandedWindowPolicy.IsExpanded(new(200, 100, 1400, 900), screen, work, 192, false), "A floating window must never activate maximized appearance.");
    Check(!ExpandedWindowPolicy.IsExpanded(new(0, 48, 960, 1040), screen, work, 192, true), "A stale maximized flag must not promote a half-width window.");
    Check(!ExpandedWindowPolicy.IsExpanded(work, screen, work, 192, false), "An ordinary resized-to-work-area window must stay in the normal scene.");
    Check(ExpandedWindowPolicy.IsExpanded(screen, screen, work, 192, false), "A real borderless fullscreen window must remain supported.");
    Check(!ExpandedWindowPolicy.IsExpanded(new(8, 56, 1912, 1032), screen, work, 192, true), "Loose edge tolerance must not treat an inset window as maximized.");
    var secondary = new WindowGeometry(-2560, -200, 0, 1240);
    var secondaryWork = new WindowGeometry(-2560, -152, 0, 1200);
    Check(ExpandedWindowPolicy.IsExpanded(secondaryWork, secondary, secondaryWork, 144, true)
        && !ExpandedWindowPolicy.IsExpanded(work, secondary, secondaryWork, 144, true), "Scene checks must honor negative coordinates and display boundaries.");
    static BandColorSample[] Band(Func<int, int, uint?> pixel) => Enumerable.Range(0, ExpandedColorPolicy.Columns)
        .SelectMany(x => Enumerable.Range(0, ExpandedColorPolicy.Rows).Select(y => (x, color: pixel(x, y))))
        .Where(p => p.color.HasValue).Select(p => new BandColorSample(p.x, p.color!.Value)).ToArray();
    Check(ExpandedColorPolicy.Representative(Band((x, y) => 0x223344)) == 0x223344, "A full-width solid background must remain detectable.");
    Check(ExpandedColorPolicy.Representative(Band((x, y) => x < 4 ? 0x3366CCu : null)) == null,
        "A small visible fragment must never color the full bar.");
    Check(ExpandedColorPolicy.Representative(Band((x, y) => x is >= 5 and < 10 ? null : 0x223344u)) == null,
        "An occluded middle region must not be renormalized into a full-window sample.");
    Check(ExpandedColorPolicy.Representative(Band((x, y) => x is >= 6 and < 10 ? 0xFF0000u : 0x223344u)) == 0x223344,
        "A localized red panel must not override the broad background.");
    Check(ExpandedColorPolicy.Representative(Band((x, y) => y == 2 ? 0xFFFFFFu : 0x223344u)) == 0x223344,
        "Sparse foreground text must not override a distributed dominant background.");
    Check(ExpandedColorPolicy.Representative(Band((x, y) => x < 8 ? 0xFFFFFFu : 0x000000u)) == null,
        "A split background without a broad consensus must not pick an arbitrary side.");
    var confidence = new ExpandedAppearanceTracker();
    confidence.Observe(new(true, 101, 0x223344), 0); confidence.Observe(new(true, 101, 0x223344), 300);
    Check(confidence.Current.HasColor && confidence.Current.UsesSolid(new(true, ExpandedMode.FollowWindow)), "A verified broad color must enable followed appearance.");
    confidence.Observe(new(true, 101, null), 400); confidence.Observe(new(true, 101, null), 700);
    Check(confidence.Current.HasColor && confidence.Current.Color == 0x223344, "Temporary occlusion may preserve only the same window's verified color.");
    confidence.Observe(new(true, 202, null), 800); confidence.Observe(new(true, 202, null), 1100);
    Check(!confidence.Current.HasColor && !confidence.Current.UsesSolid(new(true, ExpandedMode.FollowWindow))
        && !confidence.Current.UsesSolid(new(true, ExpandedMode.Automatic)), "An unreadable new window must not inherit another window's full-bar color.");
    Check(confidence.Current.UsesSolid(new(true, ExpandedMode.Custom)), "Custom solid mode must depend on scene eligibility, not captured pixels.");
    confidence.Observe(new(false, 202, 0xFF0000), 1200); confidence.Observe(new(false, 202, 0xFF0000), 1500);
    Check(!confidence.Current.UsesSolid(new(true, ExpandedMode.Custom)), "Even a vivid normal window must leave dedicated appearance.");
    var noBase = new MaterialOptions(false, 0, 0, false, "#FF0000", false, true);
    Check(noBase.FlatColor(false).Alpha == 0 && noBase.FlatColor(true).Alpha == 0, "Zero tint and mask must be fully transparent without a base.");
    var redOverlay = (noBase with { TintOpacity = .5f }).FlatColor(false);
    Check(redOverlay.Alpha == 128 && redOverlay.Rgb == 0xFF0000, "Unblurred tint must retain its color and alpha independently.");
    Check((noBase with { LuminosityOpacity = 1 }).FlatColor(false) == (255, 0xFFFFFFu)
        && (noBase with { LuminosityOpacity = 1 }).FlatColor(true) == (255, 0u), "Flat luminosity mask must follow the light/dark theme.");
    var mixedOverlay = (noBase with { TintOpacity = .5f, LuminosityOpacity = .5f }).FlatColor(true);
    Check(mixedOverlay.Alpha == 191 && mixedOverlay.Rgb == 0xAA0000, "Flat layers must use alpha compositing rather than adding their opacities.");
    restored.Preview(noBase); restored.SaveCurrent();
    Check(new AppearanceStore(path).Material == noBase, "No-base selection must survive restart.");
    restored.SavePreset("透明测试"); restored.Save(GlassStyle.Frosted);
    restored.ApplyPreset(restored.Presets.Single(p => p.Name == "透明测试").Id);
    Check(restored.Material.NoBase, "Personal presets must restore no-base mode.");
    restored.Export(exported); imported.Import(exported);
    Check(imported.Material.NoBase && imported.Material.TintOpacity == 0, "Portable configuration must retain transparent mode.");
    string legacyMaterialPath = Path.Combine(directory, "legacy-material.json");
    File.WriteAllText(legacyMaterialPath, "{\"Style\":3,\"Custom\":{\"Thin\":true,\"TintOpacity\":0.3}}");
    var legacyMaterial = new AppearanceStore(legacyMaterialPath).Material;
    Check(!legacyMaterial.NoBase && legacyMaterial.Thin && legacyMaterial.TintOpacity == .3f,
        "Existing material JSON without NoBase must retain its original acrylic base and tint.");
    Console.WriteLine($"PASS: {count} appearance persistence, palette and scene stability checks");
}
finally
{
    // This directory is generated above solely for this test run.
    Directory.Delete(directory, true);
}
