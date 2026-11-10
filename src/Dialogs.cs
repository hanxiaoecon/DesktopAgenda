using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace DesktopAgenda
{
    internal static class Ui
    {
        private static readonly float scale = ReadScale();
        public static readonly Color Face = Color.FromArgb(212, 208, 200);
        public static readonly Font Normal = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        public static readonly Font Bold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        public static readonly Font Strike = new Font("Microsoft YaHei UI", 9f, FontStyle.Strikeout);
        public static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        private static float ReadScale() { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
        public static int S(int value) { return (int)Math.Round(value * scale); }
        public static void ScaleChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                Rectangle b = child.Bounds; child.SetBounds(S(b.X), S(b.Y), S(b.Width), S(b.Height)); ScaleChildren(child);
            }
        }
        public static Button Button(string text, EventHandler action)
        {
            Button b = new Button { Text = text, AutoSize = false, Size = new Size(S(86), S(28)), Font = Normal, BackColor = Face, FlatStyle = FlatStyle.Standard, UseVisualStyleBackColor = false, Margin = new Padding(S(3)) };
            b.Click += action; return b;
        }
        public static Label Label(string text, int x, int y, int w, int h)
        { return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent }; }
        public static void Error(IWin32Window owner, string text) { MessageBox.Show(owner, text, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Exclamation); }
        public static void InputError(IWin32Window owner, string text) { MessageBox.Show(owner, text, "请检查输入", MessageBoxButtons.OK, MessageBoxIcon.Exclamation); }
        public static bool Confirm(IWin32Window owner, string text, string title)
        { return MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes; }
    }

    internal sealed class Choice
    {
        public const string Create = "__create_related__";
        public string Id; public string Caption;
        public Choice(string id, string caption) { Id = id; Caption = caption; }
        public override string ToString() { return Caption; }
    }

    // 可直接输入的时间框：接受 19:30、19：30、19.30、1930、19点30分 等写法，也可下拉选择整点/半点。
    internal sealed class TimeBox : ComboBox
    {
        public TimeBox(string name, DateTime value, int x, int y, int width)
        {
            AccessibleName = name;
            DropDownStyle = ComboBoxStyle.DropDown;
            AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            AutoCompleteSource = AutoCompleteSource.ListItems;
            Location = new Point(x, y);
            Size = new Size(width, 26);
            for (int h = 0; h < 24; h++)
            {
                Items.Add(h.ToString("00") + ":00");
                Items.Add(h.ToString("00") + ":30");
            }
            Set(value);
        }
        public void Set(DateTime value) { Text = value.Hour.ToString("00") + ":" + value.Minute.ToString("00"); }
        public bool TryRead(out int hour, out int minute)
        {
            return TimeText.TryParse(Text, out hour, out minute);
        }
    }

    internal abstract class EditorBase : Form
    {
        protected EditorBase(string title, int width, int height)
        {
            Text = title; Font = Ui.Normal; BackColor = Ui.Face; ForeColor = Color.Black; AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(width), Ui.S(height)); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = Program.TestBuild; MaximizeBox = false; MinimizeBox = false;
            Icon = Ui.AppIcon;
        }
        protected Label Label(string text, int x, int y, int w, int h) { Label label = Ui.Label(text, x, y, w, h); Controls.Add(label); return label; }
        protected TextBox Box(string text, string name, int x, int y, int w, int h, bool multi)
        {
            TextBox box = new TextBox { Text = text ?? "", AccessibleName = name, Location = new Point(x, y), Size = new Size(w, h), Multiline = multi, BorderStyle = BorderStyle.Fixed3D, MaxLength = multi ? 5000 : 160 };
            if (multi) box.ScrollBars = ScrollBars.Vertical; Controls.Add(box); return box;
        }
        protected Button Button(string text, int x, int y, int w, EventHandler action)
        { Button button = Ui.Button(text, action); button.SetBounds(x, y, w, 29); Controls.Add(button); return button; }
        protected ComboBox Combo(string name, int x, int y, int width)
        { ComboBox box = new ComboBox { AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Size = new Size(width, 25) }; Controls.Add(box); return box; }
        protected static void SelectChoice(ComboBox box, string id)
        {
            box.SelectedIndex = 0; for (int i = 0; i < box.Items.Count; i++) if (((Choice)box.Items[i]).Id == id) { box.SelectedIndex = i; break; }
        }
        protected DateTimePicker Picker(string name, DateTime value, int x, int y, int width)
        {
            DateTimePicker box = new DateTimePicker { AccessibleName = name, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = value, Location = new Point(x, y), Size = new Size(width, 26) }; Controls.Add(box); return box;
        }
        protected void SetHeight(int height)
        {
            ClientSize = new Size(ClientSize.Width, Ui.S(height));
            if (Visible) { Rectangle area = Screen.FromControl(this).WorkingArea; if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height); }
        }
    }

    internal sealed class ScheduleFields : GroupBox
    {
        private readonly TextBox title, notes;
        private readonly Schedule existing;
        private readonly DateTimePicker startDay, endDay;
        private readonly TimeBox startClock, endClock;
        private readonly NumericUpDown advance;
        private readonly CheckBox remind, strong;
        public ScheduleFields(DateTime day, Schedule current = null)
        {
            existing = current;
            Text = current == null ? "同时新建日程" : "日程"; SetBounds(17, 215, 476, current == null ? 284 : 388); Visible = false;
            DateTime start = day.Date == DateTime.Today ? DateTime.Now.AddMinutes(30) : day.Date.AddHours(9);
            start = start.AddTicks(-(start.Ticks % TimeSpan.TicksPerMinute));
            if (current != null) start = current.StartTime;
            DateTime end = current == null ? start.AddMinutes(30) : current.EndTime;
            string prefix = current == null ? "新日程" : "日程";
            AddLabel(current == null ? "日程名称（留空使用待办名称）：" : "日程名称：", 10, 17, 450);
            title = new TextBox { Text = current == null ? "" : current.Title, AccessibleName = prefix + "名称", Location = new Point(10, 42), Size = new Size(454, 27), MaxLength = 160 }; Controls.Add(title);
            AddLabel("开始日期：", 10, 75, 220); AddLabel("开始时间：", 251, 75, 200);
            startDay = AddPicker(prefix + "开始日期", start, 10, 99, 224); startClock = AddTime(prefix + "开始时间", start, 252, 99, 212);
            AddLabel("结束日期：", 10, 134, 220); AddLabel("结束时间：", 251, 134, 200);
            endDay = AddPicker(prefix + "结束日期", end, 10, 158, 224); endClock = AddTime(prefix + "结束时间", end, 252, 158, 212);
            remind = new CheckBox { Text = "开始前提醒", Checked = current == null || current.AdvanceMinutes >= 0, Location = new Point(10, 199), Size = new Size(125, 25) }; Controls.Add(remind);
            advance = new NumericUpDown { AccessibleName = prefix + "提前提醒分钟数", Minimum = 0, Maximum = 525600, Value = current == null ? 10 : Math.Max(0, current.AdvanceMinutes), Location = new Point(138, 200), Size = new Size(105, 27) }; Controls.Add(advance);
            AddLabel("分钟（0 表示开始时）", 254, 200, 210);
            strong = new CheckBox { Text = "持续提醒：关闭弹窗后，5分钟再提醒", Checked = current != null && current.Strong, Location = new Point(10, 242), Size = new Size(454, 25) }; Controls.Add(strong);
            remind.CheckedChanged += delegate { advance.Enabled = strong.Enabled = remind.Checked; };
            advance.Enabled = strong.Enabled = remind.Checked;
            if (current != null)
            {
                AddLabel("日程备注：", 10, 270, 454);
                notes = new TextBox { Text = current.Notes ?? "", AccessibleName = "日程备注", Location = new Point(10, 298), Size = new Size(454, 76), MaxLength = 5000, Multiline = true, ScrollBars = ScrollBars.Vertical }; Controls.Add(notes);
            }
        }
        private void AddLabel(string text, int x, int y, int width) { Controls.Add(Ui.Label(text, x, y, width, 23)); }
        private DateTimePicker AddPicker(string name, DateTime value, int x, int y, int width)
        {
            DateTimePicker picker = new DateTimePicker { AccessibleName = name, Value = value, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Location = new Point(x, y), Size = new Size(width, 26) }; Controls.Add(picker); return picker;
        }
        private TimeBox AddTime(string name, DateTime value, int x, int y, int width)
        {
            TimeBox box = new TimeBox(name, value, x, y, width); Controls.Add(box); return box;
        }
        public Schedule Read(string fallbackTitle)
        {
            string name = String.IsNullOrWhiteSpace(title.Text) ? fallbackTitle : title.Text.Trim();
            if (String.IsNullOrWhiteSpace(name)) { title.Focus(); throw new InvalidDataException("请输入日程名称。"); }
            int sh, sm, eh, em;
            if (!startClock.TryRead(out sh, out sm)) { startClock.Focus(); throw new InvalidDataException("日程的开始时间格式不正确，可直接输入如 19:30，或从下拉框选择。"); }
            if (!endClock.TryRead(out eh, out em)) { endClock.Focus(); throw new InvalidDataException("日程的结束时间格式不正确，请直接输入如 19:30，或从下拉框选择。"); }
            DateTime start = startDay.Value.Date.AddHours(sh).AddMinutes(sm);
            DateTime end = endDay.Value.Date.AddHours(eh).AddMinutes(em);
            Schedule result = existing == null ? new Schedule() : Json.Copy(existing);
            result.Title = name; result.Start = Clock.Format(start); result.End = Clock.Format(end);
            result.AdvanceMinutes = remind.Checked ? (int)advance.Value : -1; result.Strong = remind.Checked && strong.Checked;
            if (notes != null) result.Notes = notes.Text.Trim();
            return result;
        }
        public void FocusTitle() { title.Focus(); title.SelectAll(); }
    }

    internal sealed class ScheduleEditor : EditorBase
    {
        private readonly AgendaData data;
        private readonly TextBox title, notes;
        private readonly DateTimePicker startDay, endDay;
        private readonly TimeBox startClock, endClock;
        private readonly NumericUpDown advance;
        private readonly CheckBox remind, strong;
        private readonly ComboBox related;
        private readonly GroupBox newTodoBox;
        private readonly TextBox newTodoTitle;
        private readonly ComboBox newTodoPriority;
        public Schedule Result { get; private set; }
        public Todo CreatedTodo { get; private set; }
        public ScheduleEditor(AgendaData current, Schedule existing, DateTime day, string suggestedTodo, DateTime? suggestedStart = null) : base(existing == null ? "新建日程" : "编辑日程", 510, 546)
        {
            data = current;
            DateTime start = day.Date == DateTime.Today ? DateTime.Now.AddMinutes(30) : day.Date.AddHours(9);
            if (suggestedStart.HasValue) start = suggestedStart.Value;
            start = start.AddTicks(-(start.Ticks % TimeSpan.TicksPerMinute));
            Result = existing == null ? new Schedule { Start = Clock.Format(start), End = Clock.Format(start.AddMinutes(30)), TodoId = suggestedTodo } : Json.Copy(existing);
            Todo suggested = data.Todos.FirstOrDefault(t => t.Id == suggestedTodo);
            if (existing == null && suggested != null) Result.Title = suggested.Title;
            Label("日程名称：", 16, 13, 475, 22); title = Box(Result.Title, "日程名称", 17, 39, 476, 27, false);
            Label("开始日期：", 16, 81, 200, 22); Label("开始时间：", 279, 81, 200, 22);
            startDay = Picker("开始日期", Result.StartTime, 17, 108, 244); startClock = AddTime("开始时间", Result.StartTime, 280, 108, 213);
            Label("结束日期：", 16, 148, 200, 22); Label("结束时间：", 279, 148, 200, 22);
            endDay = Picker("结束日期", Result.EndTime, 17, 175, 244); endClock = AddTime("结束时间", Result.EndTime, 280, 175, 213);
            remind = new CheckBox { Text = "开始前提醒", Checked = Result.AdvanceMinutes >= 0, Location = new Point(17, 222), Size = new Size(130, 24) }; Controls.Add(remind);
            advance = new NumericUpDown { Minimum = 0, Maximum = 525600, Value = Math.Max(0, Result.AdvanceMinutes), Location = new Point(151, 221), Size = new Size(112, 27), AccessibleName = "提前提醒分钟数" }; Controls.Add(advance);
            Label("分钟（0 表示开始时提醒）", 275, 220, 220, 28);
            strong = new CheckBox { Text = "持续提醒：关闭弹窗后，5分钟再提醒", Checked = Result.Strong, Location = new Point(17, 258), Size = new Size(476, 25) }; Controls.Add(strong);
            remind.CheckedChanged += delegate { advance.Enabled = remind.Checked; strong.Enabled = remind.Checked; }; advance.Enabled = strong.Enabled = remind.Checked;
            Label("关联待办（可选，也可同时新建）：", 16, 296, 475, 22); related = Combo("关联待办", 17, 323, 476);
            related.Items.Add(new Choice(null, "（不关联待办，例如上课、会议）"));
            related.Items.Add(new Choice(Choice.Create, "＋ 同时新建待办"));
            foreach (Todo task in data.Todos.Where(t => !data.Schedules.Any(s => s.Id != Result.Id && s.TodoId == t.Id)).OrderBy(t => t.Done).ThenBy(t => t.Priority).ThenBy(t => t.Title))
                related.Items.Add(new Choice(task.Id, task.Title + (task.Done ? " [已完成]" : "") + " / " + Rules.Priorities[task.Priority]));
            SelectChoice(related, Result.TodoId);
            newTodoBox = new GroupBox { Text = "同时新建待办", Location = new Point(17, 361), Size = new Size(476, 95), Visible = false }; Controls.Add(newTodoBox);
            newTodoBox.Controls.Add(Ui.Label("待办名称（留空使用日程名称）：", 10, 18, 285, 22));
            newTodoTitle = new TextBox { AccessibleName = "新待办名称", Location = new Point(10, 44), Size = new Size(280, 27), MaxLength = 160 }; newTodoBox.Controls.Add(newTodoTitle);
            newTodoBox.Controls.Add(Ui.Label("优先级：", 302, 18, 160, 22));
            newTodoPriority = new ComboBox { AccessibleName = "新待办优先级", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(302, 44), Size = new Size(162, 25) }; newTodoPriority.Items.AddRange(Rules.Priorities); newTodoPriority.SelectedIndex = 0; newTodoBox.Controls.Add(newTodoPriority);
            Label notesLabel = Label("备注：", 16, 364, 475, 22); notes = Box(Result.Notes, "日程备注", 17, 391, 476, 72, true);
            Label hint = Label("时间可直接输入，如 19:30；同一时间只能有一个日程，前后相接允许保存。", 16, 474, 475, 22); hint.ForeColor = SystemColors.GrayText;
            Button cancel = Button("取消", 305, 510, 88, delegate { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
            Button save = Button("确定", 405, 510, 88, delegate { Save(); }); AcceptButton = save;
            Ui.ScaleChildren(this);
            related.SelectedIndexChanged += delegate
            {
                bool create = ((Choice)related.SelectedItem).Id == Choice.Create; int extra = create ? 105 : 0;
                newTodoBox.Visible = create; notesLabel.Top = Ui.S(364 + extra); notes.Top = Ui.S(391 + extra); hint.Top = Ui.S(474 + extra); cancel.Top = save.Top = Ui.S(510 + extra); SetHeight(546 + extra);
            };
            Shown += delegate { title.Focus(); title.SelectAll(); };
        }
        private TimeBox AddTime(string name, DateTime value, int x, int y, int width)
        {
            TimeBox box = new TimeBox(name, value, x, y, width); Controls.Add(box); return box;
        }
        private void Save()
        {
            if (String.IsNullOrWhiteSpace(title.Text)) { Ui.InputError(this, "请输入日程名称。"); title.Focus(); return; }
            int sh, sm, eh, em;
            if (!startClock.TryRead(out sh, out sm)) { Ui.InputError(this, "开始时间格式不正确，可直接输入如 19:30，或从下拉框选择。"); startClock.Focus(); startClock.SelectAll(); return; }
            if (!endClock.TryRead(out eh, out em)) { Ui.InputError(this, "结束时间格式不正确，可直接输入如 19:30，或从下拉框选择。"); endClock.Focus(); endClock.SelectAll(); return; }
            DateTime start = startDay.Value.Date.AddHours(sh).AddMinutes(sm), end = endDay.Value.Date.AddHours(eh).AddMinutes(em);
            if (end <= start) { Ui.InputError(this, "结束时间必须晚于开始时间。"); return; }
            Schedule proposed = Json.Copy(Result); proposed.Title = title.Text.Trim(); proposed.Start = Clock.Format(start); proposed.End = Clock.Format(end);
            proposed.Notes = notes.Text.Trim(); proposed.AdvanceMinutes = remind.Checked ? (int)advance.Value : -1; proposed.Strong = remind.Checked && strong.Checked;
            string selected = ((Choice)related.SelectedItem).Id;
            Todo created = selected == Choice.Create ? new Todo { Title = String.IsNullOrWhiteSpace(newTodoTitle.Text) ? proposed.Title : newTodoTitle.Text.Trim(), Priority = newTodoPriority.SelectedIndex } : null;
            proposed.TodoId = created == null ? selected : created.Id;
            if (proposed.Start != Result.Start || proposed.AdvanceMinutes != Result.AdvanceMinutes || proposed.Strong != Result.Strong) proposed.ResetReminder();
            try { Rules.SaveSchedule(Json.Copy(data), proposed, created); }
            catch (Exception ex) { Ui.Error(this, ex.Message); return; }
            Result = proposed; CreatedTodo = created; DialogResult = DialogResult.OK; Close();
        }
    }

    internal sealed class TodoEditor : EditorBase
    {
        private readonly TextBox title, notes;
        private readonly ComboBox priority, related;
        private readonly ScheduleFields newScheduleBox;
        public Todo Result { get; private set; }
        public string ScheduleId { get; private set; }
        public Schedule CreatedSchedule { get; private set; }
        public TodoEditor(AgendaData data, Todo existing, int defaultPriority, DateTime day) : base(existing == null ? "新建待办" : "编辑待办", 510, 425)
        {
            Result = existing == null ? new Todo { Priority = defaultPriority } : Json.Copy(existing);
            Label("待办事项：", 16, 13, 475, 22); title = Box(Result.Title, "待办事项", 17, 40, 476, 27, false);
            Label("优先级：", 16, 83, 475, 22); priority = Combo("优先级", 17, 111, 476); priority.Items.AddRange(Rules.Priorities); priority.SelectedIndex = Result.Priority;
            Label("关联日程（可选，也可同时新建）：", 16, 152, 475, 22); related = Combo("关联日程", 17, 179, 476);
            related.Items.Add(new Choice(null, "（暂不安排时间）"));
            related.Items.Add(new Choice(Choice.Create, "＋ 同时新建日程"));
            foreach (Schedule schedule in data.Schedules.Where(s => String.IsNullOrEmpty(s.TodoId) || s.TodoId == Result.Id).OrderBy(s => s.StartTime))
                related.Items.Add(new Choice(schedule.Id, Rules.Range(schedule, true) + "  " + schedule.Title));
            Schedule current = Rules.ScheduleFor(data, Result.Id); SelectChoice(related, current == null ? null : current.Id);
            newScheduleBox = new ScheduleFields(day); Controls.Add(newScheduleBox);
            Label notesLabel = Label("备注：", 16, 221, 475, 22); notes = Box(Result.Notes, "待办备注", 17, 248, 476, 88, true);
            Label hint = Label("选择“同时新建日程”，填写时间后一次保存两项；时间可直接输入，如 19:30。", 16, 345, 475, 23); hint.ForeColor = SystemColors.GrayText;
            Button cancel = Button("取消", 305, 385, 88, delegate { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
            Button save = Button("确定", 405, 385, 88, delegate
            {
                if (String.IsNullOrWhiteSpace(title.Text)) { Ui.InputError(this, "请输入待办事项。"); title.Focus(); return; }
                Todo proposed = Json.Copy(Result); proposed.Title = title.Text.Trim(); proposed.Notes = notes.Text.Trim(); proposed.Priority = priority.SelectedIndex;
                string selected = ((Choice)related.SelectedItem).Id;
                Schedule created;
                try
                {
                    created = selected == Choice.Create ? newScheduleBox.Read(proposed.Title) : null;
                    if (created != null) created.TodoId = proposed.Id;
                    Rules.SaveTodo(Json.Copy(data), proposed, created == null ? selected : created.Id, created);
                }
                catch (Exception ex) { Ui.Error(this, ex.Message); return; }
                Result = proposed; CreatedSchedule = created; ScheduleId = created == null ? selected : created.Id; DialogResult = DialogResult.OK; Close();
            }); AcceptButton = save;
            Ui.ScaleChildren(this);
            related.SelectedIndexChanged += delegate
            {
                bool create = ((Choice)related.SelectedItem).Id == Choice.Create; int extra = create ? 294 : 0;
                newScheduleBox.Visible = create; notesLabel.Top = Ui.S(221 + extra); notes.Top = Ui.S(248 + extra); hint.Top = Ui.S(345 + extra); cancel.Top = save.Top = Ui.S(385 + extra); SetHeight(425 + extra);
            };
            Shown += delegate { title.Focus(); title.SelectAll(); };
        }
    }

    internal static class Chime
    {
        private static MemoryStream stream;
        private static SoundPlayer player;
        public static byte[] Wav()
        {
            const int rate = 22050; double[] notes = { 523.25, 659.25, 783.99 }; int samplesPerNote = (int)(rate * 0.22); int total = samplesPerNote * notes.Length;
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + total * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(total * 2);
                for (int note = 0; note < notes.Length; note++) for (int i = 0; i < samplesPerNote; i++)
                {
                    double t = (double)i / rate; double envelope = Math.Min(1.0, t / .012) * Math.Pow(1.0 - (double)i / samplesPerNote, 1.4);
                    writer.Write((short)(Math.Sin(2 * Math.PI * notes[note] * t) * 14000 * envelope));
                }
                writer.Flush(); return buffer.ToArray();
            }
        }
        public static void Play()
        {
            if (Program.TestBuild) return;
            try { if (player == null) { stream = new MemoryStream(Wav()); player = new SoundPlayer(stream); player.Load(); } player.Play(); }
            catch { SystemSounds.Exclamation.Play(); }
        }
    }

    internal sealed class ReminderDialog : EditorBase
    {
        private bool resolved;
        private readonly Schedule schedule;
        private readonly Action<string, int> action;
        private readonly Timer timer;
        private readonly bool preview;
        private readonly Func<bool> soundOn;
        public ReminderDialog(Schedule item, Todo task, bool test, int offset, Func<bool> audio, Action<string, int> callback) : base(test ? "提醒测试" : "日程提醒", 450, 290)
        {
            schedule = item; action = callback; preview = test; soundOn = audio;
            ShowInTaskbar = true; TopMost = true; StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - Ui.S(18 + offset * 24), area.Bottom - Height - Ui.S(18 + offset * 24));
            Label name = Label(item.Title, 15, 12, 420, 43); name.Font = new Font(Ui.Normal.FontFamily, 12, FontStyle.Bold); name.AutoEllipsis = true;
            Label(Rules.Range(item, true), 15, 65, 420, 26);
            Label(task == null ? "此日程没有关联待办。" : "关联待办：" + task.Title + (task.Done ? " [已完成]" : ""), 15, 103, 420, 35).AutoEllipsis = true;
            Label(String.IsNullOrWhiteSpace(item.Notes) ? (test ? "测试不会新增或修改日程。" : "请确认安排，或选择稍后提醒。") : item.Notes.Replace("\r", " ").Replace("\n", " "), 15, 146, 420, 30).AutoEllipsis = true;
            ComboBox snooze = Combo("稍后提醒时长", 15, 197, 122); snooze.Items.AddRange(new object[] { "5 分钟", "10 分钟", "15 分钟", "30 分钟", "60 分钟" }); snooze.SelectedIndex = 0; snooze.Enabled = !test;
            Button("稍后提醒", 147, 195, 94, delegate { Resolve("snooze", new[] { 5, 10, 15, 30, 60 }[snooze.SelectedIndex]); }).Enabled = !test;
            bool done = task == null ? item.Done : task.Done;
            if (!test && !done) Button(task == null ? "完成日程" : "完成待办", 251, 195, 84, delegate { Resolve("done", 0); });
            Button confirm = Button(test ? "关闭测试" : "已知晓", test || done ? 335 : 345, 195, test || done ? 100 : 90, delegate { Resolve("dismiss", 0); }); AcceptButton = confirm;
            Label(item.Strong && !test ? "直接关闭窗口：5分钟后再次提醒。" : "直接关闭窗口：停止本项提醒，保留日程。", 15, 243, 420, 30).ForeColor = SystemColors.GrayText;
            Ui.ScaleChildren(this);
            timer = new Timer { Interval = 30000 }; timer.Tick += delegate { if (soundOn() && schedule.Strong) Chime.Play(); };
            Shown += delegate { if (soundOn()) Chime.Play(); if (item.Strong && !test) timer.Start(); };
            FormClosing += delegate { timer.Stop(); if (!resolved) { resolved = true; action(test ? "preview" : item.Strong ? "snooze" : "dismiss", item.Strong ? 5 : 0); } };
            FormClosed += delegate { timer.Dispose(); };
        }
        public void CloseWithoutAction() { if (IsDisposed) return; resolved = true; Close(); }
        private void Resolve(string kind, int minutes) { if (resolved) return; resolved = true; action(preview ? "preview" : kind, minutes); if (!IsDisposed) Close(); }
    }
}
