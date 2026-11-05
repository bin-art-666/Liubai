using Liubai;
using System.Xml.Linq;

if (args.Contains("--session-state")) { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Locked = SessionState.Locked })); return; }

int checks = 0;
void Check(bool result, string name) { checks++; if (!result) throw new Exception(name); }
async Task Reject(Func<Task> action, string name)
{ bool rejected = false; try { await action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { rejected = true; } Check(rejected, name); }
string root = Path.Combine(Path.GetTempPath(), "Liubai.ClockChecks." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
ClockStore Store(string name) => new(Path.Combine(root, name + ".json"));
var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
var zone = TimeZoneInfo.Utc;
try
{
    Check(ClockPolicy.NextAlarm(new(13, 0), now, zone) == new DateTime(2026, 11, 4, 13, 0, 0), "Later today");
    Check(ClockPolicy.NextAlarm(new(11, 0), now, zone).Date == new DateTime(2026, 11, 5), "Past time tomorrow");
    Check(ClockPolicy.NextAlarm(new(12, 0), now, zone).Date == new DateTime(2026, 11, 5), "Exact minute tomorrow");
    Check(ClockPolicy.NextAlarm(new(0, 1), now.Date.AddHours(23).AddMinutes(55), zone).Date == new DateTime(2026, 11, 5), "Across midnight");
    Check(ClockPolicy.Format(TimeSpan.FromMilliseconds(1750)) == "00:02", "Display rounds remaining up");
    Check(ClockPolicy.Format(TimeSpan.FromHours(25)) == "25:00:00" && ClockPolicy.Format(TimeSpan.FromSeconds(-1)) == "00:00", "Long and expired display");
    var dst = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    var missing = new DateTime(2026, 3, 8, 2, 30, 0);
    Check(dst.IsInvalidTime(missing) && ClockPolicy.Resolve(missing, dst).Hour == 3, "DST missing local minute");
    var repeated = new DateTime(2026, 11, 1, 1, 30, 0);
    Check(dst.IsAmbiguousTime(repeated) && ClockPolicy.Resolve(repeated, dst).Offset == dst.BaseUtcOffset, "DST repeated hour deterministic");
    var store = Store("main");
    Check(!store.Data.ShowRemainingInBar, "Remaining label defaults off");
    Check(!System.Text.Json.JsonSerializer.Deserialize<ClockDocument>("{\"Version\":1,\"Alarms\":[],\"Countdown\":null}")!.ShowRemainingInBar, "Older clock data defaults off");
    await store.AddAlarm(new(13, 0), "喝水 & <休息>", now, zone);
    var alarm = store.Data.Alarms.Single();
    Check(alarm.State == ClockState.Running && ClockPolicy.Due(alarm, zone) == now.AddHours(1), "Alarm saved");
    Check(!ClockPolicy.IsDue(alarm, now, zone) && ClockPolicy.IsDue(alarm, now.AddHours(1), zone), "Alarm exact deadline");
    Check(ClockPolicy.Due(alarm, TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test")) == now.AddHours(-1), "Alarm follows local timezone");
    var snapshot = store.Data; snapshot.Alarms[0] = alarm with { Title = "tampered" };
    Check(store.Data.Alarms[0].Title == alarm.Title, "External snapshots cannot mutate live state");
    var originalToken = alarm.Token;
    await store.EditAlarm(alarm.Id, new(14, 0), "edited", true, now, zone);
    Check(store.Data.Alarms[0].Token != originalToken, "Editing invalidates old notification");
    Check(!await store.ApplyAction(new(alarm.Id, originalToken, ClockActionKind.Stop), now.AddHours(3)), "Old notification ignored");
    await store.SetAlarmEnabled(alarm.Id, false, now);
    Check(!store.HasPending, "Disabled alarm no longer pending");
    await store.SetAlarmEnabled(alarm.Id, true, now);
    Check(store.HasPending && store.Data.Alarms[0].State == ClockState.Running, "Re-enable schedules next local time");
    await store.StartCountdown(25, "休息", now);
    Check(ClockPolicy.PreferCountdown(store.Data), "Running timer opens timer page");
    Check(!ClockPolicy.PreferCountdown(store.Data, alarm.Id) && ClockPolicy.PreferCountdown(store.Data, store.Data.Countdown!.Id), "Notification target overrides active timer");
    Check(ClockPolicy.ToolbarText(store.Data, now, zone) == "", "Default toolbar remains icon only");
    var labelToken = store.Data.Countdown!.Token;
    await store.SetShowRemainingInBar(true);
    Check(ClockPolicy.ToolbarText(store.Data, now, zone) == "00:25" && Store("main").Data.ShowRemainingInBar, "Remaining display persists without changing countdown");
    Check(store.Data.Countdown.Token == labelToken && store.Data.Countdown.EndsAt == now.AddSeconds(25), "Display preference never reschedules timer");
    Check(store.Data.Countdown?.EndsAt == now.AddSeconds(25), "Timer stores absolute deadline");
    Check(ClockPolicy.Remaining(store.Data.Countdown!, now.AddSeconds(10), zone) == TimeSpan.FromSeconds(15), "No tick accumulation drift");
    await Reject(() => store.StartCountdown(5, "second", now), "Only one active timer");
    await store.PauseCountdown(now.AddMilliseconds(7250));
    var paused = store.Data.Countdown!;
    Check(ClockPolicy.PreferCountdown(store.Data) && ClockPolicy.ToolbarText(store.Data, now, zone) == "Ⅱ", "Paused timer opens timer page and shows pause symbol");
    Check(paused.State == ClockState.Paused && paused.RemainingTicks == TimeSpan.FromMilliseconds(17750).Ticks, "Subsecond pause precision");
    var reopened = Store("main");
    Check(reopened.Data.Countdown?.RemainingTicks == paused.RemainingTicks, "Paused timer persists across restart");
    Check(!ClockPolicy.IsDue(paused, now.AddDays(1), zone), "Paused time does not elapse during sleep");
    await reopened.ResumeCountdown(now.AddHours(2));
    Check(reopened.Data.Countdown?.EndsAt == now.AddHours(2).AddMilliseconds(17750), "Resume restores exact remaining duration");
    await reopened.CancelCountdown(); Check(reopened.Data.Countdown == null, "Cancel removes countdown");
    Check(!ClockPolicy.PreferCountdown(reopened.Data) && ClockPolicy.ToolbarText(reopened.Data, now, zone) == "", "Canceled timer restores alarm page and icon-only toolbar");
    await Reject(() => reopened.StartCountdown(0, "", now), "Zero duration rejected");
    await Reject(() => reopened.StartCountdown(ClockPolicy.MaximumSeconds + 1, "", now), "Duration overflow rejected");
    await Reject(() => reopened.AddAlarm(new(15, 0), new string('x', 121), now), "Oversized title rejected");
    await reopened.StartCountdown(1, "notify", now);
    var notices = new Notices(); var coordinator = new ClockCoordinator(reopened, notices, _ => { });
    notices.Accept = false;
    await coordinator.Tick(now.AddSeconds(2), zone);
    Check(reopened.Data.Countdown?.State == ClockState.Running && reopened.Data.Countdown.NotifiedAt == null, "Notification disabled does not mark delivered");
    notices.Accept = true;
    await coordinator.Tick(now.AddHours(3), zone);
    Check(reopened.Data.Countdown?.State == ClockState.Finished && reopened.Data.Countdown.NotifiedAt != null, "Missed timer delivered on wake");
    int sent = notices.Sent; await coordinator.Tick(now.AddDays(1), zone); Check(notices.Sent == sent, "No duplicate delivery");
    var finished = reopened.Data.Countdown!;
    Check(!ClockPolicy.PreferCountdown(reopened.Data) && ClockPolicy.ToolbarText(reopened.Data, now, zone) == "", "Finished timer hides label and defaults to alarms");
    var repeat = new ClockAction(finished.Id, finished.Token, ClockActionKind.Repeat);
    Check(await coordinator.Handle(repeat, now), "Repeat notification action accepted");
    Check(!await coordinator.Handle(repeat, now), "Repeated action cannot restart twice");
    Check(reopened.Data.Countdown?.EndsAt == now.AddSeconds(1), "Repeat uses original duration");
    Check(!await reopened.ApplyAction(new(finished.Id, reopened.Data.Countdown!.Token, ClockActionKind.Snooze), now.AddSeconds(2)), "Timer rejects alarm snooze");
    await reopened.CancelCountdown();
    var snoozeStore = Store("snooze"); await snoozeStore.AddAlarm(new(12, 1), "alarm", now, zone);
    var snoozeNotices = new Notices(); var snoozer = new ClockCoordinator(snoozeStore, snoozeNotices, _ => { });
    await snoozer.Tick(now.AddMinutes(2), zone); var fired = snoozeStore.Data.Alarms.Single();
    Check(await snoozer.Handle(new(fired.Id, fired.Token, ClockActionKind.Snooze), now.AddMinutes(2)), "Alarm snooze accepted");
    Check(snoozeStore.Data.Alarms.Single().SnoozeUntil == now.AddMinutes(7), "Snooze exactly five minutes");
    Check(!await snoozer.Handle(new(fired.Id, fired.Token, ClockActionKind.Stop), now.AddMinutes(2)), "Old snoozed token cannot stop new schedule");
    await snoozer.Tick(now.AddMinutes(7), zone); Check(snoozeNotices.Sent == 2, "Snoozed alarm rings again once");
    var currentAlarm = snoozeStore.Data.Alarms.Single();
    Check(await snoozer.Handle(new(currentAlarm.Id, currentAlarm.Token, ClockActionKind.Stop), now.AddMinutes(7)) && !snoozeStore.HasPending, "Stop alarm disables one-shot");
    var payload = XDocument.Parse(ClockPolicy.Payload(alarm, now.AddHours(2), zone));
    Check(payload.Root?.Attribute("scenario")?.Value == "alarm" && payload.Descendants("text").First().Value == alarm.Title, "System alarm with escaped text");
    foreach (ClockActionKind kind in Enum.GetValues<ClockActionKind>())
        Check(ClockPolicy.TryParse(ClockPolicy.Arguments(alarm, kind), out var parsed) && parsed == new ClockAction(alarm.Id, alarm.Token, kind), "Action roundtrip " + kind);
    Check(!ClockPolicy.TryParse("clock|stop|bad|bad", out _) && !ClockPolicy.TryParse(ClockPolicy.Arguments(alarm, ClockActionKind.Stop) + "|extra", out _), "Malformed actions rejected");
    Check(XDocument.Parse(ClockPolicy.Payload(finished, now, zone, true)).Descendants("audio").Single().Attribute("silent")?.Value == "true", "Verification is silent");
    var writeStore = Store("write-failure"); await writeStore.StartCountdown(1, "durable", now);
    var writeNotices = new Notices(); var writeCoordinator = new ClockCoordinator(writeStore, writeNotices, _ => { });
    string blocked = Path.Combine(root, "write-failure.json.tmp"); Directory.CreateDirectory(blocked);
    await Reject(() => writeCoordinator.Tick(now.AddSeconds(2), zone), "Delivery stamp write failure reported");
    Directory.Delete(blocked); await writeCoordinator.Tick(now.AddSeconds(3), zone);
    Check(writeNotices.Sent == 1 && writeStore.Data.Countdown?.State == ClockState.Finished, "Successful notification is not resent on stamp retry");
    var suspended = Store("suspended"); await suspended.StartCountdown(1, "", now);
    var suspendedNotices = new Notices(); var suspendedCoordinator = new ClockCoordinator(suspended, suspendedNotices, _ => { }, () => false);
    await suspendedCoordinator.Tick(now.AddDays(1), zone); Check(suspendedNotices.Sent == 0, "Suspension prevents delivery");
    var capacity = Store("capacity"); await Task.WhenAll(Enumerable.Range(0, ClockStore.AlarmCapacity).Select(i => capacity.AddAlarm(new(15, i), "", now, zone)));
    Check(capacity.Data.Alarms.Length == ClockStore.AlarmCapacity, "Concurrent saves serialize");
    await Reject(() => capacity.AddAlarm(new(16, 0), "", now, zone), "Alarm capacity enforced atomically");
    await capacity.RemoveAlarm(capacity.Data.Alarms[0].Id); Check(capacity.Data.Alarms.Length == ClockStore.AlarmCapacity - 1, "Remove alarm");
    string future = Path.Combine(root, "future.json"); File.WriteAllText(future, "{\"Version\":99,\"Alarms\":[]}");
    var futureStore = new ClockStore(future); Check(futureStore.ReadOnly, "Future version protected");
    await Reject(() => futureStore.StartCountdown(1, "", now), "Future version cannot be overwritten");
    Check(File.ReadAllText(future).Contains("99"), "Future file unchanged");
    string damaged = Path.Combine(root, "damaged.json"); File.WriteAllText(damaged, "bad");
    File.Copy(Path.Combine(root, "main.json"), damaged + ".bak");
    Check(!new ClockStore(damaged).ReadOnly && new ClockStore(damaged).LoadWarning != null, "Valid backup recovers corrupt primary");
    File.WriteAllText(damaged + ".bak", "also bad"); Check(new ClockStore(damaged).ReadOnly, "Both corrupt stops writes");
    Console.WriteLine($"PASS: {checks} clock scheduling, pause/resume, recovery, notification, replay and persistence checks");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
finally
{
    if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
        && !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(p => File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))) Directory.Delete(root, true);
}

sealed class Notices : IClockNotifications
{
    public int Sent; public bool Accept = true; public string? Warning => Accept ? null : "disabled";
    public Task ReconcileClock(IReadOnlyList<ClockEntry> entries) => Task.CompletedTask;
    public Task<bool> ShowClock(ClockEntry item, DateTimeOffset now, TimeZoneInfo zone) { if (Accept) Sent++; return Task.FromResult(Accept); }
    public Task ClearClock() => Task.CompletedTask;
}
