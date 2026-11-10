using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopAgenda
{
    public sealed class Todo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public int Priority { get; set; }
        public bool Done { get; set; }
        public string CreatedAt { get; set; }
        public string CompletedAt { get; set; }
        public Todo() { Id = Guid.NewGuid().ToString("N"); Notes = ""; CreatedAt = Clock.Format(DateTime.Now); }
    }

    public sealed class Schedule
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Notes { get; set; }
        public string TodoId { get; set; }
        public int AdvanceMinutes { get; set; }
        public bool Strong { get; set; }
        public string LastAlert { get; set; }
        public string SnoozeUntil { get; set; }
        public bool AlertsStopped { get; set; }
        public bool Done { get; set; }
        public string CompletedAt { get; set; }
        public Schedule() { Id = Guid.NewGuid().ToString("N"); Notes = ""; AdvanceMinutes = 10; }
        public DateTime StartTime { get { return Clock.Parse(Start); } }
        public DateTime EndTime { get { return Clock.Parse(End); } }
        public void ResetReminder() { LastAlert = null; SnoozeUntil = null; AlertsStopped = false; }
    }

    public sealed class AgendaData
    {
        public int Version { get; set; }
        public List<Schedule> Schedules { get; set; }
        public List<Todo> Todos { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool Sound { get; set; }
        public bool RunAtStartup { get; set; }
        public bool ShowCompleted { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public AgendaData()
        {
            Version = 2; Schedules = new List<Schedule>(); Todos = new List<Todo>();
            AlwaysOnTop = true; Sound = true; RunAtStartup = true; ShowCompleted = true;
            X = -99999; Y = -99999;
        }
    }

    public sealed class LegacyData
    {
        public int Version { get; set; }
        public List<LegacyItem> Items { get; set; }
        public bool AlwaysOnTop { get; set; }
    }
    public sealed class LegacyItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Start { get; set; }
        public string Notes { get; set; }
        public int AdvanceMinutes { get; set; }
        public bool Strong { get; set; }
        public bool Done { get; set; }
        public string LastAlert { get; set; }
        public string SnoozeUntil { get; set; }
        public bool AlertsStopped { get; set; }
    }

    public static class Clock
    {
        public static string Format(DateTime value) { return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture); }
        public static DateTime Parse(string value)
        {
            DateTime result;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                throw new InvalidDataException("日期时间格式不正确。");
            return DateTime.SpecifyKind(result, DateTimeKind.Local);
        }
    }
    public static class Json
    {
        public static string Write(object value) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(value); }
        public static T Read<T>(string value) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Deserialize<T>(value); }
        public static T Copy<T>(T value) { return Read<T>(Write(value)); }
    }

    public static class Rules
    {
        public static readonly string[] Priorities = { "重要紧急", "紧急但不重要", "重要但不紧急", "不紧急也不重要" };
        public static bool Overlaps(Schedule a, Schedule b) { return a.StartTime < b.EndTime && b.StartTime < a.EndTime; }
        public static Schedule Conflict(AgendaData data, Schedule proposed)
        { return data.Schedules.Where(s => s.Id != proposed.Id && Overlaps(s, proposed)).OrderBy(s => s.StartTime).FirstOrDefault(); }
        public static bool OccursOn(Schedule item, DateTime day)
        { return item.StartTime < day.Date.AddDays(1) && item.EndTime > day.Date; }
        public static string Range(Schedule item, bool includeDate)
        {
            string start = item.StartTime.ToString(includeDate ? "M月d日 HH:mm" : "HH:mm");
            return start + " — " + item.EndTime.ToString(item.EndTime.Date != item.StartTime.Date ? "M月d日 HH:mm" : "HH:mm");
        }
        public static Schedule ScheduleFor(AgendaData data, string todoId) { return data.Schedules.FirstOrDefault(s => s.TodoId == todoId); }
        public static bool IsDue(Schedule item, DateTime now)
        {
            return !item.Done && ReminderDue(item, now);
        }
        private static bool ReminderDue(Schedule item, DateTime now)
        {
            if (item.AdvanceMinutes < 0 || item.AlertsStopped) return false;
            if (!String.IsNullOrEmpty(item.SnoozeUntil)) return now >= Clock.Parse(item.SnoozeUntil);
            if (String.IsNullOrEmpty(item.LastAlert)) return now >= item.StartTime.AddMinutes(-item.AdvanceMinutes);
            return item.Strong && now >= Clock.Parse(item.LastAlert).AddMinutes(5);
        }
        public static bool ScheduleComplete(AgendaData data, Schedule item)
        {
            Todo linked = data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
            return linked == null ? item.Done : linked.Done;
        }
        public static bool IsDue(AgendaData data, Schedule item, DateTime now)
        { return !ScheduleComplete(data, item) && ReminderDue(item, now); }
        public static bool IsOverdue(AgendaData data, Schedule item, DateTime now)
        { return item.EndTime <= now && !ScheduleComplete(data, item); }
        public static bool TodoOverdue(AgendaData data, Todo item, DateTime now)
        {
            Schedule linked = ScheduleFor(data, item.Id);
            return !item.Done && linked != null && linked.EndTime <= now;
        }
        public static List<Todo> SortTodos(AgendaData data, IEnumerable<Todo> tasks, DateTime now)
        {
            return tasks.Select(task => new { Task = task, Schedule = ScheduleFor(data, task.Id) })
                .OrderBy(row => row.Task.Done ? 3 : row.Schedule == null ? 1 : row.Schedule.EndTime <= now ? 0 : 2)
                .ThenBy(row => row.Schedule == null ? (String.IsNullOrEmpty(row.Task.CreatedAt) ? DateTime.MaxValue : Clock.Parse(row.Task.CreatedAt)) : row.Schedule.StartTime)
                .ThenBy(row => row.Task.CreatedAt, StringComparer.Ordinal)
                .ThenBy(row => row.Task.Title, StringComparer.CurrentCulture)
                .ThenBy(row => row.Task.Id, StringComparer.Ordinal)
                .Select(row => row.Task).ToList();
        }
        public static DateTime TodoTime(AgendaData data, Todo item)
        {
            Schedule linked = ScheduleFor(data, item.Id);
            return linked != null ? linked.EndTime : String.IsNullOrEmpty(item.CreatedAt) ? DateTime.MaxValue : Clock.Parse(item.CreatedAt);
        }
        private static bool ArchiveReady(bool done, string completedAt, DateTime taskTime, DateTime now)
        {
            if (!done || taskTime > now) return false;
            DateTime completed = String.IsNullOrEmpty(completedAt) ? taskTime : Clock.Parse(completedAt);
            return completed <= now.AddDays(-1);
        }
        public static bool ScheduleVisible(AgendaData data, Schedule item, DateTime day, DateTime now)
        {
            Todo linked = data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
            string completedAt = linked == null ? item.CompletedAt : linked.CompletedAt;
            return OccursOn(item, day) && (!ArchiveReady(ScheduleComplete(data, item), completedAt, item.EndTime, now) || day.Date < now.Date);
        }
        public static bool TodoVisible(AgendaData data, Todo item, DateTime day, DateTime now)
        {
            Schedule linked = ScheduleFor(data, item.Id);
            bool taskDay = linked == null ? !String.IsNullOrEmpty(item.CreatedAt) && Clock.Parse(item.CreatedAt).Date == day.Date : OccursOn(linked, day);
            bool completionDay = !String.IsNullOrEmpty(item.CompletedAt) && Clock.Parse(item.CompletedAt).Date == day.Date;
            return !ArchiveReady(item.Done, item.CompletedAt, TodoTime(data, item), now) || (day.Date < now.Date && (taskDay || completionDay));
        }
        public static DateTime NextViewChange(AgendaData data, DateTime now)
        {
            DateTime next = now.Date.AddDays(1);
            foreach (Schedule item in data.Schedules) if (item.EndTime > now && item.EndTime < next) next = item.EndTime;
            foreach (Todo item in data.Todos.Where(t => t.Done))
                next = EarlierExpiry(next, now, item.CompletedAt, TodoTime(data, item));
            foreach (Schedule item in data.Schedules.Where(s => s.Done && String.IsNullOrEmpty(s.TodoId)))
                next = EarlierExpiry(next, now, item.CompletedAt, item.EndTime);
            return next;
        }
        private static DateTime EarlierExpiry(DateTime next, DateTime now, string completedAt, DateTime fallback)
        {
            DateTime completed = String.IsNullOrEmpty(completedAt) ? fallback : Clock.Parse(completedAt);
            if (completed > DateTime.MaxValue.AddDays(-1)) return next;
            DateTime expires = completed.AddDays(1);
            return expires > now && expires < next ? expires : next;
        }
        private static void DetachSchedule(AgendaData data, Schedule item)
        {
            Todo linked = data.Todos.FirstOrDefault(t => t.Id == item.TodoId);
            if (linked != null) { item.Done = linked.Done; item.CompletedAt = linked.CompletedAt; }
            item.TodoId = null;
        }
        public static void CompleteTodo(AgendaData data, string id, bool done, DateTime now)
        {
            Todo item = data.Todos.Single(t => t.Id == id);
            item.Done = done; item.CompletedAt = done ? Clock.Format(now) : null;
            foreach (Schedule schedule in data.Schedules.Where(s => s.TodoId == id))
            {
                schedule.Done = done; schedule.CompletedAt = item.CompletedAt;
                if (!done) schedule.ResetReminder();
            }
        }
        public static void CompleteSchedule(AgendaData data, string id, bool done, DateTime now)
        {
            Schedule item = data.Schedules.Single(s => s.Id == id);
            if (!String.IsNullOrEmpty(item.TodoId)) { CompleteTodo(data, item.TodoId, done, now); return; }
            item.Done = done; item.CompletedAt = done ? Clock.Format(now) : null;
            if (!done) item.ResetReminder();
        }
        public static void LinkTodo(AgendaData data, string todoId, string scheduleId)
        {
            if (!data.Todos.Any(t => t.Id == todoId)) throw new InvalidDataException("关联的待办不存在。");
            Schedule target = String.IsNullOrEmpty(scheduleId) ? null : data.Schedules.Single(s => s.Id == scheduleId);
            if (target != null && !String.IsNullOrEmpty(target.TodoId) && target.TodoId != todoId)
                throw new InvalidDataException("该日程已经关联另一条待办。");
            foreach (Schedule item in data.Schedules.Where(s => s.TodoId == todoId)) DetachSchedule(data, item);
            if (target != null)
            {
                Todo linked = data.Todos.Single(t => t.Id == todoId);
                target.TodoId = todoId; target.Done = linked.Done; target.CompletedAt = linked.CompletedAt;
            }
        }
        public static void PutSchedule(AgendaData data, Schedule item)
        {
            if (item.EndTime <= item.StartTime) throw new InvalidDataException("结束时间必须晚于开始时间。");
            Schedule conflict = Conflict(data, item);
            if (conflict != null) throw new InvalidDataException("时间冲突，无法保存。\n\n已有日程：" + conflict.Title + "\n" + Range(conflict, true) + "\n\n请调整开始或结束时间。");
            if (!String.IsNullOrEmpty(item.TodoId))
            {
                if (!data.Todos.Any(t => t.Id == item.TodoId)) throw new InvalidDataException("关联的待办不存在。");
                if (data.Schedules.Any(s => s.Id != item.Id && s.TodoId == item.TodoId)) throw new InvalidDataException("这条待办已关联其他日程，请先解除原关联。");
                Todo linked = data.Todos.Single(t => t.Id == item.TodoId); item.Done = linked.Done; item.CompletedAt = linked.CompletedAt;
            }
            data.Schedules.RemoveAll(s => s.Id == item.Id); data.Schedules.Add(item);
        }
        public static void SaveSchedule(AgendaData data, Schedule schedule, Todo createdTodo)
        {
            AgendaData next = Json.Copy(data);
            Schedule proposed = Json.Copy(schedule), previous = next.Schedules.FirstOrDefault(s => s.Id == schedule.Id);
            if (previous != null && !String.IsNullOrEmpty(previous.TodoId) && String.IsNullOrEmpty(proposed.TodoId))
            { DetachSchedule(next, previous); proposed.Done = previous.Done; proposed.CompletedAt = previous.CompletedAt; }
            if (createdTodo != null)
            {
                if (next.Todos.Any(t => t.Id == createdTodo.Id)) throw new InvalidDataException("新待办的编号已经存在。");
                if (schedule.TodoId != createdTodo.Id) throw new InvalidDataException("新待办必须与本次日程关联。");
                next.Todos.Add(Json.Copy(createdTodo));
            }
            PutSchedule(next, proposed); Validate(next);
            data.Schedules = next.Schedules; data.Todos = next.Todos;
        }
        public static void SaveTodo(AgendaData data, Todo todo, string scheduleId, Schedule createdSchedule)
        {
            AgendaData next = Json.Copy(data);
            next.Todos.RemoveAll(t => t.Id == todo.Id); next.Todos.Add(Json.Copy(todo));
            if (createdSchedule != null)
            {
                if (next.Schedules.Any(s => s.Id == createdSchedule.Id)) throw new InvalidDataException("新日程的编号已经存在。");
                if (createdSchedule.TodoId != todo.Id) throw new InvalidDataException("新日程必须与本次待办关联。");
                LinkTodo(next, todo.Id, null); PutSchedule(next, Json.Copy(createdSchedule));
            }
            else LinkTodo(next, todo.Id, scheduleId);
            Validate(next); data.Schedules = next.Schedules; data.Todos = next.Todos;
        }
        public static AgendaData PreparePairEdit(AgendaData data, Schedule editedSchedule, Todo editedTodo)
        {
            AgendaData next = Json.Copy(data);
            Schedule schedule = next.Schedules.SingleOrDefault(s => s.Id == editedSchedule.Id);
            Todo todo = next.Todos.SingleOrDefault(t => t.Id == editedTodo.Id);
            if (schedule == null || todo == null || schedule.TodoId != todo.Id || editedSchedule.TodoId != todo.Id)
                throw new InvalidDataException("关联已更改，请重新打开编辑窗口。");
            todo.Title = editedTodo.Title; todo.Notes = editedTodo.Notes; todo.Priority = editedTodo.Priority;
            bool resetReminder = schedule.Start != editedSchedule.Start || schedule.AdvanceMinutes != editedSchedule.AdvanceMinutes || schedule.Strong != editedSchedule.Strong;
            schedule.Title = editedSchedule.Title; schedule.Notes = editedSchedule.Notes;
            schedule.Start = editedSchedule.Start; schedule.End = editedSchedule.End;
            schedule.AdvanceMinutes = editedSchedule.AdvanceMinutes; schedule.Strong = editedSchedule.Strong;
            if (resetReminder) schedule.ResetReminder();
            PutSchedule(next, schedule); Validate(next);
            return next;
        }
        public static void DeleteTodo(AgendaData data, string id)
        { foreach (Schedule schedule in data.Schedules.Where(s => s.TodoId == id)) DetachSchedule(data, schedule); data.Todos.RemoveAll(t => t.Id == id); }
        public static void DeleteSchedule(AgendaData data, string id) { data.Schedules.RemoveAll(s => s.Id == id); }
        public static void ApplyTodoBatch(AgendaData data, IEnumerable<string> todoIds, string action, int priority)
        {
            HashSet<string> ids = new HashSet<string>(todoIds);
            if (ids.Count == 0) throw new InvalidDataException("请先勾选要处理的待办。");
            if (ids.Any(id => !data.Todos.Any(t => t.Id == id))) throw new InvalidDataException("部分待办已不存在，请重新选择。");
            if (action != "complete" && action != "restore" && action != "priority" && action != "delete") throw new InvalidDataException("无法识别批量操作。");
            if (action == "priority" && (priority < 0 || priority > 3)) throw new InvalidDataException("请选择有效的优先级。");
            AgendaData next = Json.Copy(data); DateTime now = DateTime.Now;
            foreach (string id in ids)
            {
                if (action == "delete") DeleteTodo(next, id);
                else if (action == "priority") next.Todos.Single(t => t.Id == id).Priority = priority;
                else CompleteTodo(next, id, action == "complete", now);
            }
            Validate(next); data.Todos = next.Todos; data.Schedules = next.Schedules;
        }

        public static void Validate(AgendaData data)
        {
            if (data == null || data.Version != 2 || data.Schedules == null || data.Todos == null) throw new InvalidDataException("无法识别日程与待办数据。");
            HashSet<string> todos = new HashSet<string>();
            foreach (Todo item in data.Todos)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Id) || String.IsNullOrWhiteSpace(item.Title) || !todos.Add(item.Id) || item.Priority < 0 || item.Priority > 3)
                    throw new InvalidDataException("待办内容或优先级不正确。");
                if (!String.IsNullOrEmpty(item.CreatedAt)) Clock.Parse(item.CreatedAt);
                if (!String.IsNullOrEmpty(item.CompletedAt)) Clock.Parse(item.CompletedAt);
            }
            HashSet<string> ids = new HashSet<string>(); HashSet<string> linked = new HashSet<string>();
            List<Schedule> sorted = new List<Schedule>();
            foreach (Schedule item in data.Schedules)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Id) || String.IsNullOrWhiteSpace(item.Title) || !ids.Add(item.Id)) throw new InvalidDataException("日程内容不完整或编号重复。");
                if (item.EndTime <= item.StartTime) throw new InvalidDataException("日程结束时间必须晚于开始时间。");
                if (item.AdvanceMinutes < -1 || item.AdvanceMinutes > 525600) throw new InvalidDataException("提醒时间超出范围。");
                if (!String.IsNullOrEmpty(item.TodoId) && (!todos.Contains(item.TodoId) || !linked.Add(item.TodoId))) throw new InvalidDataException("日程和待办必须一对一关联。");
                if (!String.IsNullOrEmpty(item.LastAlert)) Clock.Parse(item.LastAlert);
                if (!String.IsNullOrEmpty(item.SnoozeUntil)) Clock.Parse(item.SnoozeUntil);
                if (!String.IsNullOrEmpty(item.CompletedAt)) Clock.Parse(item.CompletedAt);
                sorted.Add(item);
            }
            sorted.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            for (int i = 1; i < sorted.Count; i++) if (Overlaps(sorted[i - 1], sorted[i])) throw new InvalidDataException("日程时间冲突：" + sorted[i - 1].Title + " / " + sorted[i].Title);
        }
    }

    public static class TimelineMath
    {
        public static bool Slice(Schedule schedule, DateTime day, out double startMinute, out double endMinute)
        {
            startMinute = Math.Max(0, (schedule.StartTime - day.Date).TotalMinutes);
            endMinute = Math.Min(1440, (schedule.EndTime - day.Date).TotalMinutes);
            return startMinute < endMinute && startMinute < 1440 && endMinute > 0;
        }
        public static int SnapMinute(double minute) { return Math.Max(0, Math.Min(1425, (int)Math.Floor(minute / 15.0) * 15)); }
    }

    public sealed class Store
    {
        public string DirectoryPath { get; private set; }
        public string FilePath { get { return Path.Combine(DirectoryPath, "agenda.json"); } }
        public string Notice { get; private set; }
        public Store(string directory) { DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath); }
        private AgendaData Decode(string path, out bool migrated)
        {
            string text = File.ReadAllText(path, Encoding.UTF8); Dictionary<string, object> root = Json.Read<Dictionary<string, object>>(text);
            migrated = root != null && root.ContainsKey("Version") && Convert.ToInt32(root["Version"]) == 1;
            if (!migrated) { AgendaData result = Json.Read<AgendaData>(text); Rules.Validate(result); return result; }
            LegacyData legacy = Json.Read<LegacyData>(text);
            if (legacy.Items == null) throw new InvalidDataException("旧版数据缺少事项列表。");
            AgendaData data = new AgendaData { AlwaysOnTop = legacy.AlwaysOnTop };
            int unplaced = 0;
            foreach (LegacyItem old in legacy.Items.OrderBy(i => Clock.Parse(i.Start)))
            {
                Todo todo = new Todo { Id = String.IsNullOrEmpty(old.Id) ? Guid.NewGuid().ToString("N") : old.Id, Title = old.Title, Notes = old.Notes, Priority = 2, Done = old.Done, CompletedAt = old.Done ? Clock.Format(DateTime.Now) : null };
                data.Todos.Add(todo);
                Schedule schedule = new Schedule { Id = Guid.NewGuid().ToString("N"), Title = old.Title, Start = old.Start, End = Clock.Format(Clock.Parse(old.Start).AddMinutes(30)), Notes = old.Notes, TodoId = todo.Id, AdvanceMinutes = old.AdvanceMinutes, Strong = old.Strong, LastAlert = old.LastAlert, SnoozeUntil = old.SnoozeUntil, AlertsStopped = old.AlertsStopped || old.Done };
                if (Rules.Conflict(data, schedule) == null) data.Schedules.Add(schedule);
                else { todo.Notes = (todo.Notes ?? "") + "\r\n[旧版时间待重新安排] " + old.Start + "；升级时为避免时间冲突，已保留为待办。"; unplaced++; }
            }
            Rules.Validate(data);
            Notice = legacy.Items.Count == 0 ? null : "已升级旧版数据：原事项保留为待办，并关联原时间的30分钟日程。" + (unplaced > 0 ? "\n其中 " + unplaced + " 项原时间重叠，已保留为待办，请重新安排日程。" : "");
            return data;
        }
        public AgendaData Load()
        {
            if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new AgendaData();
            bool migrated; AgendaData data;
            try { data = Decode(FilePath, out migrated); }
            catch (Exception ex)
            {
                if (!File.Exists(FilePath + ".bak")) throw new InvalidDataException("无法读取日程文件，原数据已保留，程序未用空数据覆盖。\n" + FilePath, ex);
                data = Decode(FilePath + ".bak", out migrated);
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                File.Copy(FilePath + ".bak", FilePath, true); Notice = "已恢复上一版备份，异常原文件已保留。";
            }
            if (migrated)
            {
                string archive = Path.Combine(DirectoryPath, "agenda-before-v2-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.Copy(FilePath, archive); Save(data);
            }
            return data;
        }
        public void Save(AgendaData data)
        {
            Rules.Validate(data); string temp = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(Json.Write(data)); writer.Flush(); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true); else File.Move(temp, FilePath);
        }
        public AgendaData ReadBackup(string path) { bool migrated; return Decode(path, out migrated); }
    }
}
