using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DesktopAgenda
{
    internal sealed class BatchTodoEditor : EditorBase
    {
        private readonly DataGridView grid;
        private readonly ComboBox priority;
        private readonly Label count;
        private bool filling;
        public AgendaData Result { get; private set; }
        public BatchTodoEditor(AgendaData current) : base("批量管理待办", 770, 510)
        {
            Result = Json.Copy(current);
            Label("勾选要处理的待办，设置后点击“保存更改”生效。", 15, 10, 730, 25);
            Button("全选", 15, 43, 75, delegate { SelectAll(true); }); Button("取消选择", 100, 43, 88, delegate { SelectAll(false); });
            count = Label("", 205, 45, 540, 25);
            grid = new DataGridView { AccessibleName = "批量管理待办列表", Location = new Point(15, 84), Size = new Size(740, 282), Font = Ui.Normal, BackgroundColor = Color.White, BorderStyle = BorderStyle.Fixed3D,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToResizeColumns = false, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ScrollBars = ScrollBars.Vertical, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 27 };
            grid.RowTemplate.Height = 29; grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Face; grid.ColumnHeadersDefaultCellStyle.Font = Ui.Normal; grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.Face;
            grid.DefaultCellStyle.SelectionBackColor = Color.Navy; grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Select", HeaderText = "选择", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 48 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "待办事项", ReadOnly = true, FillWeight = 42 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Priority", HeaderText = "优先级", ReadOnly = true, FillWeight = 24 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schedule", HeaderText = "关联日程", ReadOnly = true, FillWeight = 34 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done", HeaderText = "完成", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 48 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e) { if (e.RowIndex >= 0 && (e.ColumnIndex == 0 || e.ColumnIndex == 4)) GridChecks.Paint(e); };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate { if (!filling) UpdateCount(); }; Controls.Add(grid);
            Button("标记完成", 15, 381, 98, delegate { Apply("complete"); }); Button("恢复未完成", 123, 381, 108, delegate { Apply("restore"); }); Button("删除选中", 241, 381, 98, delegate { Apply("delete"); });
            Label("优先级：", 15, 426, 75, 26); priority = Combo("批量设置优先级", 94, 426, 210); priority.Items.AddRange(Rules.Priorities); priority.SelectedIndex = 0;
            Button("应用优先级", 316, 424, 115, delegate { Apply("priority"); });
            Button cancel = Button("取消", 532, 468, 95, delegate { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
            Button save = Button("保存更改", 640, 468, 115, delegate { grid.EndEdit(); Rules.Validate(Result); DialogResult = DialogResult.OK; Close(); }); AcceptButton = save;
            Ui.ScaleChildren(this); grid.RowTemplate.Height = Ui.S(29); grid.ColumnHeadersHeight = Ui.S(27); grid.Columns[0].Width = grid.Columns[4].Width = Ui.S(48);
            Fill(new HashSet<string>());
        }
        private HashSet<string> SelectedIds()
        {
            grid.EndEdit(); return new HashSet<string>(grid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => (string)r.Tag));
        }
        private void Fill(HashSet<string> selected)
        {
            filling = true; grid.SuspendLayout();
            try
            {
                grid.Rows.Clear();
                foreach (Todo task in Rules.SortTodos(Result, Result.Todos, DateTime.Now).OrderBy(t => t.Priority))
                {
                    Schedule schedule = Rules.ScheduleFor(Result, task.Id);
                    int index = grid.Rows.Add(selected.Contains(task.Id), task.Title, Rules.Priorities[task.Priority], schedule == null ? "未安排" : Rules.Range(schedule, true), task.Done);
                    DataGridViewRow row = grid.Rows[index]; row.Tag = task.Id;
                    bool overdue = Rules.TodoOverdue(Result, task, DateTime.Now);
                    row.Cells[1].Style.Font = task.Done ? Ui.Strike : Ui.Normal; row.Cells[1].Style.ForeColor = overdue ? Color.Firebrick : task.Done ? SystemColors.GrayText : Color.Black;
                    if (overdue) { row.Cells[1].Style.SelectionForeColor = Color.Firebrick; row.Cells[1].Style.SelectionBackColor = Color.FromArgb(255, 230, 225); }
                    row.Cells[1].ToolTipText = task.Title + "\r\n" + task.Notes;
                }
                grid.ClearSelection(); grid.CurrentCell = null;
            }
            finally { grid.ResumeLayout(); filling = false; UpdateCount(); }
        }
        private void UpdateCount() { count.Text = "共 " + Result.Todos.Count + " 项，已选择 " + grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells[0].Value)) + " 项"; }
        private void SelectAll(bool selected) { filling = true; try { grid.EndEdit(); foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = selected; } finally { filling = false; UpdateCount(); } }
        private void Apply(string action)
        {
            HashSet<string> ids = SelectedIds(); if (ids.Count == 0) { Ui.Error(this, "请先勾选要处理的待办。"); return; }
            if (action == "delete" && !Ui.Confirm(this, "将删除选中的 " + ids.Count + " 项待办，关联日程会保留。\n\n点击“保存更改”后才会正式生效。继续？", "批量删除待办")) return;
            try { Rules.ApplyTodoBatch(Result, ids, action, priority.SelectedIndex); Fill(ids); }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
        }
    }
}
