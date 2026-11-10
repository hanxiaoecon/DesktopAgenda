using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopAgenda
{
    internal sealed class PairEditor : EditorBase
    {
        private readonly AgendaData data;
        private readonly Todo originalTodo;
        private readonly ScheduleFields schedule;
        private readonly TextBox title, notes;
        private readonly ComboBox priority;
        public Schedule EditedSchedule { get; private set; }
        public Todo EditedTodo { get; private set; }

        public PairEditor(AgendaData data, Schedule currentSchedule, Todo currentTodo, bool focusTodo)
            : base("编辑日程和待办", 830, 458)
        {
            this.data = data; originalTodo = currentTodo;
            schedule = new ScheduleFields(currentSchedule.StartTime.Date, currentSchedule) { Location = new Point(16, 12), Visible = true };
            Controls.Add(schedule);

            GroupBox task = new GroupBox { Text = "待办", Bounds = new Rectangle(504, 12, 310, 388) }; Controls.Add(task);
            task.Controls.Add(Ui.Label("待办名称：", 10, 17, 288, 23));
            title = new TextBox { Text = currentTodo.Title, AccessibleName = "待办名称", Bounds = new Rectangle(10, 42, 288, 27), MaxLength = 160 }; task.Controls.Add(title);
            task.Controls.Add(Ui.Label("优先级：", 10, 75, 288, 23));
            priority = new ComboBox { AccessibleName = "待办优先级", Bounds = new Rectangle(10, 99, 288, 27), DropDownStyle = ComboBoxStyle.DropDownList }; task.Controls.Add(priority);
            priority.Items.AddRange(Rules.Priorities); priority.SelectedIndex = currentTodo.Priority;
            task.Controls.Add(Ui.Label("待办备注：", 10, 134, 288, 23));
            notes = new TextBox { Text = currentTodo.Notes ?? "", AccessibleName = "待办备注", Bounds = new Rectangle(10, 159, 288, 215), Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 5000 }; task.Controls.Add(notes);

            Label hint = Label("可直接输入时间，如 19:30；点击“保存两项”后一起生效。", 16, 413, 588, 27); hint.ForeColor = SystemColors.GrayText;
            CancelButton = Button("取消", 620, 412, 88, delegate { DialogResult = DialogResult.Cancel; Close(); });
            AcceptButton = Button("保存两项", 718, 412, 96, delegate { Save(); });
            Ui.ScaleChildren(this);
            Shown += delegate { if (focusTodo) { title.Focus(); title.SelectAll(); } else schedule.FocusTitle(); };
        }

        private void Save()
        {
            if (String.IsNullOrWhiteSpace(title.Text)) { Ui.InputError(this, "请输入待办名称。"); title.Focus(); return; }
            Todo task = Json.Copy(originalTodo); task.Title = title.Text.Trim(); task.Notes = notes.Text.Trim(); task.Priority = priority.SelectedIndex;
            try
            {
                Schedule item = schedule.Read(null);
                Rules.PreparePairEdit(data, item, task);
                EditedSchedule = item; EditedTodo = task; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
        }
    }
}
