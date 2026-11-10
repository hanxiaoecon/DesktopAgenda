using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopAgenda
{
    internal static class Startup
    {
        private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string Name = "DesktopAgenda";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }
        public static bool Enabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && String.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase); }
        }
        public static void Apply(bool enabled)
        {
            if (Program.TestBuild || Program.PortableBuild) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            { if (enabled) key.SetValue(Name, Command, RegistryValueKind.String); else key.DeleteValue(Name, false); }
        }
    }

    internal sealed class MainWindow : Form
    {
        private AgendaData data;
        private readonly Store store;
        private readonly int revealMessage;
        private readonly DateTimePicker datePicker;
        private readonly DayTimeline timeline;
        private readonly TodoGrid[] todoLists = new TodoGrid[4];
        private readonly GroupBox[] quadrants = new GroupBox[4];
        private readonly StatusBar status;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer ticker;
        private readonly MenuItem pinMenu, soundMenu, startupMenu;
        private readonly Dictionary<string, ReminderDialog> reminders = new Dictionary<string, ReminderDialog>();
        private bool refreshing, selecting, quitting;
        private bool hotKeyRegistered;
        private string selectedTodo;
        private string selectedSchedule;
        private string lastMinute;
        private string startupError;
        private DateTime selectedDay = DateTime.Today;
        private DateTime viewedToday = DateTime.Today;
        private bool followToday = true;
        private DateTime nextViewChange = DateTime.MinValue;
        private readonly ToolTip hints = new ToolTip();

        public MainWindow(Store disk, AgendaData initial, int message)
        {
            store = disk; data = initial; revealMessage = message;
            Text = Program.Caption; Font = Ui.Normal; BackColor = Ui.Face; AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = true; MaximizeBox = true; Icon = Ui.AppIcon;
            MinimumSize = new Size(Ui.S(850), Ui.S(590));
            ClientSize = data.Width > 0 ? new Size(Math.Max(Ui.S(850), Math.Min(data.Width, Ui.S(1450))), Math.Max(Ui.S(550), Math.Min(data.Height, Ui.S(1100)))) : new Size(Ui.S(1000), Ui.S(660));
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            if (Height > area.Height) Height = area.Height - Ui.S(12);
            if (Width > area.Width) Width = area.Width - Ui.S(12);
            Location = data.X == -99999 ? new Point(area.Right - Width - Ui.S(16), area.Top + Ui.S(36)) : new Point(data.X, data.Y);
            if (!Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, Bounds).Width >= Ui.S(100) && Rectangle.Intersect(s.WorkingArea, Bounds).Height >= Ui.S(70))) Location = new Point(area.Left + Ui.S(25), area.Top + Ui.S(25));
            TopMost = data.AlwaysOnTop;

            MenuItem file = new MenuItem("文件(&F)");
            file.MenuItems.Add(new MenuItem("新建日程(&S)", delegate { EditSchedule(null, null); }, Shortcut.CtrlN));
            file.MenuItems.Add(new MenuItem("新建待办(&T)", delegate { EditTodo(null, 0); }, Shortcut.CtrlShiftN));
            file.MenuItems.Add("-"); file.MenuItems.Add("导出备份(&E)…", delegate { Export(); }); file.MenuItems.Add("恢复备份(&R)…", delegate { Import(); });
            file.MenuItems.Add("打开数据文件夹", delegate { Process.Start("explorer.exe", "\"" + store.DirectoryPath + "\""); });
            file.MenuItems.Add("-"); file.MenuItems.Add("收起到托盘", delegate { HideToTray(); });
            MenuItem options = new MenuItem("选项(&O)");
            pinMenu = new MenuItem("窗口置顶", delegate { if (Commit(delegate { data.AlwaysOnTop = !data.AlwaysOnTop; })) TopMost = data.AlwaysOnTop; UpdateMenus(); });
            soundMenu = new MenuItem("声音提醒", delegate { Commit(delegate { data.Sound = !data.Sound; }); UpdateMenus(); });
            options.MenuItems.AddRange(new[] { pinMenu, soundMenu });
            if (!Program.PortableBuild) startupMenu = options.MenuItems.Add("开机自动启动", delegate { ToggleStartup(); });
            options.Popup += delegate { UpdateMenus(); };
            MenuItem help = new MenuItem("帮助(&H)"); help.MenuItems.Add("测试声音与提醒", delegate { PreviewReminder(); }); help.MenuItems.Add("使用说明", delegate { Help(); });
            Menu = new MainMenu(new[] { file, options, help });

            status = new StatusBar { ShowPanels = false, Font = Ui.Normal, Dock = DockStyle.Bottom, Height = Ui.S(24), SizingGrip = true }; Controls.Add(status);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(6)), ColumnCount = 2, RowCount = 1, BackColor = Ui.Face };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root); root.BringToFront();
            GroupBox scheduleGroup = new GroupBox { Text = "日程", Font = Ui.Bold, Dock = DockStyle.Fill, Padding = new Padding(Ui.S(8)), Margin = new Padding(Ui.S(3)), BackColor = Ui.Face }; root.Controls.Add(scheduleGroup, 0, 0);
            TableLayoutPanel scheduleTable = Table(1, 3); scheduleTable.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(40))); scheduleTable.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(37))); scheduleTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); scheduleGroup.Controls.Add(scheduleTable);
            TableLayoutPanel dates = Table(4, 1); dates.ColumnStyles.Clear(); dates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(36))); dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); dates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(36))); dates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(60))); scheduleTable.Controls.Add(dates, 0, 0);
            Button previous = Ui.Button("<", delegate { SetSelectedDay(selectedDay.AddDays(-1)); }); previous.Width = Ui.S(30); dates.Controls.Add(previous, 0, 0);
            datePicker = new DateTimePicker { AccessibleName = "时间轴日期", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd dddd", Value = selectedDay, Font = Ui.Normal, Dock = DockStyle.Fill, Margin = new Padding(Ui.S(3), Ui.S(4), Ui.S(3), 0) };
            datePicker.ValueChanged += delegate { selectedDay = datePicker.Value.Date; followToday = selectedDay == DateTime.Today; RenderSchedules(); RenderTodos(); }; dates.Controls.Add(datePicker, 1, 0);
            Button following = Ui.Button(">", delegate { SetSelectedDay(selectedDay.AddDays(1)); }); following.Width = Ui.S(30); dates.Controls.Add(following, 2, 0);
            Button today = Ui.Button("今天", delegate { SetSelectedDay(DateTime.Today); timeline.LocateNow(); }); today.Width = Ui.S(54); dates.Controls.Add(today, 3, 0);
            FlowLayoutPanel timelineTools = Flow(); Button newSchedule = Ui.Button("新增日程", delegate { EditSchedule(null, null); }); newSchedule.Width = Ui.S(96); timelineTools.Controls.Add(newSchedule);
            Button now = Ui.Button("定位现在", delegate { SetSelectedDay(DateTime.Today); timeline.LocateNow(); }); now.Width = Ui.S(86); timelineTools.Controls.Add(now);
            Label axisHint = new Label { Text = "滚轮查看全天", AutoSize = true, Font = Ui.Normal, Margin = new Padding(Ui.S(9), Ui.S(9), 0, 0) }; timelineTools.Controls.Add(axisHint); scheduleTable.Controls.Add(timelineTools, 0, 1);
            Panel timelineFrame = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(0) }; timeline = new DayTimeline(); timelineFrame.Controls.Add(timeline); scheduleTable.Controls.Add(timelineFrame, 0, 2);
            timeline.SelectionChanged += delegate(string id) { selectedSchedule = id; };
            timeline.EditRequested += delegate(string id) { selectedSchedule = id; EditSelectedSchedule(); };
            timeline.NewRequested += delegate(DateTime start) { EditSchedule(null, null, start); };
            timeline.DeleteRequested += delegate(string id) { selectedSchedule = id; DeleteSelectedSchedule(); };
            ContextMenu scheduleContext = new ContextMenu();
            MenuItem editSchedule = new MenuItem("编辑日程", delegate { EditSelectedSchedule(); });
            MenuItem linkedTodo = new MenuItem("更改关联待办…", delegate { Schedule item = CurrentSchedule(); if (item != null) EditSchedule(item, null, null, false); });
            MenuItem completeSchedule = new MenuItem("标记完成", delegate { ToggleSelectedSchedule(); });
            MenuItem deleteSchedule = new MenuItem("删除日程", delegate { DeleteSelectedSchedule(); });
            scheduleContext.MenuItems.AddRange(new[] { editSchedule, linkedTodo, completeSchedule, deleteSchedule, new MenuItem("-"), new MenuItem("在此时间新增日程", delegate { EditSchedule(null, null, timeline.ClickedTime); }) });
            scheduleContext.Popup += delegate { selectedSchedule = timeline.SelectedId; Schedule item = CurrentSchedule(); editSchedule.Enabled = deleteSchedule.Enabled = completeSchedule.Enabled = item != null; linkedTodo.Enabled = item != null && !String.IsNullOrEmpty(item.TodoId); editSchedule.Text = linkedTodo.Enabled ? "编辑日程和待办…" : "编辑日程…"; completeSchedule.Text = item != null && Rules.ScheduleComplete(data, item) ? "恢复未完成" : "标记完成"; if (linkedTodo.Enabled) completeSchedule.Text += "（同步关联待办）"; }; timeline.ContextMenu = scheduleContext;

            GroupBox todoGroup = new GroupBox { Text = "待办", Font = Ui.Bold, Dock = DockStyle.Fill, Padding = new Padding(Ui.S(8)), Margin = new Padding(Ui.S(3)), BackColor = Ui.Face }; root.Controls.Add(todoGroup, 1, 0);
            TableLayoutPanel todoTable = Table(1, 3); todoTable.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(40))); todoTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); todoTable.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(29))); todoGroup.Controls.Add(todoTable);
            FlowLayoutPanel todoButtons = Flow(); todoButtons.Controls.Add(Ui.Button("新增待办", delegate { EditTodo(null, 0); }));
            Button manage = Ui.Button("批量管理待办", delegate { ManageTodos(); }); manage.Width = Ui.S(125); todoButtons.Controls.Add(manage); todoTable.Controls.Add(todoButtons, 0, 0);
            TableLayoutPanel four = Table(2, 2); four.ColumnStyles.Clear(); four.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); four.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); four.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); four.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); todoTable.Controls.Add(four, 0, 1);
            for (int index = 0; index < 4; index++)
            {
                GroupBox group = new GroupBox { Text = Rules.Priorities[index], Font = Ui.Normal, Dock = DockStyle.Fill, Padding = new Padding(Ui.S(5)), Margin = new Padding(Ui.S(3)) }; quadrants[index] = group; four.Controls.Add(group, index % 2, index / 2);
                TodoGrid list = new TodoGrid { AccessibleName = Rules.Priorities[index] + "待办列表" }; todoLists[index] = list; group.Controls.Add(list);
                list.ToggleRequested += delegate(string id) { BeginInvoke((MethodInvoker)delegate { if (IsDisposed) return; selectedTodo = id; ToggleSelectedTodo(); }); };
                list.EditRequested += delegate(string id) { selectedTodo = id; EditSelectedTodo(); };
                list.SelectionChanged += delegate
                {
                    if (refreshing || selecting || list.SelectedTodoId == null) return; selectedTodo = list.SelectedTodoId; selecting = true;
                    try { foreach (TodoGrid other in todoLists) if (other != null && other != list) { other.ClearSelection(); other.CurrentCell = null; } } finally { selecting = false; }
                };
                ContextMenu context = new ContextMenu(); MenuItem edit = new MenuItem("编辑待办", delegate { EditSelectedTodo(); }); MenuItem arrange = new MenuItem("安排/编辑日程", delegate { ScheduleSelectedTodo(); });
                MenuItem removeSchedule = new MenuItem("删除关联日程", delegate { Todo task = CurrentTodo(); Schedule linked = task == null ? null : Rules.ScheduleFor(data, task.Id); if (linked != null) { selectedSchedule = linked.Id; DeleteSelectedSchedule(); } });
                MenuItem changePriority = new MenuItem("更改优先级");
                for (int p = 0; p < Rules.Priorities.Length; p++) { int priority = p; changePriority.MenuItems.Add(Rules.Priorities[p], delegate { Todo task = CurrentTodo(); if (task != null) { Commit(delegate { task.Priority = priority; }); RefreshAll(); } }); }
                MenuItem removeTodo = new MenuItem("删除待办", delegate { DeleteSelectedTodo(); }); context.MenuItems.AddRange(new[] { edit, arrange, removeSchedule, changePriority, new MenuItem("-"), removeTodo });
                context.Popup += delegate { selectedTodo = list.SelectedTodoId; Todo task = CurrentTodo(); edit.Enabled = arrange.Enabled = changePriority.Enabled = removeTodo.Enabled = task != null; bool linked = task != null && Rules.ScheduleFor(data, task.Id) != null; removeSchedule.Enabled = linked; edit.Text = linked ? "编辑待办和日程…" : "编辑待办…"; arrange.Text = linked ? "更改关联日程…" : "安排日程…"; }; list.ContextMenu = context;
            }
            Label todoHint = new Label { Text = "红字表示逾期；完成保留24小时，隐藏后可回看日期或批量管理。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Normal, AutoEllipsis = true }; hints.SetToolTip(todoHint, "勾选完成，再点恢复。完成后划线保留24小时，任务时间已过后自动隐藏；切回任务日期或完成日期可查看，批量管理也始终保留全部待办。"); todoTable.Controls.Add(todoHint, 0, 2);

            
            tray = new NotifyIcon { Icon = Icon, Text = Program.Caption, Visible = true };
            ContextMenu trayMenu = new ContextMenu(); MenuItem showWindow = trayMenu.MenuItems.Add("显示窗口", delegate { Reveal(); }); trayMenu.MenuItems.Add("新建日程", delegate { EditSchedule(null, null); }); trayMenu.MenuItems.Add("新建待办", delegate { EditTodo(null, 0); }); trayMenu.MenuItems.Add("-"); trayMenu.MenuItems.Add("完全退出", delegate { Quit(); }); tray.ContextMenu = trayMenu; tray.DoubleClick += delegate { Reveal(); }; tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Reveal(); };
            ticker = new System.Windows.Forms.Timer { Interval = 1000 }; ticker.Tick += delegate { Tick(); };
            FormClosing += ClosingWindow;
            FormClosed += delegate { ticker.Stop(); ticker.Dispose(); tray.Visible = false; tray.Dispose(); hints.Dispose(); Native.UnregisterHotKey(Handle, 1001); };
            Shown += delegate
            {
                hotKeyRegistered = Native.RegisterHotKey(Handle, 1001, 0x4000 | 0x0002 | 0x0001, (uint)Keys.D);
                if (hotKeyRegistered) showWindow.Text += "  Ctrl+Alt+D";
                RefreshAll();
                try { if (!Program.TestBuild && !Program.PortableBuild) { Startup.Apply(data.RunAtStartup); store.Save(data); } }
                catch (Exception ex) { startupError = ex.Message; MessageBox.Show(this, "日程与待办可以使用，但开机启动未能设置成功：\n" + ex.Message, "开机启动", MessageBoxButtons.OK, MessageBoxIcon.Exclamation); }
                UpdateMenus(); ticker.Start();
                if (!String.IsNullOrEmpty(store.Notice)) MessageBox.Show(this, store.Notice, "数据已保留", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }
        private static TableLayoutPanel Table(int cols, int rows)
        {
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, Margin = new Padding(0), Padding = new Padding(0), BackColor = Ui.Face };
            if (cols == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
        }
        private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0), Font = Ui.Normal }; }
        private void SetSelectedDay(DateTime day)
        {
            if (day.Date < datePicker.MinDate.Date || day.Date > datePicker.MaxDate.Date) return;
            selectedDay = day.Date; followToday = selectedDay == DateTime.Today; if (datePicker.Value.Date != selectedDay) datePicker.Value = selectedDay; else { RenderSchedules(); RenderTodos(); }
        }
        private void RefreshAll()
        {
            foreach (KeyValuePair<string, ReminderDialog> reminder in reminders.ToArray())
            {
                if (reminder.Key == "preview") continue;
                Schedule item = data.Schedules.FirstOrDefault(s => s.Id == reminder.Key);
                if (item == null || Rules.ScheduleComplete(data, item)) reminder.Value.CloseWithoutAction();
            }
            RenderSchedules(); RenderTodos(); UpdateMenus(); UpdateStatus();
        }
        private void RenderSchedules()
        {
            if (timeline == null) return; timeline.SetData(data, selectedDay, selectedSchedule); selectedSchedule = timeline.SelectedId; UpdateStatus();
        }
        private void RenderTodos()
        {
            if (todoLists[3] == null) return; refreshing = true;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    DateTime now = DateTime.Now;
                    List<Todo> tasks = Rules.SortTodos(data, data.Todos.Where(t => t.Priority == i && Rules.TodoVisible(data, t, selectedDay, now)), now);
                    quadrants[i].Text = Rules.Priorities[i] + "  (" + tasks.Count(t => !t.Done) + "/" + tasks.Count + ")";
                    todoLists[i].FillRows(tasks, data, selectedTodo, now);
                }
            }
            finally { refreshing = false; }
            nextViewChange = Rules.NextViewChange(data, DateTime.Now);
            UpdateStatus();
        }
        private void ManageTodos()
        {
            Reveal(); ticker.Stop();
            try
            {
                using (BatchTodoEditor editor = new BatchTodoEditor(data))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    Commit(delegate { data.Todos = editor.Result.Todos; data.Schedules = editor.Result.Schedules; }); RefreshAll();
                }
            }
            finally { ticker.Start(); }
        }
        
        private void UpdateMenus()
        {
            pinMenu.Checked = data.AlwaysOnTop; soundMenu.Checked = data.Sound;
            if (startupMenu != null)
            {
                try { startupMenu.Checked = Program.TestBuild ? data.RunAtStartup : Startup.Enabled; } catch { startupMenu.Checked = false; }
            }
        }
        private void UpdateStatus()
        {
            if (status == null) return;
            int due = data.Schedules.Count(s => Rules.IsDue(data, s, DateTime.Now));
            string startup = Program.PortableBuild ? "便携版" : startupError != null ? "开机启动设置失败" : data.RunAtStartup ? "开机启动开" : "开机启动关";
            status.Text = "日程 " + data.Schedules.Count + " 项  |  待办 " + data.Todos.Count(t => !t.Done) + " 项 / 已完成 " + data.Todos.Count(t => t.Done) + " 项  |  声音" + (data.Sound ? "开" : "关") + "  |  " + startup + (due > 0 ? "  |  " + due + " 项待提醒" : "") + (hotKeyRegistered ? "  |  Ctrl+Alt+D 找回" : "");
        }
        private bool Commit(Action change)
        {
            string original = Json.Write(data);
            try { change(); store.Save(data); UpdateStatus(); return true; }
            catch (Exception ex) { data = Json.Read<AgendaData>(original); Ui.Error(this, "修改未保存，原数据仍保留。\n\n" + ex.Message); return false; }
        }
        private void EditPair(Schedule item, Todo task, bool focusTodo)
        {
            Reveal(); ticker.Stop();
            try
            {
                using (PairEditor editor = new PairEditor(data, item, task, focusTodo))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    if (Commit(delegate { data = Rules.PreparePairEdit(data, editor.EditedSchedule, editor.EditedTodo); }))
                    { selectedSchedule = item.Id; selectedTodo = task.Id; }
                    RefreshAll();
                }
            }
            finally { ticker.Start(); }
        }
        private void EditSchedule(Schedule item, string todoId, DateTime? suggestedStart = null, bool editTogether = true)
        {
            if (editTogether && item != null && !String.IsNullOrEmpty(item.TodoId))
            {
                Todo task = data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
                if (task != null) { EditPair(item, task, false); return; }
            }
            Reveal(); ticker.Stop();
            try
            {
                using (ScheduleEditor editor = new ScheduleEditor(data, item, selectedDay, todoId, suggestedStart))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    if (Commit(delegate { Rules.SaveSchedule(data, editor.Result, editor.CreatedTodo); })) { selectedSchedule = editor.Result.Id; if (editor.CreatedTodo != null) selectedTodo = editor.CreatedTodo.Id; }
                    RefreshAll();
                }
            }
            finally { ticker.Start(); }
        }
        private void EditSelectedSchedule() { Schedule item = CurrentSchedule(); if (item != null) EditSchedule(item, null); }
        private Schedule CurrentSchedule() { return data.Schedules.FirstOrDefault(s => s.Id == selectedSchedule); }
        private Todo CurrentTodo() { return data.Todos.FirstOrDefault(t => t.Id == selectedTodo); }
        private void EditTodo(Todo item, int priority, bool editTogether = true)
        {
            if (editTogether && item != null)
            {
                Schedule linked = Rules.ScheduleFor(data, item.Id);
                if (linked != null) { EditPair(linked, item, true); return; }
            }
            Reveal(); ticker.Stop();
            try
            {
                using (TodoEditor editor = new TodoEditor(data, item, priority, selectedDay))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    if (Commit(delegate { Rules.SaveTodo(data, editor.Result, editor.ScheduleId, editor.CreatedSchedule); })) { selectedTodo = editor.Result.Id; if (editor.CreatedSchedule != null) selectedSchedule = editor.CreatedSchedule.Id; }
                    RefreshAll();
                }
            }
            finally { ticker.Start(); }
        }
        private void EditSelectedTodo() { Todo item = CurrentTodo(); if (item != null) EditTodo(item, item.Priority); }
        private void ScheduleSelectedTodo()
        {
            Todo todo = CurrentTodo(); if (todo == null) { MessageBox.Show(this, "先选中一条待办事项。", "安排日程", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Rules.ScheduleFor(data, todo.Id) == null) EditSchedule(null, todo.Id);
            else EditTodo(todo, todo.Priority, false);
        }
        private void ToggleSelectedTodo() { Todo todo = CurrentTodo(); if (todo != null) { Commit(delegate { Rules.CompleteTodo(data, todo.Id, !todo.Done, DateTime.Now); }); RefreshAll(); } }
        private void ToggleSelectedSchedule() { Schedule item = CurrentSchedule(); if (item != null) { Commit(delegate { Rules.CompleteSchedule(data, item.Id, !Rules.ScheduleComplete(data, item), DateTime.Now); }); RefreshAll(); } }
        private void DeleteSelectedTodo()
        {
            Todo todo = CurrentTodo(); if (todo == null) return;
            if (!Ui.Confirm(this, "删除待办“" + todo.Title + "”？\n\n关联日程会保留，只解除关联。\n如果只是做完了，请勾选完成，便于以后恢复。", "删除待办")) return;
            Commit(delegate { Rules.DeleteTodo(data, todo.Id); }); selectedTodo = null; RefreshAll();
        }
        private void DeleteSelectedSchedule()
        {
            Schedule schedule = CurrentSchedule(); if (schedule == null) return;
            if (!Ui.Confirm(this, "删除日程“" + schedule.Title + "”？\n" + Rules.Range(schedule, true) + "\n\n关联待办会保留，只解除关联。", "删除日程")) return;
            Commit(delegate { Rules.DeleteSchedule(data, schedule.Id); }); selectedSchedule = null; RefreshAll();
        }
        private void Tick()
        {
            if (quitting || IsDisposed) return;
            timeline.RefreshClock();
            DateTime now = DateTime.Now;
            if (viewedToday != now.Date) { viewedToday = now.Date; if (followToday) SetSelectedDay(now.Date); }
            string minute = now.ToString("yyyyMMddHHmm"); if (minute != lastMinute || now >= nextViewChange) { lastMinute = minute; RenderSchedules(); RenderTodos(); UpdateStatus(); }
            Schedule next = data.Schedules.Where(s => !reminders.ContainsKey(s.Id) && Rules.IsDue(data, s, now)).OrderBy(s => s.StartTime).FirstOrDefault();
            if (next == null) return;
            if (Commit(delegate { next.LastAlert = Clock.Format(DateTime.Now); next.SnoozeUntil = null; })) ShowReminder(next, false);
        }
        private void ShowReminder(Schedule item, bool preview)
        {
            string key = preview ? "preview" : item.Id;
            if (reminders.ContainsKey(key)) { reminders[key].Activate(); return; }
            Todo task = data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
            ReminderDialog dialog = new ReminderDialog(item, task, preview, reminders.Count % 6, delegate { return data.Sound; }, delegate(string kind, int minutes)
            {
                if (kind == "preview") return;
                Commit(delegate
                {
                    Schedule current = data.Schedules.FirstOrDefault(s => s.Id == item.Id); if (current == null) return;
                    if (kind == "snooze") { current.SnoozeUntil = Clock.Format(DateTime.Now.AddMinutes(minutes)); current.AlertsStopped = false; }
                    else { current.AlertsStopped = true; current.SnoozeUntil = null; if (kind == "done") Rules.CompleteSchedule(data, current.Id, true, DateTime.Now); }
                }); RefreshAll();
            });
            reminders.Add(key, dialog); dialog.Icon = Icon; dialog.FormClosed += delegate { reminders.Remove(key); }; dialog.Show(); dialog.Activate();
            Native.Flash(dialog.Handle);
        }
        private void PreviewReminder() { ShowReminder(new Schedule { Title = "声音与日程提醒测试", Start = Clock.Format(DateTime.Now.AddMinutes(10)), End = Clock.Format(DateTime.Now.AddMinutes(40)), Notes = "这是测试，不会新增或修改日程。" }, true); }
        private void ToggleStartup()
        {
            bool target = !data.RunAtStartup;
            try { Startup.Apply(target); if (Commit(delegate { data.RunAtStartup = target; })) startupError = null; else Startup.Apply(!target); }
            catch (Exception ex) { startupError = ex.Message; Ui.Error(this, "开机启动设置失败：\n" + ex.Message); }
            UpdateMenus(); UpdateStatus();
        }
        private void Export()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "日程与待办备份 (*.json)|*.json", FileName = "日程与待办备份-" + DateTime.Now.ToString("yyyyMMdd") + ".json", AddExtension = true, DefaultExt = "json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, Json.Write(data), new UTF8Encoding(false)); MessageBox.Show(this, "备份已保存。", "导出备份", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { Ui.Error(this, ex.Message); }
            }
        }
        private void Import()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "日程与待办备份 (*.json)|*.json", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    AgendaData imported = store.ReadBackup(dialog.FileName);
                    if (!Ui.Confirm(this, "恢复备份将替换当前日程和待办。\n备份包含 " + imported.Schedules.Count + " 项日程、" + imported.Todos.Count + " 项待办。\n\n当前数据会先保存到独立备份文件。继续？", "恢复备份")) return;
                    File.WriteAllText(Path.Combine(store.DirectoryPath, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), Json.Write(data), new UTF8Encoding(false));
                    Commit(delegate { data.Schedules = imported.Schedules; data.Todos = imported.Todos; }); RefreshAll();
                }
                catch (Exception ex) { Ui.Error(this, ex.Message); }
            }
        }
        private void Help()
        {
            string storageHelp = Program.PortableBuild
                ? "数据保存在程序旁的 PortableData 文件夹。换电脑时，先完全退出，再把程序和 PortableData 文件夹一起复制过去。"
                : "只允许一个主程序运行；重复启动会唤回已有窗口，自动读取固定目录里的日程与待办。";
            string startupHelp = Program.PortableBuild
                ? "便携版需手动启动。声音提醒默认开启，可在“选项”中关闭。"
                : "声音提醒和开机自动启动默认开启，可在“选项”中关闭。";
            string revealHelp = hotKeyRegistered ? "Ctrl+Alt+D或再次启动找回原窗口。" : "点击托盘图标或再次启动找回原窗口。";
            MessageBox.Show(this, Program.Caption + " 2.4.4\n\n左侧：选择日期，滚动查看00:00–24:00的时间轴。今天有红色当前时间线，默认定位到当前时间前1小时；“今天”和“定位现在”可找回这一时段，手动滚动后保留位置。右键时间块可编辑、完成、恢复或删除，双击空白处新增日程；时间不可重叠。\n\n右侧：按四类优先级显示待办。每区按逾期未完成、未安排日程、其余未完成日程、已完成排序，同组日程按开始时间从早到晚。每行最右侧勾选完成，再点恢复，完成后划线保留24小时；任务时间已过后自动隐藏，切回对应日期或在批量管理中可找回。无日程待办只记录创建时间，不会变红；日程结束仍未完成时，日程和关联待办显示红字。右键待办可编辑、安排日程、更改优先级或删除。\n\n批量管理：包含已自动隐藏的全部待办。勾选后统一完成、恢复、更改优先级或删除，点击“保存更改”生效，取消则放弃改动。\n\n新建日程或待办时，可在关联下拉框选择“同时新建”配套事项，一次保存并关联。已关联的两项可双击任一项一起编辑，点击“保存两项”后生效。换绑或解除关联请用右键的“更改关联”。删除任一项会保留另一项。\n\n" + storageHelp + "\n\n" + startupHelp + "程序运行且电脑唤醒时才会提醒。\n\n最小化保留在任务栏；点X收起到托盘，提醒继续。" + revealHelp + "\nCtrl+N新建日程，Ctrl+Shift+N新建待办。完全退出请右键托盘图标 → 完全退出。\n\n本地保存，不连接外部日历。数据：\n" + store.FilePath, "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        public void Reveal()
        {
            if (IsDisposed) return; Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Form target = OwnedForms.LastOrDefault(f => f.Visible) ?? this; target.Activate(); Native.SetForegroundWindow(target.Handle);
        }
        private void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            Commit(delegate { data.X = Left; data.Y = Top; data.Width = ClientSize.Width; data.Height = ClientSize.Height; });
        }
        private void HideToTray() { SaveBounds(); Hide(); }
        private void Quit() { SaveBounds(); quitting = true; foreach (ReminderDialog dialog in reminders.Values.ToArray()) dialog.Close(); Close(); }
        private void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) { SaveBounds(); quitting = true; return; }
            if (!quitting) { e.Cancel = true; HideToTray(); }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == revealMessage || (m.Msg == 0x0312 && m.WParam.ToInt32() == 1001)) { Reveal(); return; } base.WndProc(ref m);
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
        [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { public uint size; public IntPtr hwnd; public uint flags; public uint count; public uint timeout; }
        public static void Flash(IntPtr hwnd) { FlashInfo info = new FlashInfo { size = (uint)Marshal.SizeOf(typeof(FlashInfo)), hwnd = hwnd, flags = 3 | 12, count = 5 }; FlashWindowEx(ref info); }
    }
    internal static class Program
    {
#if TESTING
        public static readonly bool TestBuild = true;
#else
        public static readonly bool TestBuild = false;
#endif
#if PORTABLE
        public static readonly bool PortableBuild = true;
#else
        public static readonly bool PortableBuild = false;
#endif
        public static string Caption { get { return PortableBuild ? "桌面日程与待办（便携版）" : "桌面日程与待办"; } }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--self-test") return Tests.Run(args[1]);
            Native.SetProcessDPIAware(); Application.SetCompatibleTextRenderingDefault(false);
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string directory = PortableBuild ? DataLocation.ResolvePortable(appDirectory) :
                DataLocation.Resolve(TestBuild, appDirectory, args.Length >= 2 && args[0] == "--data-dir" ? args[1] : null);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                try { Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "error.log"), DateTime.Now.ToString("o") + "\r\n" + e.Exception + "\r\n", Encoding.UTF8); } catch { }
                MessageBox.Show("本次操作遇到错误，请关闭提示后重试。已保存的数据仍保留。\n\n" + e.Exception.Message, "桌面日程与待办", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try
            {
                string token = PortableBuild ? DataLocation.PortableInstanceToken(directory) :
                    (TestBuild ? "DesktopAgenda.Test.Primary" : "DesktopAgenda.Primary");
                int message = Native.RegisterWindowMessage(token + "-Show");
                using (InstanceLock instance = new InstanceLock(token))
                {
                    if (!instance.IsPrimary) { Native.AllowSetForegroundWindow(-1); Native.PostMessage(new IntPtr(0xffff), message, IntPtr.Zero, IntPtr.Zero); return 0; }
                    if (!TestBuild && !PortableBuild) DataLocation.CopyLegacyIfMissing(directory, Path.Combine(appDirectory, "data"));
                    Store store = new Store(directory); AgendaData data = store.Load();
                    if (PortableBuild)
                    {
                        data.RunAtStartup = false;
                        if (!File.Exists(store.FilePath)) store.Save(data);
                    }
                    using (MainWindow window = new MainWindow(store, data, message))
                    {
                        window.Shown += delegate { instance.Listen(window, window.Reveal); }; Application.Run(window);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                string hint = PortableBuild ? "\n\n便携版需要在程序旁保存 PortableData，请将程序放在可写的文件夹内。" : "";
                MessageBox.Show("程序未能启动，原数据仍保留。\n\n" + ex.Message + hint, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
