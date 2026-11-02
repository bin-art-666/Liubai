using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Liubai
{
    [DataContract] public class Todo
    {
        [DataMember] public string Id = Guid.NewGuid().ToString();
        [DataMember] public string Text;
        [DataMember] public bool Done;
    }
    [DataContract] public class State
    {
        [DataMember] public List<Todo> Items = new List<Todo>();
    }
    public class Store
    {
        public readonly string Path;
        public State Data = new State();
        public string Warning;
        public Store(string path)
        {
            Path = path;
            if (!File.Exists(path)) return;
            try { Data = Read(path); }
            catch
            {
                // Keep the unreadable file separate so future saves cannot erase it.
                try { File.Copy(path, path + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")); } catch { }
                try { Data = Read(path + ".bak"); Warning = "已从备份恢复待办。"; }
                catch { Warning = "待办文件无法读取，原文件已保留。"; }
            }
        }
        static State Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var state = (State)new DataContractJsonSerializer(typeof(State)).ReadObject(stream);
                if (state == null || state.Items == null || state.Items.Any(x => x == null || x.Text == null))
                    throw new InvalidDataException();
                return state;
            }
        }
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string temp = Path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(State)).WriteObject(stream, Data);
                stream.Flush(true);
            }
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak");
            else File.Move(temp, Path);
        }
    }
    static class Program
    {
        public static bool Inspect;
        public static void Trace(string message)
        {
            try { File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log"), DateTime.Now.ToString("O") + " " + message + Environment.NewLine); } catch { }
        }
        [STAThread] static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTest(args.Last());
            Inspect = args.Contains("--inspect");
            using (var mutex = new System.Threading.Mutex(true, "Local\\Liubai.DesktopBar", out acquired))
            {
                if (!acquired) return 0;
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liubai", "todos.json");
                int dataIndex = Array.IndexOf(args, "--data-dir");
                if (dataIndex >= 0 && dataIndex + 1 < args.Length) path = System.IO.Path.Combine(args[dataIndex + 1], "todos.json");
                try { Application.Run(new Bar(new Store(path))); }
                catch (Exception ex) { Trace(ex.ToString()); return 1; }
            }
            return 0;
        }
        static bool acquired;
        static int SelfTest(string directory)
        {
            string path = System.IO.Path.Combine(directory, "todos-test.json");
            try
            {
                var hiding = new AutoHideState();
                if (hiding.Update(0, false, false)) throw new Exception("Bar must start hidden");
                if (!hiding.Update(100, true, false) || !hiding.Update(3099, true, false)) throw new Exception("Edge reveal failed");
                if (hiding.Update(3100, true, false) || hiding.Update(3200, true, false)) throw new Exception("Three-second hide or edge latch failed");
                hiding.Update(3300, false, false);
                if (!hiding.Update(3400, true, false)) throw new Exception("Edge re-entry failed");
                if (!hiding.Update(6500, false, true)) throw new Exception("Interaction must keep bar visible");
                if (!hiding.Update(9499, false, false) || hiding.Update(9500, false, false)) throw new Exception("Hide after interaction failed");
                var store = new Store(path);
                store.Data.Items.Add(new Todo { Text = "中文待办 / 重启后继续" }); store.Save();
                var loaded = new Store(path);
                if (loaded.Data.Items.Count != 1 || loaded.Data.Items[0].Text != "中文待办 / 重启后继续") throw new Exception("Persistence failed");
                loaded.Data.Items[0].Done = true; loaded.Save();
                if (!new Store(path).Data.Items[0].Done) throw new Exception("Completion failed");
                File.WriteAllText(path, "broken");
                var recovered = new Store(path);
                if (recovered.Warning == null || recovered.Data.Items.Count != 1) throw new Exception("Recovery failed");
                recovered.Data.Items.Clear(); recovered.Save();
                if (new Store(path).Data.Items.Count != 0) throw new Exception("Deletion failed");
                File.WriteAllText(System.IO.Path.Combine(directory, "test-result.txt"), "PASS: hidden startup, edge reveal, 3-second hide, edge re-entry, interaction pause; Unicode persistence, completion, backup recovery, deletion.");
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(directory, "test-result.txt"), ex.ToString()); return 1; }
        }
    }
    // Edge entry opens once; remaining at the edge must not reopen after timeout.
    public class AutoHideState
    {
        public bool Visible { get; private set; }
        bool wasAtEdge;
        long deadline;
        public void Reveal(long now) { Visible = true; deadline = now + 3000; }
        public bool Update(long now, bool atEdge, bool interacting)
        {
            if (atEdge && !wasAtEdge && !Visible) Reveal(now);
            wasAtEdge = atEdge;
            if (interacting) deadline = now + 3000;
            else if (Visible && now >= deadline) Visible = false;
            return Visible;
        }
    }
    // Square-check outline, drawn as vectors so it remains crisp at system DPI.
    public class TodoButton : Button
    {
        public int PendingCount;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float dpi = e.Graphics.DpiX / 96F;
            float size = 18 * dpi;
            string count = PendingCount > 0 ? PendingCount.ToString() : "";
            var countSize = count.Length > 0 ? TextRenderer.MeasureText(count, Font) : Size.Empty;
            float total = size + (count.Length > 0 ? 5 * dpi + countSize.Width : 0);
            float left = (Width - total) / 2F, top = (Height - size) / 2F;
            var saved = e.Graphics.Save();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform(left, top);
            e.Graphics.ScaleTransform(size / 24F, size / 24F);
            using (var pen = new Pen(ForeColor, 2F))
            using (var outline = new System.Drawing.Drawing2D.GraphicsPath())
            {
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                outline.AddArc(3, 3, 4, 4, 180, 90);
                outline.AddArc(17, 3, 4, 4, 270, 90);
                outline.AddArc(17, 17, 4, 4, 0, 90);
                outline.AddArc(3, 17, 4, 4, 90, 90);
                outline.CloseFigure();
                e.Graphics.DrawPath(pen, outline);
                e.Graphics.DrawLines(pen, new[] { new PointF(9, 12), new PointF(11, 14), new PointF(15, 10) });
            }
            e.Graphics.Restore(saved);
            if (count.Length > 0)
                TextRenderer.DrawText(e.Graphics, count, Font, new Rectangle((int)(left + size + 5 * dpi), 0, countSize.Width, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }
    public class Bar : Form
    {
        readonly Store store;
        readonly TodoButton trigger;
        readonly TodoWindow popup;
        readonly Timer edgeTimer = new Timer { Interval = 50 };
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        readonly AutoHideState autoHide = new AutoHideState();
        protected override bool ShowWithoutActivation { get { return true; } }
        public Bar(Store store)
        {
            this.store = store;
            Text = "留白 · 桌面栏"; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = Program.Inspect; TopMost = true; BackColor = Color.FromArgb(246, 247, 249);
            Font = new Font("Microsoft YaHei UI", 9F);
            var brand = new Label { Text = "留白", AutoSize = true, Location = new Point(18, 8), ForeColor = Color.FromArgb(83, 90, 102), Cursor = Cursors.Hand };
            Controls.Add(brand);
            var right = new Panel { Dock = DockStyle.Right, Width = 204 };
            trigger = new TodoButton { AccessibleName = "待办", FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill, Cursor = Cursors.Hand, ForeColor = Color.FromArgb(49, 58, 72) };
            trigger.FlatAppearance.BorderSize = 0;
            var todoTip = new ToolTip(); todoTip.SetToolTip(trigger, "待办");
            trigger.Disposed += delegate { todoTip.Dispose(); };
            var settings = new Button { Text = "\uE713", Font = new Font("Segoe MDL2 Assets", 12F), AccessibleName = "设置", FlatStyle = FlatStyle.Flat, Width = 72, Dock = DockStyle.Right, Cursor = Cursors.Hand, ForeColor = Color.FromArgb(83, 90, 102) };
            settings.FlatAppearance.BorderSize = 0;
            var settingsTip = new ToolTip(); settingsTip.SetToolTip(settings, "设置");
            settings.Disposed += delegate { settingsTip.Dispose(); };
            right.Controls.Add(trigger); right.Controls.Add(settings); Controls.Add(right);
            popup = new TodoWindow(store, RefreshCount);
            trigger.Click += delegate { if (popup.Visible) popup.Hide(); else OpenTodos(); };
            var menu = new ContextMenuStrip();
            var startup = new ToolStripMenuItem("开机启动") { CheckOnClick = false };
            startup.Checked = IsStartup();
            startup.Click += delegate
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (IsStartup()) key.DeleteValue("Liubai", false);
                        else key.SetValue("Liubai", "\"" + Application.ExecutablePath + "\" --startup");
                    }
                    startup.Checked = IsStartup();
                }
                catch (Exception ex) { MessageBox.Show("无法修改开机启动：" + ex.Message, "留白"); }
            };
            menu.Items.Add("打开待办", null, delegate { OpenTodos(); });
            menu.Items.Add(startup);
            menu.Items.Add("打开数据文件夹", null, delegate { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.Path)); System.Diagnostics.Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(store.Path)); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出留白", null, delegate { Close(); });
            ContextMenuStrip = menu; brand.Click += delegate { menu.Show(brand, new Point(0, brand.Height)); };
            settings.Click += delegate
            {
                popup.Hide();
                menu.Show(settings, new Point(settings.Width, settings.Height), ToolStripDropDownDirection.BelowLeft);
            };
            RefreshCount();
            edgeTimer.Tick += delegate
            {
                var bounds = Screen.PrimaryScreen.Bounds;
                var cursor = Cursor.Position;
                bool atEdge = cursor.Y == bounds.Top && cursor.X >= bounds.Left && cursor.X < bounds.Right;
                bool interacting = popup.Visible || popup.IsEditing || ContextMenuStrip.Visible;
                bool show = autoHide.Update(clock.ElapsedMilliseconds, atEdge, interacting);
                if (show && !Visible) { PositionBar(); Show(); }
                else if (!show && Visible) Hide();
            };
            Shown += delegate
            {
                Hide(); edgeTimer.Start();
                if (store.Warning != null) MessageBox.Show(store.Warning, "留白");
            };
        }
        static bool IsStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue("Liubai") != null;
            }
            catch { return false; }
        }
        void RefreshCount()
        {
            trigger.PendingCount = store.Data.Items.Count(x => !x.Done);
            trigger.AccessibleName = "待办，" + trigger.PendingCount + " 项未完成";
            trigger.Invalidate();
        }
        void OpenTodos()
        {
            autoHide.Reveal(clock.ElapsedMilliseconds);
            if (!Visible) { PositionBar(); Show(); }
            var area = Screen.FromHandle(Handle).WorkingArea;
            float scale; using (var g = CreateGraphics()) scale = g.DpiY / 96;
            popup.Height = Math.Min((int)(570 * scale), area.Height - 24);
            popup.Location = new Point(Right - popup.Width - 12, Bottom + 8);
            popup.Show(this); popup.Activate(); popup.FocusInput();
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PositionBar();
        }
        void PositionBar()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            int height; using (var g = CreateGraphics()) height = (int)Math.Round(34 * g.DpiY / 96);
            Bounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, height);
            if (popup.Visible) popup.Location = new Point(Right - popup.Width - 12, Bottom + 8);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x007E) PositionBar();
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            edgeTimer.Stop(); edgeTimer.Dispose();
            popup.Dispose(); base.OnFormClosed(e);
        }
    }
    public class TodoWindow : Form
    {
        readonly Store store; readonly Action changed;
        readonly TextBox input; readonly FlowLayoutPanel list;
        readonly Label status; readonly Button completed;
        bool showCompleted;
        public bool IsEditing { get; private set; }
        readonly Color ink = Color.FromArgb(42, 49, 61);
        public TodoWindow(Store store, Action changed)
        {
            this.store = store; this.changed = changed;
            Text = "留白待办"; Width = 370; Height = 530; FormBorderStyle = FormBorderStyle.None;
            ControlBox = false; ShowInTaskbar = Program.Inspect; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F); ForeColor = ink;
            AutoScaleMode = AutoScaleMode.None;
            var header = new Panel { Dock = DockStyle.Top, Height = 94, Width = ClientSize.Width };
            header.Controls.Add(new Label { Text = "接着，把事情做好。", AutoSize = true, Location = new Point(22, 18), Font = new Font(Font.FontFamily, 14F, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = "写下要做的事，下次回来继续。", AutoSize = true, Location = new Point(23, 54), ForeColor = Color.Gray });
            var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, Size = new Size(30, 30), Location = new Point(Width - 48, 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.FlatAppearance.BorderSize = 0; close.Click += delegate { Hide(); }; header.Controls.Add(close);
            var entry = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(22, 5, 22, 8) };
            input = new TextBox { Dock = DockStyle.Top, Font = new Font(Font.FontFamily, 11F), MaxLength = 500 };
            var hint = new Label { Text = "输入待办，按 Enter 添加", Dock = DockStyle.Bottom, ForeColor = Color.Gray, Height = 20 };
            entry.Controls.Add(input); entry.Controls.Add(hint);
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string text = input.Text.Trim(); if (text.Length == 0) return;
                var item = new Todo { Text = text }; store.Data.Items.Add(item);
                if (Persist()) { input.Clear(); Render(); } else store.Data.Items.Remove(item);
            };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(18, 0, 18, 8) };
            completed = new Button { Dock = DockStyle.Top, Height = 30, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft };
            completed.FlatAppearance.BorderSize = 0;
            completed.Click += delegate { showCompleted = !showCompleted; Render(); };
            status = new Label { Dock = DockStyle.Bottom, Height = 21, ForeColor = Color.Gray, Text = "自动保存在此电脑" };
            footer.Controls.Add(completed); footer.Controls.Add(status);
            list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18, 6, 12, 6) };
            Controls.Add(list); Controls.Add(entry); Controls.Add(header); Controls.Add(footer);
            Deactivate += delegate { Hide(); };
            Render();
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96F, graphics.DpiY / 96F));
            Shown += delegate { Render(); };
        }
        public void FocusInput() { input.Focus(); }
        bool Persist()
        {
            try { store.Save(); status.Text = "已保存到此电脑"; changed(); return true; }
            catch (Exception ex) { MessageBox.Show(this, "保存失败，请检查磁盘空间或文件权限。\n" + ex.Message, "留白"); return false; }
        }
        void Render()
        {
            float scale = IsHandleCreated ? DeviceScale() : 1F;
            list.SuspendLayout();
            foreach (Control control in list.Controls.Cast<Control>().ToArray()) control.Dispose();
            int count = store.Data.Items.Count(x => !x.Done);
            completed.Text = (showCompleted ? "收起" : "展开") + "已完成  ·  " + store.Data.Items.Count(x => x.Done);
            var visible = store.Data.Items.Where(x => !x.Done || showCompleted).OrderBy(x => x.Done).ToList();
            if (count == 0)
            {
                var empty = new Label { Text = store.Data.Items.Count == 0 ? "还没有待办\n从一件小事开始吧。" : "待办都完成了。\n留一点时间给自己。", Width = 300, Height = 76, Padding = new Padding(7, 18, 0, 0), ForeColor = Color.Gray };
                empty.Scale(new SizeF(scale, scale)); list.Controls.Add(empty);
            }
            foreach (var item in visible)
            {
                var row = new Panel { Width = (int)(ClientSize.Width / scale) - 54, Height = 62, Margin = new Padding(0, 0, 0, 8), BackColor = Color.FromArgb(247, 248, 250) };
                var check = new CheckBox { Checked = item.Done, Location = new Point(12, 20), Size = new Size(20, 22) };
                var label = new Label { Text = item.Text, Location = new Point(40, 10), Size = new Size(row.Width - 75, 43), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = item.Done ? Color.Gray : ink, Cursor = Cursors.Hand };
                if (item.Done) label.Font = new Font(Font, FontStyle.Strikeout);
                var tips = new ToolTip(); tips.SetToolTip(label, item.Text + "\n双击编辑"); row.Disposed += delegate { tips.Dispose(); };
                check.CheckedChanged += delegate
                {
                    item.Done = check.Checked;
                    if (Persist()) BeginInvoke((Action)Render);
                    else { item.Done = !check.Checked; BeginInvoke((Action)Render); }
                };
                label.DoubleClick += delegate { Edit(item); };
                var menu = new ContextMenuStrip();
                menu.Items.Add("编辑", null, delegate { Edit(item); });
                menu.Items.Add("删除", null, delegate
                {
                    int index = store.Data.Items.IndexOf(item); store.Data.Items.Remove(item);
                    if (!Persist()) store.Data.Items.Insert(index, item);
                    Render();
                });
                row.ContextMenuStrip = menu; label.ContextMenuStrip = menu;
                row.Disposed += delegate { menu.Dispose(); };
                var more = new Button { Text = "⋯", FlatStyle = FlatStyle.Flat, Size = new Size(26, 28), Location = new Point(row.Width - 29, 17) };
                more.FlatAppearance.BorderSize = 0; more.Click += delegate { menu.Show(more, new Point(0, more.Height)); };
                row.Controls.Add(check); row.Controls.Add(label); row.Controls.Add(more); row.Scale(new SizeF(scale, scale)); list.Controls.Add(row);
            }
            list.ResumeLayout();
        }
        float DeviceScale() { using (var g = CreateGraphics()) return g.DpiY / 96F; }
        void Edit(Todo item)
        {
            IsEditing = true;
            try
            {
            // Keep this window visible while its owned edit dialog has focus.
            using (var dialog = new Form { Text = "编辑待办", Width = 380, Height = 170, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = Font, TopMost = true })
            {
                dialog.AutoScaleMode = AutoScaleMode.None;
                var text = new TextBox { Text = item.Text, Location = new Point(18, 20), Width = 325, MaxLength = 500 };
                var save = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(255, 68), Width = 88 };
                dialog.Controls.Add(text); dialog.Controls.Add(save); dialog.AcceptButton = save;
                dialog.Scale(new SizeF(DeviceScale(), DeviceScale()));
                if (dialog.ShowDialog() == DialogResult.OK && text.Text.Trim().Length > 0)
                {
                    string old = item.Text; item.Text = text.Text.Trim(); if (!Persist()) item.Text = old;
                }
            }
            Render(); Show(); Activate();
            }
            finally { IsEditing = false; }
        }
    }
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    }
}
