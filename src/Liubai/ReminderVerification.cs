namespace Liubai;

public sealed partial class BarWindow
{
    async Task VerifyReminders()
    {
        string result = "PASS", error = "";
        var checks = new List<string>();
        ReminderService? service = null;
        bool opened = false;
        try
        {
            await Task.Delay(300);
            var at = DateTime.Now.AddMinutes(-2);
            await App.Planner.AddTask("留白提醒验证（测试）", "这是一条独立测试通知，完成后自动清理，不会加入正式待办。",
                DateOnly.FromDateTime(at), TimeOnly.FromDateTime(at), 0);
            service = App.StartReminders(DispatcherQueue, _ => { opened = true; return true; }, verification: true);
            long deadline = Environment.TickCount64 + 25000;
            while (App.Planner.Data.Tasks[0].NotifiedAt == null && service.Warning == null && Environment.TickCount64 < deadline) await Task.Delay(80);
            if (service.Warning != null || App.Planner.Data.Tasks[0].NotifiedAt == null)
                throw new InvalidOperationException(service.Warning + " " + service.Diagnostic);
            checks.Add("windows-registration-and-delivery");
            var token = App.Planner.Data.Tasks[0].ReminderToken;
            if (Environment.GetCommandLineArgs().Contains("--wait-notification-action"))
            {
                deadline = Environment.TickCount64 + 90000;
                while (!opened && !App.Planner.Data.Tasks[0].Completed && App.Planner.Data.Tasks[0].ReminderToken == token && Environment.TickCount64 < deadline) await Task.Delay(100);
                if (!opened && App.Planner.Data.Tasks[0].ReminderToken == token) throw new InvalidOperationException("Timed out waiting for a Windows notification action.");
                checks.Add(opened ? "native-open-action" : App.Planner.Data.Tasks[0].Completed ? "native-complete-action" : "native-snooze-action");
            }
            else await Task.Delay(500);
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            if (service != null) { await service.ClearVerification(); await service.Stop(); }
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "reminder-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Quit();
        }
    }
}
