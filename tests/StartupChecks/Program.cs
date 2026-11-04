using Liubai;
using System.Xml.Linq;

int count = 0;
void Check(bool success, string name) { count++; if (!success) throw new Exception(name); }
string executable = Path.GetFullPath(@"D:\Apps\留白 & tools\dist\Liubai.exe"), sid = "S-1-5-21-111-222-333-1001";
string xml = StartupDefinition.Build(executable, sid);
XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
var task = XDocument.Parse(xml);
string Value(string name) => task.Descendants(ns + name).First().Value;
Check(Value("Command") == executable, "Unicode, space and ampersand in executable path");
Check(Value("WorkingDirectory") == Path.GetDirectoryName(executable), "Explicit working directory");
Check(Value("Arguments") == "--startup", "Startup diagnostic mode");
Check(Value("LogonType") == "InteractiveToken", "Interactive desktop, no stored password");
Check(Value("RunLevel") == "LeastPrivilege", "No administrator privileges");
Check(Value("Delay") == "PT15S", "Delayed login trigger");
Check(Value("UserId") == sid && task.Descendants(ns + "UserId").All(x => x.Value == sid), "Only the current user");
Check(Value("ExecutionTimeLimit") == "PT0S", "Resident app never times out");
Check(Value("DisallowStartIfOnBatteries") == "false" && Value("StopIfGoingOnBatteries") == "false", "Battery does not suppress resident app");
Check(Value("StartWhenAvailable") == "true", "Recover missed launch");
Check(Value("MultipleInstancesPolicy") == "IgnoreNew", "Single task instance");
Check(Value("Interval") == "PT1M" && Value("Count") == "3", "Bounded failure retries");
Check(StartupDefinition.Matches(xml, executable, sid), "Fresh registration matches");
Check(!StartupDefinition.Matches(xml, executable + ".old", sid), "Moved executable needs repair");
Check(!StartupDefinition.Matches(xml, executable, sid + "2"), "Wrong user rejected");
task.Root!.Element(ns + "Settings")!.Element(ns + "Enabled")!.Value = "false";
Check(StartupDefinition.Matches(task.ToString(), executable, sid), "Disabled task remains disabled without constant repair");
task.Root.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "Enabled")!.Value = "false";
Check(!StartupDefinition.Matches(task.ToString(), executable, sid), "Disabled trigger cannot be reported as working startup");
task = XDocument.Parse(xml); task.Descendants(ns + "WorkingDirectory").Single().Remove();
Check(!StartupDefinition.Matches(task.ToString(), executable, sid), "Missing working directory repaired");
task = XDocument.Parse(xml); task.Root!.Element(ns + "Actions")!.Add(new XElement(ns + "Exec", new XElement(ns + "Command", "other.exe")));
Check(!StartupDefinition.Matches(task.ToString(), executable, sid), "Unexpected launch action rejected");
Check(!StartupDefinition.Matches("broken", executable, sid) && !StartupDefinition.Matches("<!-- no task -->", executable, sid), "Malformed registration rejected");
task = XDocument.Parse(xml);
task.Descendants(ns + "Enabled").Remove(); task.Descendants(ns + "RunLevel").Remove();
task.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value = "PC\\User";
Check(StartupDefinition.Matches(task.ToString(), executable, sid, "PC\\User"), "Schema defaults and account name normalization accepted");
Check(!StartupDefinition.Matches(task.ToString(), executable, sid, "PC\\Other"), "Another user's logon trigger rejected");
if (args.Length == 1)
{
    string registeredXml = File.ReadAllText(args[0]);
    var registered = XDocument.Parse(registeredXml);
    Check(StartupDefinition.Matches(registeredXml, registered.Descendants(ns + "Command").Single().Value,
        registered.Descendants(ns + "Principal").Single().Element(ns + "UserId")!.Value,
        registered.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value), "Windows-normalized task matches without repeated re-registration");
}
Console.WriteLine($"PASS: {count} login task, current user, working directory, retry and disabled-state checks");
