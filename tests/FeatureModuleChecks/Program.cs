using Liubai;

int count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
int attempts = 0, releases = 0;
var module = new FeatureModule<object>(() => { attempts++; if (attempts == 1) throw new IOException("retryable"); return new(); });
var independent = new FeatureModule<object>(() => new());
Check(module.Current == null && attempts == 0, "Passive state created feature");
module.Release(_ => releases++);
Check(releases == 0 && attempts == 0, "Stopping unused feature created it");
try { module.Get(); throw new Exception("Expected fault"); } catch (IOException) { }
Check(module.Current == null && module.LastError == "retryable", "Failed construction was cached");
Check(independent.Get() != null && independent.LastError == null, "Fault propagated to another module");
var first = module.Get();
Check(attempts == 2 && module.LastError == null, "Retry did not recover");
Check(ReferenceEquals(first, module.Get()) && attempts == 2, "Multiple consumers created duplicate resources");
try { module.Release(_ => throw new IOException("busy")); throw new Exception("Expected fault"); } catch (IOException) { }
Check(ReferenceEquals(first, module.Current), "Failed cleanup lost resource owner");
module.Release(_ => releases++);
Check(module.Current == null && releases == 1, "Cleanup did not clear feature");
module.Release(_ => releases++);
Check(releases == 1, "Cleanup was not idempotent");
Check(!ReferenceEquals(first, module.Get()) && attempts == 3, "Reopen reused stopped feature");
Console.WriteLine($"PASS: {count} feature lifecycle checks");
