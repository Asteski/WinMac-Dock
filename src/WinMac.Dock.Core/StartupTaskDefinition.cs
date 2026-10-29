using System.Xml.Linq;

namespace WinMac.Dock.Core;

public static class StartupTaskDefinition
{
    public const string Arguments = "--startup --logon-task";
    public static string NameForUser(string sid) => "WinMac Dock - " + sid;

    public static string CreateXml(string executable, string sid)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(sid))
            throw new ArgumentException("An executable and current-user SID are required.");
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name, object value) => new(ns + name, value);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Description", "Start WinMac Dock at sign-in for this user without a configured delay.")),
            Element("Triggers", new XElement(ns + "LogonTrigger", Element("Enabled", "true"), Element("Delay", "PT0S"), Element("UserId", sid))),
            Element("Principals", new XElement(ns + "Principal", new XAttribute("id", "CurrentUser"),
                Element("UserId", sid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", "false"), Element("StopIfGoingOnBatteries", "false"),
                Element("AllowHardTerminate", "false"), Element("StartWhenAvailable", "true"),
                Element("RunOnlyIfNetworkAvailable", "false"), Element("AllowStartOnDemand", "true"),
                Element("Enabled", "true"), Element("Hidden", "false"), Element("RunOnlyIfIdle", "false"),
                Element("WakeToRun", "false"), Element("ExecutionTimeLimit", "PT0S"), Element("Priority", 7)),
            new XElement(ns + "Actions", new XAttribute("Context", "CurrentUser"),
                new XElement(ns + "Exec", Element("Command", executable), Element("Arguments", Arguments),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable) ?? ""))))
            .ToString();
    }
}
