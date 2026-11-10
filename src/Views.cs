using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DesktopAgenda
{
    internal static class GridChecks
    {
        public static void Paint(DataGridViewCellPaintingEventArgs e)
        {
            e.PaintBackground(e.ClipBounds, true); e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
            int size = Ui.S(15); Rectangle box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size);
            ControlPaint.DrawCheckBox(e.Graphics, box, Convert.ToBoolean(e.Value) ? ButtonState.Checked : ButtonState.Normal);
            using (Pen outline = new Pen(Color.Gray, Math.Max(1, Ui.S(1)))) e.Graphics.DrawRectangle(outline, box.X, box.Y, box.Width - 1, box.Height - 1);
            e.Handled = true;
        }
    }
    internal sealed class TodoGrid : DataGridView
    {
        public event Action<string> ToggleRequested;
        public event Action<string> EditRequested;
        public string SelectedTodoId { get { return SelectedRows.Count == 0 || CurrentRow == null ? null : CurrentRow.Tag as string; } }
        public TodoGrid()
        {
            Dock = DockStyle.Fill; Margin = new Padding(0); Font = Ui.Normal; BackgroundColor = Color.White; BorderStyle = BorderStyle.Fixed3D;
            RowHeadersVisible = false; AllowUserToAddRows = false; AllowUserToDeleteRows = false; AllowUserToResizeRows = false; AllowUserToResizeColumns = false;
            AllowUserToOrderColumns = false; MultiSelect = false; ReadOnly = true; SelectionMode = DataGridViewSelectionMode.FullRowSelect; ScrollBars = ScrollBars.Vertical;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; RowTemplate.Height = Ui.S(29);
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; ColumnHeadersHeight = Ui.S(26); EnableHeadersVisualStyles = false;
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Ui.Face, ForeColor = Color.Black, Font = Ui.Normal, SelectionBackColor = Ui.Face, SelectionForeColor = Color.Black, Padding = new Padding(Ui.S(3), 0, 0, 0) };
            DefaultCellStyle = new DataGridViewCellStyle { Font = Ui.Normal, ForeColor = Color.Black, BackColor = Color.White, SelectionBackColor = Color.Navy, SelectionForeColor = Color.White, Padding = new Padding(Ui.S(3), 0, 0, 0) };
            GridColor = Color.FromArgb(219, 219, 219); CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "待办事项", FillWeight = 62, MinimumWidth = Ui.S(55), SortMode = DataGridViewColumnSortMode.NotSortable });
            Columns.Add(new DataGridViewTextBoxColumn { Name = "Schedule", HeaderText = "关联日程", FillWeight = 38, MinimumWidth = Ui.S(48), SortMode = DataGridViewColumnSortMode.NotSortable });
            Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done", HeaderText = "完成", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = Ui.S(44), Resizable = DataGridViewTriState.False, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Standard, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = new Padding(0) } });
            DoubleBuffered = true;
        }
        public void FillRows(List<Todo> tasks, AgendaData data, string selectedId, DateTime now)
        {
            int first = FirstDisplayedScrollingRowIndex; SuspendLayout();
            try
            {
                Rows.Clear();
                foreach (Todo task in tasks)
                {
                    Schedule schedule = Rules.ScheduleFor(data, task.Id);
                    int index = Rows.Add(task.Title, schedule == null ? "未安排" : schedule.StartTime.ToString("MM-dd HH:mm"), task.Done);
                    bool overdue = Rules.TodoOverdue(data, task, now);
                    DataGridViewRow row = Rows[index]; row.Tag = task.Id; row.DefaultCellStyle.Font = task.Done ? Ui.Strike : Ui.Normal; row.DefaultCellStyle.ForeColor = overdue ? Color.Firebrick : task.Done ? SystemColors.GrayText : Color.Black;
                    row.DefaultCellStyle.SelectionBackColor = overdue ? Color.FromArgb(255, 230, 225) : Color.Navy;
                    row.DefaultCellStyle.SelectionForeColor = overdue ? Color.Firebrick : Color.White;
                    string tip = task.Title + "\r\n" + (schedule == null ? "未安排日程；任务时间：" + (String.IsNullOrEmpty(task.CreatedAt) ? "未知" : Clock.Parse(task.CreatedAt).ToString("yyyy-MM-dd HH:mm")) : Rules.Range(schedule, true) + " " + schedule.Title) + (overdue ? "\r\n已逾期：任务时间已过，仍未完成。" : "") + "\r\n" + task.Notes;
                    foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = cell.ColumnIndex == 2 ? (task.Done ? "已完成；点击恢复为未完成" : "点击完成此待办") : tip;
                }
                ClearSelection(); CurrentCell = null;
                if (first >= 0 && Rows.Count > 0) FirstDisplayedScrollingRowIndex = Math.Min(first, Rows.Count - 1);
                foreach (DataGridViewRow row in Rows) if ((string)row.Tag == selectedId) { CurrentCell = row.Cells[0]; row.Selected = true; break; }
            }
            finally { ResumeLayout(); }
        }
        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 2) GridChecks.Paint(e); else base.OnCellPainting(e);
        }
        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) { CurrentCell = Rows[e.RowIndex].Cells[e.ColumnIndex]; Rows[e.RowIndex].Selected = true; }
            else if (e.Button == MouseButtons.Right) { ClearSelection(); CurrentCell = null; }
            base.OnCellMouseDown(e);
        }
        protected override void OnCellMouseClick(DataGridViewCellMouseEventArgs e)
        {
            base.OnCellMouseClick(e);
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == 2 && ToggleRequested != null) ToggleRequested((string)Rows[e.RowIndex].Tag);
        }
        protected override void OnCellMouseDoubleClick(DataGridViewCellMouseEventArgs e)
        {
            base.OnCellMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex != 2 && EditRequested != null) EditRequested((string)Rows[e.RowIndex].Tag);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && ContextMenu != null) ContextMenu.Show(this, e.Location);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            string id = SelectedTodoId;
            if ((e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)) && ContextMenu != null)
            {
                Rectangle cell = CurrentCell == null ? new Rectangle(0, 0, 1, 1) : GetCellDisplayRectangle(CurrentCell.ColumnIndex, CurrentCell.RowIndex, false);
                ContextMenu.Show(this, new Point(Math.Max(0, cell.X + 10), Math.Max(0, cell.Bottom))); e.Handled = e.SuppressKeyPress = true; return;
            }
            if (id != null && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                e.Handled = e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Space && ToggleRequested != null) ToggleRequested(id);
                if (e.KeyCode == Keys.Enter && EditRequested != null) EditRequested(id);
                return;
            }
            base.OnKeyDown(e);
        }
    }

    internal sealed class DayTimeline : ScrollableControl
    {
        private sealed class TimelineBlock
        {
            public Schedule Item;
            public float TimeTop, TimeBottom, LabelTop;
            public int LabelHeight;
            public bool NeedsMarker;
        }

        private AgendaData data;
        private readonly List<TimelineBlock> blocks = new List<TimelineBlock>();
        private readonly ToolTip tooltip = new ToolTip();
        private string hoverId;
        private DateTime day = DateTime.MinValue;
        private bool resetPending;
        private int HourHeight { get { return Ui.S(64); } }
        private int TopPadding { get { return Ui.S(15); } }
        public string SelectedId { get; private set; }
        public DateTime ClickedTime { get; private set; }
        public event Action<string> SelectionChanged;
        public event Action<string> EditRequested;
        public event Action<DateTime> NewRequested;
        public event Action<string> DeleteRequested;
        public DayTimeline()
        {
            Dock = DockStyle.Fill; AutoScroll = true; BackColor = Color.White; Font = Ui.Normal; TabStop = true;
            AccessibleName = "全天日程时间轴"; AccessibleRole = AccessibleRole.List;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScrollMinSize = new Size(0, TopPadding * 2 + HourHeight * 24); AutoScrollMargin = Size.Empty;
        }
        public void SetData(AgendaData current, DateTime selectedDay, string selectedId)
        {
            bool changed = day != selectedDay.Date; day = selectedDay.Date; data = current; SelectedId = selectedId;
            LayoutBlocks(DateTime.Now);
            if (!blocks.Any(b => b.Item.Id == SelectedId)) SelectedId = null;
            if (changed) { resetPending = true; ClickedTime = day.AddHours(9); if (IsHandleCreated) BeginInvoke((MethodInvoker)ResetView); }
            Invalidate();
        }
        private void ResetView()
        {
            if (IsDisposed || !resetPending) return; resetPending = false;
            LocateNow();
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (resetPending) BeginInvoke((MethodInvoker)ResetView); }
        public void LocateNow() { if (day == DateTime.Today) ScrollToMinute(Math.Max(0, DateTime.Now.TimeOfDay.TotalMinutes - 60)); else ScrollToMinute(7 * 60); }
        public void RefreshClock() { if (day == DateTime.Today) Invalidate(); }
        private void ScrollToMinute(double minute)
        {
            ScrollToOffset(minute <= 0 ? 0 : OffsetAtMinute(minute));
        }
        private void ScrollToOffset(float offset)
        {
            int max = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(max, (int)offset))); Invalidate();
        }
        private float OffsetAtMinute(double minute) { return (float)(TopPadding + minute * HourHeight / 60.0); }
        private float AtMinute(double minute) { return OffsetAtMinute(minute) + AutoScrollPosition.Y; }
        private void LayoutBlocks(DateTime now)
        {
            blocks.Clear();
            float nextTop = TopPadding;
            int minimum = Math.Max(Ui.S(21), Ui.Normal.Height + Ui.S(4));
            foreach (Schedule item in data.Schedules.Where(s => Rules.ScheduleVisible(data, s, day, now)).OrderBy(s => s.StartTime))
            {
                double from, to;
                if (!TimelineMath.Slice(item, day, out from, out to)) continue;
                float timeTop = OffsetAtMinute(from), timeBottom = OffsetAtMinute(to);
                int naturalHeight = (int)Math.Floor(timeBottom - timeTop - 2);
                int labelHeight = Math.Max(minimum, naturalHeight);
                // Let a short label use the space before its end so the next event can stay in place.
                float preferredTop = labelHeight > naturalHeight ? timeBottom - 1 - labelHeight : timeTop + 1;
                float labelTop = Math.Max(preferredTop, nextTop);
                blocks.Add(new TimelineBlock
                {
                    Item = item, TimeTop = timeTop, TimeBottom = timeBottom,
                    LabelTop = labelTop, LabelHeight = labelHeight,
                    NeedsMarker = labelHeight > naturalHeight || labelTop != preferredTop
                });
                nextTop = labelTop + labelHeight + 1;
            }
            AutoScrollMinSize = new Size(0, (int)Math.Ceiling(Math.Max(OffsetAtMinute(1440), nextTop)) + TopPadding);
        }
        private RectangleF LabelBounds(TimelineBlock block)
        {
            return new RectangleF(Ui.S(67), block.LabelTop + AutoScrollPosition.Y, Math.Max(1, ClientSize.Width - Ui.S(77)), block.LabelHeight);
        }
        private RectangleF MarkerBounds(TimelineBlock block)
        {
            return new RectangleF(Ui.S(57), block.TimeTop + AutoScrollPosition.Y, Ui.S(5), Math.Max(1, block.TimeBottom - block.TimeTop));
        }
        private Schedule Hit(Point point)
        {
            TimelineBlock hit = blocks.FirstOrDefault(b => LabelBounds(b).Contains(point) || (b.NeedsMarker && MarkerBounds(b).Contains(point)));
            return hit == null ? null : hit.Item;
        }
        private DateTime TimeAt(Point point) { return day.AddMinutes(TimelineMath.SnapMinute((point.Y - AutoScrollPosition.Y - TopPadding) * 60.0 / HourHeight)); }
        private void Pick(Schedule schedule)
        {
            SelectedId = schedule == null ? null : schedule.Id; if (SelectionChanged != null) SelectionChanged(SelectedId); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); DateTime now = DateTime.Now; Graphics g = e.Graphics; int right = ClientSize.Width - Ui.S(9), axis = Ui.S(55);
            using (SolidBrush quiet = new SolidBrush(Color.FromArgb(247, 247, 242)))
            { g.FillRectangle(quiet, axis, AtMinute(0), Math.Max(0, right - axis), HourHeight * 7); g.FillRectangle(quiet, axis, AtMinute(22 * 60), Math.Max(0, right - axis), HourHeight * 2); }
            using (Pen hour = new Pen(Color.FromArgb(180, 180, 180), Ui.S(1)))
            using (Pen half = new Pen(Color.FromArgb(222, 222, 222), Ui.S(1)))
            {
                for (int h = 0; h <= 24; h++)
                {
                    float y = AtMinute(h * 60); if (y < -HourHeight || y > ClientSize.Height + HourHeight) continue;
                    g.DrawLine(hour, axis, y, right, y);
                    TextRenderer.DrawText(g, h.ToString("00") + ":00", Ui.Normal, new Rectangle(Ui.S(3), (int)y - Ui.S(9), Ui.S(47), Ui.S(19)), Color.DimGray, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (h < 24) g.DrawLine(half, axis, y + HourHeight / 2f, right, y + HourHeight / 2f);
                }
            }
            if (day == DateTime.Today)
            {
                float y = AtMinute(now.TimeOfDay.TotalMinutes);
                if (y >= -Ui.S(10) && y <= ClientSize.Height + Ui.S(10))
                {
                    using (Pen line = new Pen(Color.Firebrick, Ui.S(2))) g.DrawLine(line, axis, y, right, y);
                    Rectangle label = new Rectangle(Ui.S(3), (int)y - Ui.S(10), Ui.S(48), Ui.S(20));
                    using (SolidBrush red = new SolidBrush(Color.Firebrick)) g.FillRectangle(red, label);
                    TextRenderer.DrawText(g, now.ToString("HH:mm"), Ui.Normal, label, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            foreach (TimelineBlock block in blocks)
            {
                RectangleF bounds = LabelBounds(block), marker = MarkerBounds(block);
                if (Math.Max(bounds.Bottom, marker.Bottom) < 0 || Math.Min(bounds.Top, marker.Top) > ClientSize.Height) continue;
                Schedule schedule = block.Item;
                Todo todo = data.Todos.FirstOrDefault(t => t.Id == schedule.TodoId);
                bool done = Rules.ScheduleComplete(data, schedule), overdue = Rules.IsOverdue(data, schedule, now);
                Color titleColor = overdue ? Color.Firebrick : done ? SystemColors.GrayText : Color.Black;
                Color rangeColor = overdue ? Color.Firebrick : done ? SystemColors.GrayText : Color.FromArgb(50, 60, 75);
                Color[] colors = { Color.FromArgb(249, 223, 217), Color.FromArgb(251, 236, 194), Color.FromArgb(216, 231, 245), Color.FromArgb(228, 236, 220) };
                Color color = todo == null ? Color.FromArgb(222, 231, 240) : colors[todo.Priority]; bool selected = SelectedId == schedule.Id;
                Color outline = selected ? Color.Navy : Color.FromArgb(101, 116, 140);
                if (block.NeedsMarker)
                {
                    using (Pen leader = new Pen(outline)) g.DrawLine(leader, marker.Right, marker.Top + marker.Height / 2, bounds.Left, bounds.Top + bounds.Height / 2);
                    using (SolidBrush bar = new SolidBrush(outline)) g.FillRectangle(bar, marker);
                }
                using (SolidBrush fill = new SolidBrush(selected ? Color.FromArgb(205, 216, 237) : color)) g.FillRectangle(fill, bounds);
                using (Pen border = new Pen(outline, selected ? Ui.S(2) : 1)) g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                Rectangle text = Rectangle.Round(bounds); text.Inflate(-Ui.S(5), -Ui.S(2));
                string range = (schedule.StartTime.Date < day ? "00:00" : schedule.StartTime.ToString("HH:mm")) + "–" + (schedule.EndTime >= day.AddDays(1) ? "24:00" : schedule.EndTime.ToString("HH:mm"));
                if (schedule.StartTime.Date < day) range = "← " + range;
                if (schedule.EndTime > day.AddDays(1)) range += " →";
                string caption = schedule.Title + (done ? " [已完成]" : overdue ? " [已逾期]" : "");
                if (text.Height >= Ui.S(37))
                {
                    TextRenderer.DrawText(g, caption, done ? Ui.Strike : Ui.Bold, new Rectangle(text.X, text.Y, text.Width, Ui.S(19)), titleColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, range, Ui.Normal, new Rectangle(text.X, text.Y + Ui.S(20), text.Width, Ui.S(17)), rangeColor, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
                else
                {
                    Rectangle compact = Rectangle.Round(bounds); compact.X += Ui.S(4); compact.Width -= Ui.S(8);
                    TextRenderer.DrawText(g, range + "  " + caption, done ? Ui.Strike : Ui.Normal, compact, titleColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); ClickedTime = TimeAt(e.Location); Pick(Hit(e.Location)); base.OnMouseDown(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e); if (e.Button != MouseButtons.Left) return; Schedule item = Hit(e.Location);
            if (item != null) { if (EditRequested != null) EditRequested(item.Id); }
            else if (NewRequested != null) NewRequested(TimeAt(e.Location));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); Schedule item = Hit(e.Location); string id = item == null ? null : item.Id;
            Cursor = item == null ? Cursors.Default : Cursors.Hand;
            if (id == hoverId) return; hoverId = id;
            Todo todo = item == null ? null : data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
            tooltip.SetToolTip(this, item == null ? "空白处双击或右键可新增日程；滚轮查看全天。" : item.Title + "\r\n" + Rules.Range(item, true) + (todo == null ? "" : "\r\n关联待办：" + todo.Title) + (Rules.IsOverdue(data, item, DateTime.Now) ? "\r\n已逾期：日程已结束，仍未完成。" : "") + "\r\n" + item.Notes);
        }
        protected override bool IsInputKey(Keys keyData) { Keys key = keyData & Keys.KeyCode; return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int index = blocks.FindIndex(b => b.Item.Id == SelectedId);
            Schedule current = index < 0 ? null : blocks[index].Item;
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (blocks.Count > 0)
                {
                    if (index < 0 && e.KeyCode == Keys.Up) index = blocks.Count;
                    index = Math.Max(0, Math.Min(blocks.Count - 1, index + (e.KeyCode == Keys.Down ? 1 : -1)));
                    Pick(blocks[index].Item);
                    ScrollToOffset(blocks[index].LabelTop - HourHeight / 2f);
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home) { ScrollToMinute(0); e.Handled = true; }
            else if (e.KeyCode == Keys.End) { ScrollToOffset(AutoScrollMinSize.Height); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && current != null && EditRequested != null) { EditRequested(current.Id); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && current != null && DeleteRequested != null) { DeleteRequested(current.Id); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
    }
}
