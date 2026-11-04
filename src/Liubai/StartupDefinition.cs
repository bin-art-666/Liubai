using System.Xml.Linq;

namespace Liubai;

internal static class StartupDefinition
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    internal static string Build(string executable, string userSid)
    {
        executable = Path.GetFullPath(executable);
        XElement Element(string name, object value) => new(Ns + name, value);
        return new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Description", "留白：登录后延迟启动，异常退出时重试。")),
            Element("Triggers", Element("LogonTrigger", new object[] { Element("Enabled", true), Element("UserId", userSid), Element("Delay", "PT15S") })),
            Element("Principals", new XElement(Ns + "Principal", new XAttribute("id", "CurrentUser"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "LeastPrivilege"))),
            Element("Settings", new object[] {
                Element("MultipleInstancesPolicy", "IgnoreNew"), Element("DisallowStartIfOnBatteries", false),
                Element("StopIfGoingOnBatteries", false), Element("AllowHardTerminate", false),
                Element("StartWhenAvailable", true), Element("Enabled", true), Element("ExecutionTimeLimit", "PT0S"),
                Element("RestartOnFailure", new object[] { Element("Interval", "PT1M"), Element("Count", 3) }) }),
            new XElement(Ns + "Actions", new XAttribute("Context", "CurrentUser"),
                Element("Exec", new object[] { Element("Command", executable), Element("Arguments", "--startup"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable)!) }))).ToString(SaveOptions.DisableFormatting);
    }
    internal static bool Matches(string xml, string executable, string userSid, string? accountName = null)
    {
        try
        {
            var actual = XDocument.Parse(xml).Root;
            if (actual is null) return false;
            var expected = XDocument.Parse(Build(executable, userSid)).Root!;
            // Windows omits schema defaults and may normalize the logon trigger's
            // SID into DOMAIN\name (the principal still contains the SID).
            bool OmittedDefault(XElement source, XElement wanted) => !source.Elements(wanted.Name).Any()
                && (wanted.Name == Ns + "Enabled" && wanted.Value == "true"
                    || wanted.Name == Ns + "RunLevel" && wanted.Value == "LeastPrivilege");
            bool Contains(XElement source, XElement wanted) => wanted.Attributes().All(a => (string?)source.Attribute(a.Name) == a.Value)
                && (wanted.HasElements ? wanted.Elements().All(w => source.Elements(w.Name).Any(s => Contains(s, w)) || OmittedDefault(source, w))
                    : wanted.Name == Ns + "Enabled" && wanted.Parent?.Name == Ns + "Settings"
                        || source.Value.Equals(wanted.Value, StringComparison.OrdinalIgnoreCase)
                        || wanted.Name == Ns + "UserId" && wanted.Parent?.Name == Ns + "LogonTrigger"
                            && accountName is not null && source.Value.Equals(accountName, StringComparison.OrdinalIgnoreCase));
            return actual.Elements(Ns + "Triggers").Elements().Count() == 1
                && actual.Elements(Ns + "Actions").Elements().Count() == 1
                && expected.Elements().Where(e => e.Name != Ns + "RegistrationInfo").All(w => actual.Elements(w.Name).Any(s => Contains(s, w)));
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException or InvalidOperationException) { return false; }
    }
}
