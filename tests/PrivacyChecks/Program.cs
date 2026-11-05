using Liubai;

string root = Path.GetFullPath(Path.Combine(".build", "privacy-checks-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
PrivacyJournal Initial() => new(Active: true, Entries: [new("camera", "Camera", "摄像头"), new("mic", "Microphone", "麦克风")]);
try
{
    var inputs = new[] { new PrivacyDevice("cam", "Camera", "摄像头", true, false), new PrivacyDevice("mic", "Microphone", "麦克风", true, false), new PrivacyDevice("out", "Speaker", "output", true, false) };
    Check(PrivacySelection.Resolve(new(Devices: []), false, inputs).SequenceEqual(new[] { "cam", "mic" }), "First click defaults to supported inputs only");
    Check(PrivacySelection.Resolve(new(Devices: []), true, inputs).Length == 0, "Explicit empty selection is never replaced");
    Check(PrivacySelection.Resolve(new(Devices: ["mic"]), true, inputs).SequenceEqual(new[] { "mic" }), "Custom selection survives first-click default policy");
    Check(PrivacySelection.Resolve(new(Devices: ["missing"]), true, inputs).SequenceEqual(new[] { "missing" }), "Disconnected selection is not silently substituted");
    Check(PrivacySelection.Resolve(new(Devices: []), false, []).Length == 0, "No devices does not create a false protected state");
    string path = Path.Combine(root, "state.json");
    var hardware = new FakeDevices(); var saved = new List<PrivacyJournal>();
    void Save(PrivacyJournal j) { PrivacyFiles.Write(path, j); saved.Add(j); }
    var entered = PrivacyOperation.Execute(Initial(), false, hardware, Save);
    Check(entered.Active && entered.Entries!.All(e => e.State == "disabled"), "Enter must verify every disabled device");
    Check(saved[0].Entries![0].State == "pending", "Intent must precede hardware mutation");
    Check(hardware.Mutations == 2 && !hardware.Find("camera")!.Enabled && !hardware.Find("mic")!.Enabled, "Both selected inputs disabled");
    Check(hardware.Find("pre-disabled")!.Disabled && hardware.Find("speaker")!.Enabled, "Unselected/pre-disabled devices untouched");
    var restored = PrivacyOperation.Execute(PrivacyFiles.Read(path, Initial()), true, hardware, Save);
    Check(!restored.Active && hardware.Find("camera")!.Enabled && hardware.Find("mic")!.Enabled, "Restart journal restores changed devices");
    Check(hardware.Find("pre-disabled")!.Disabled && hardware.Mutations == 4, "Restoration does not enable originally disabled devices");
    PrivacyOperation.Execute(restored, true, hardware, Save); Check(hardware.Mutations == 4, "Repeated restoration is idempotent");

    hardware = new FakeDevices(); hardware.Fail = "mic";
    entered = PrivacyOperation.Execute(Initial(), false, hardware, Save);
    Check(entered.Entries![0].State == "disabled" && entered.Entries[1].Error.Length > 0, $"Partial failure must be explicit: {System.Text.Json.JsonSerializer.Serialize(entered)}");
    hardware.Fail = ""; restored = PrivacyOperation.Execute(entered, true, hardware, Save);
    Check(!restored.Active && hardware.Mutations == 2, "Unchanged failed device never force-enabled");

    hardware = new FakeDevices(); entered = PrivacyOperation.Execute(Initial(), false, hardware, Save); hardware.Missing = "camera";
    restored = PrivacyOperation.Execute(entered, true, hardware, Save);
    Check(restored.Active && restored.Entries![0].Error.Length > 0 && hardware.Find("mic")!.Enabled, "Unplugged device retains recovery without blocking other devices");
    hardware.Missing = ""; restored = PrivacyOperation.Execute(restored, true, hardware, Save);
    Check(!restored.Active && hardware.Find("camera")!.Enabled, "Reconnect can complete restoration");

    hardware = new FakeDevices();
    var interrupted = Initial() with { Entries = [new("camera", "Camera", "摄像头", "pending")] };
    hardware.SetEnabled("camera", false);
    restored = PrivacyOperation.Execute(interrupted, true, hardware, Save);
    Check(!restored.Active && hardware.Find("camera")!.Enabled, "Crash after mutation before result still recoverable");
    int before = hardware.Mutations; restored = PrivacyOperation.Execute(interrupted, true, hardware, Save);
    Check(!restored.Active && hardware.Mutations == before, "Crash before mutation does not force-enable already active input");
    var planned = Initial() with { Entries = [new("pre-disabled", "Off", "摄像头")] };
    restored = PrivacyOperation.Execute(planned, true, hardware, Save);
    Check(!restored.Active && hardware.Find("pre-disabled")!.Disabled, "Canceled authorization leaves original disabled inputs off");
    var changedBeforeEnter = PrivacyOperation.Execute(planned, false, hardware, Save);
    Check(changedBeforeEnter.Entries![0].State == "restored" && hardware.Find("pre-disabled")!.Disabled, "Device disabled between snapshot and operation is not owned");

    hardware = new FakeDevices(); bool rejected = false;
    try { PrivacyOperation.Execute(Initial(), false, hardware, _ => throw new IOException("disk full")); } catch (IOException) { rejected = true; }
    Check(rejected && hardware.Mutations == 0, "No hardware mutation without durable intent");
    foreach (var invalid in new[] { new PrivacyJournal(2, true, []), new PrivacyJournal(Entries: [new("x\n", "n", "摄像头")]), new PrivacyJournal(Entries: [new("id", "n", "speaker")]), new PrivacyJournal(Entries: [new("ID", "n", "摄像头"), new("id", "n", "摄像头")]) })
    {
        rejected = false; try { PrivacyFiles.Validate(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Malformed journal rejected");
    }
    Check(PrivacyNative.Kind("SWD\\MMDEVAPI\\{0.0.1.00000000}.x", "AudioEndpoint", "") == "麦克风", "Only capture endpoints recognized");
    Check(PrivacyNative.Kind("SWD\\MMDEVAPI\\{0.0.0.00000000}.x", "AudioEndpoint", "") is null, "Render endpoint never targeted");
    Check(PrivacyNative.Kind("USB\\headset", "MEDIA", "usbaudio") is null, "Whole audio adapter never targeted");
    Check(PrivacyNative.Kind("USB\\scanner", "Image", "scanner") is null, "Scanner never targeted");
    Check(PrivacyNative.Kind("USB\\camera", "Image", "usbvideo") == "摄像头", "Legacy USB video recognized");
    File.WriteAllText(path, "{broken"); rejected = false;
    try { PrivacyFiles.Read(path, Initial()); } catch (System.Text.Json.JsonException) { rejected = true; }
    Check(rejected && File.ReadAllText(path) == "{broken", "Corrupt recovery record is preserved");
    if (args.Contains("--enumerate"))
    {
        var actual = new PrivacyNative().List();
        Check(actual.All(d => d.Kind is "摄像头" or "麦克风"), "Native read-only enumeration filters device classes");
        Console.WriteLine($"Read-only enumeration: {actual.Count(d => d.Kind == "摄像头")} camera(s), {actual.Count(d => d.Kind == "麦克风")} capture endpoint(s); no device modified.");
    }
    Console.WriteLine($"PASS: {checks} privacy checks");
}
finally { Directory.Delete(root, true); }

sealed class FakeDevices : IPrivacyDevices
{
    readonly Dictionary<string, PrivacyDevice> values = new() {
        ["camera"] = new("camera", "Camera", "摄像头", true, false), ["mic"] = new("mic", "Microphone", "麦克风", true, false),
        ["pre-disabled"] = new("pre-disabled", "Off", "摄像头", false, true), ["speaker"] = new("speaker", "Speaker", "output", true, false) };
    internal string Missing = "", Fail = ""; internal int Mutations;
    public PrivacyDevice? Find(string id) => id == Missing ? null : values.GetValueOrDefault(id);
    public void SetEnabled(string id, bool enabled)
    { if (id == Fail) throw new IOException("driver veto"); values[id] = values[id] with { Enabled = enabled, Disabled = !enabled }; Mutations++; }
}
