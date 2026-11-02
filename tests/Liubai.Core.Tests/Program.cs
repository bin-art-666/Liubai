using Liubai.Modern;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var hiding = new AutoHideState();
Check(!hiding.Update(false, false), "Starts hidden");
Check(!hiding.Update(false, true), "Hover elsewhere does not reveal a hidden bar");
Check(hiding.Update(true, false), "Edge reveals");
Check(hiding.Update(false, true), "Moving from edge into bar keeps visible");
Check(hiding.Update(false, true), "Hovering the panel or connecting gap keeps visible");
Check(!hiding.Update(false, false), "Leaving surfaces hides immediately");
Check(!hiding.Update(false, false), "Remains hidden outside surfaces");
Check(hiding.Update(true, false), "Edge reentry reopens");
Check(hiding.Update(false, false, true), "Editing dialog keeps visible");
Check(!hiding.Update(false, false), "Closing dialog outside hides immediately");
hiding.Reveal();
Check(hiding.Update(false, true), "Explicit open stays visible while hovering");
Check(!hiding.Update(false, false), "Explicit open also hides on leave");

string folder = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("test-output", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "todos.json");
// Exact JSON field format used by the original WinForms application.
File.WriteAllText(path, "{\"Items\":[{\"Done\":false,\"Id\":\"legacy-id\",\"Text\":\"旧版中文待办\"}]}");
var store = new TodoStore(path);
Check(store.Data.Items.Count == 1 && store.Data.Items[0].Text == "旧版中文待办", "Legacy data compatibility");
store.Data.Items[0].Done = true;
store.Data.Items.Add(new Todo { Text = "新界面待办" }); store.Save();
var reloaded = new TodoStore(path);
Check(reloaded.Data.Items.Count == 2 && reloaded.Data.Items[0].Done, "Save completion and Unicode");
reloaded.Data.Items[1].Text = "编辑后的内容"; reloaded.Save();
Check(new TodoStore(path).Data.Items[1].Text == "编辑后的内容", "Edit persists");
File.WriteAllText(path, "invalid");
var recovered = new TodoStore(path);
Check(recovered.Data.Items.Count == 2 && recovered.Warning != null, "Backup recovery");
Check(Directory.GetFiles(folder, "*.unreadable-*").Length == 1, "Preserves corrupt original");
recovered.Data.Items.Clear(); recovered.Save();
Check(new TodoStore(path).Data.Items.Count == 0, "Deletion persists");
Check(hiding.Update(false, false, false, false), "Resident mode stays visible away from bar");
Check(!hiding.Update(false, false, false, true), "Switching back to auto-hide hides outside");
string modePath = Path.Combine(folder, "display.json");
var mode = new BarSettings(modePath);
Check(!mode.AutoHide && !File.Exists(modePath), "Default is resident without writing on load");
mode.Save(true); Check(new BarSettings(modePath).AutoHide, "Auto-hide choice survives restart");
mode.Save(false); Check(!new BarSettings(modePath).AutoHide, "Resident choice survives restart");
File.WriteAllText(modePath, "invalid");
Check(new BarSettings(modePath).AutoHide, "Display choice recovers from previous backup");
Console.WriteLine("PASS: auto-hide/resident switching and persistence; immediate hide, legacy todos, editing, completion and backup recovery.");
