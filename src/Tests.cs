using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DesktopAgenda
{
    internal static class Tests
    {
        private static readonly List<string> results = new List<string>();
        private static DateTime At(int hour, int minute) { return new DateTime(2026, 11, 6, hour, minute, 0); }
        private static Schedule Event(string title, DateTime start, DateTime end, string todo)
        { return new Schedule { Title = title, Start = Clock.Format(start), End = Clock.Format(end), TodoId = todo }; }
        private static void Check(bool success, string message) { if (!success) throw new Exception("FAIL: " + message); results.Add("PASS: " + message); }
        private static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        public static int Run(string output)
        {
            try
            {
                AgendaData data = new AgendaData();
                Check(data.Sound && data.RunAtStartup && data.ShowCompleted, "Defaults enable sound, startup and completed-task visibility");
                Check(Rules.Priorities.Length == 4 && Rules.Priorities[1] == "紧急但不重要" && Rules.Priorities[2] == "重要但不紧急", "Four priorities match the requested order");
                Todo report = new Todo { Title = "完成报告", Priority = 0 }, call = new Todo { Title = "沟通", Priority = 1 };
                data.Todos.Add(report); data.Todos.Add(call);
                Schedule lesson = Event("上课", At(9, 0), At(10, 0), null); Rules.PutSchedule(data, lesson);
                Schedule work = Event("写报告", At(10, 0), At(11, 0), report.Id); Rules.PutSchedule(data, work);
                Check(data.Schedules.Count == 2, "Adjacent schedules can touch without conflict");
                Check(String.IsNullOrEmpty(lesson.TodoId), "A schedule may exist without a todo");
                Check(Rules.ScheduleFor(data, call.Id) == null, "A todo may exist without a schedule");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("重叠", At(9, 30), At(10, 30), null)); }), "Partial overlap is rejected");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("包含", At(8, 0), At(12, 0), null)); }), "Containing overlap is rejected");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("被包含", At(9, 10), At(9, 20), null)); }), "Contained overlap is rejected");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("相同开始", At(9, 0), At(9, 5), null)); }), "Equal start times are rejected");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("零时长", At(12, 0), At(12, 0), null)); }), "Zero duration is rejected");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("倒序", At(13, 0), At(12, 0), null)); }), "Reversed duration is rejected");
                Schedule unchanged = Json.Copy(work); unchanged.Title = "报告工作时段"; Rules.PutSchedule(data, unchanged);
                Check(data.Schedules.Count == 2 && data.Schedules.Any(s => s.Title == "报告工作时段"), "Editing excludes the same schedule from conflict checks");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("第二时段", At(13, 0), At(14, 0), report.Id)); }), "One todo cannot be linked to two schedules");
                Rules.LinkTodo(data, report.Id, lesson.Id);
                Check(lesson.TodoId == report.Id && Rules.ScheduleFor(data, report.Id).Id == lesson.Id && String.IsNullOrEmpty(unchanged.TodoId), "Relinking from todo moves the association atomically");
                Check(Reject(delegate { Rules.LinkTodo(data, call.Id, lesson.Id); }), "A schedule already linked to a different todo rejects reassignment");
                Check(lesson.TodoId == report.Id, "Rejected relinking preserves the original association");
                Rules.LinkTodo(data, report.Id, null); Check(Rules.ScheduleFor(data, report.Id) == null, "Unlinking retains both records");
                Rules.LinkTodo(data, report.Id, work.Id); Rules.CompleteTodo(data, report.Id, true, At(10, 25));
                Check(report.Done && report.CompletedAt == Clock.Format(At(10, 25)), "Completion records state and timestamp");
                Check(data.Schedules.Count == 2 && Rules.ScheduleFor(data, report.Id) != null, "Completing a todo retains its linked schedule");
                Check(Reject(delegate { Rules.PutSchedule(data, Event("完成后仍冲突", At(10, 15), At(10, 30), null)); }), "Completing a todo does not free occupied schedule time");
                Rules.CompleteTodo(data, report.Id, false, At(10, 26)); Check(!report.Done && report.CompletedAt == null, "Undo completion restores the todo");
                AgendaData deleted = Json.Copy(data); Rules.DeleteSchedule(deleted, work.Id);
                Check(deleted.Todos.Count == 2 && Rules.ScheduleFor(deleted, report.Id) == null, "Deleting schedule keeps linked todo and removes association");
                deleted = Json.Copy(data); Rules.DeleteTodo(deleted, report.Id);
                Check(deleted.Schedules.Count == 2 && deleted.Schedules.All(s => String.IsNullOrEmpty(s.TodoId)) && deleted.Todos.Count == 1, "Deleting todo keeps schedules and removes dangling links");
                Schedule overnight = Event("跨夜", At(23, 0), At(1, 0).AddDays(1), null);
                Check(Rules.OccursOn(overnight, At(0, 0)) && Rules.OccursOn(overnight, At(0, 0).AddDays(1)), "Overnight schedule appears on both calendar dates");
                Schedule midnight = Event("到午夜", At(22, 0), At(0, 0).AddDays(1), null);
                Check(!Rules.OccursOn(midnight, At(0, 0).AddDays(1)), "An end at midnight does not occupy the following day");
                Rules.PutSchedule(data, overnight);
                Check(Reject(delegate { Rules.PutSchedule(data, Event("跨日冲突", At(0, 30).AddDays(1), At(2, 0).AddDays(1), null)); }), "Conflict checks work across dates");
                Schedule reminder = Event("自定义37分钟", At(15, 0), At(16, 0), null); reminder.AdvanceMinutes = 37;
                Check(!Rules.IsDue(reminder, At(14, 22)), "Custom reminder does not fire early");
                Check(Rules.IsDue(reminder, At(14, 23)), "Custom reminder fires at exact minute boundary");
                Check(Rules.IsDue(reminder, At(18, 0)), "Unseen reminders catch up after restart or sleep");
                reminder.LastAlert = Clock.Format(At(14, 23)); Check(!Rules.IsDue(reminder, At(18, 0)), "Normal reminder is shown only once");
                reminder.Strong = true; Check(!Rules.IsDue(reminder, At(14, 27)) && Rules.IsDue(reminder, At(14, 28)), "Strong reminder repeats after five minutes");
                reminder.SnoozeUntil = Clock.Format(At(14, 43)); Check(!Rules.IsDue(reminder, At(14, 42)) && Rules.IsDue(reminder, At(14, 43)), "Custom snooze overrides repeat interval and fires on time");
                reminder.AlertsStopped = true; Check(!Rules.IsDue(reminder, At(18, 0)), "Acknowledging stops further reminders without deleting schedule");
                reminder.ResetReminder(); Check(String.IsNullOrEmpty(reminder.LastAlert) && String.IsNullOrEmpty(reminder.SnoozeUntil) && !reminder.AlertsStopped, "Rescheduling resets reminder state");
                reminder.AdvanceMinutes = -1; Check(!Rules.IsDue(reminder, At(18, 0)), "Disabled reminders do not fire");
                Rules.Validate(data);
                AgendaData paired = new AgendaData();
                Todo pairedTodo = new Todo { Title = "待办同时建日程", Priority = 2 };
                Schedule pairedSchedule = Event("配套日程", At(15, 0), At(16, 0), pairedTodo.Id); pairedSchedule.AdvanceMinutes = 37;
                Rules.SaveTodo(paired, pairedTodo, pairedSchedule.Id, pairedSchedule);
                Check(paired.Todos.Count == 1 && paired.Schedules.Count == 1 && Rules.ScheduleFor(paired, pairedTodo.Id).Id == pairedSchedule.Id, "Creating a todo and schedule saves both with one-to-one association");
                Check(paired.Schedules[0].AdvanceMinutes == 37 && paired.Todos[0].Priority == 2, "Paired creation retains custom reminder and priority");
                Todo fromSchedule = new Todo { Title = "日程同时建待办", Priority = 1 };
                Schedule scheduleFirst = Event("另一日程", At(16, 0), At(17, 0), fromSchedule.Id);
                Rules.SaveSchedule(paired, scheduleFirst, fromSchedule);
                Check(paired.Todos.Count == 2 && paired.Schedules.Count == 2 && Rules.ScheduleFor(paired, fromSchedule.Id).Id == scheduleFirst.Id, "Creating a schedule and todo saves both with one-to-one association");
                string pairedBefore = Json.Write(paired);
                Todo conflictingTodo = new Todo { Title = "不应残留待办", Priority = 0 };
                Schedule conflictingPair = Event("冲突配套日程", At(15, 30), At(16, 30), conflictingTodo.Id);
                Check(Reject(delegate { Rules.SaveTodo(paired, conflictingTodo, conflictingPair.Id, conflictingPair); }) && Json.Write(paired) == pairedBefore, "Conflict during todo-first creation leaves neither orphan nor partial update");
                Check(Reject(delegate { Rules.SaveSchedule(paired, conflictingPair, conflictingTodo); }) && Json.Write(paired) == pairedBefore, "Conflict during schedule-first creation leaves neither orphan nor partial update");
                Todo wrongPriority = new Todo { Title = "错误优先级", Priority = 4 };
                Schedule invalidPair = Event("应整体拒绝", At(17, 0), At(18, 0), wrongPriority.Id);
                Check(Reject(delegate { Rules.SaveSchedule(paired, invalidPair, wrongPriority); }) && Json.Write(paired) == pairedBefore, "Invalid paired todo rejects the complete schedule-first operation");
                Todo editedTodo = Json.Copy(paired.Todos[0]); editedTodo.Title = "修改名称且新安排";
                Schedule replacedLink = Event("新安排", At(18, 0), At(19, 0), editedTodo.Id);
                Rules.SaveTodo(paired, editedTodo, replacedLink.Id, replacedLink);
                Check(paired.Schedules.Count == 3 && Rules.ScheduleFor(paired, editedTodo.Id).Id == replacedLink.Id && String.IsNullOrEmpty(paired.Schedules.Single(s => s.Id == pairedSchedule.Id).TodoId), "Creating a new schedule while editing todo preserves and unlinks the old schedule");
                pairedBefore = Json.Write(paired);
                Check(Reject(delegate { Rules.SaveTodo(paired, editedTodo, replacedLink.Id, replacedLink); }) && Json.Write(paired) == pairedBefore, "Paired creation cannot silently replace an existing schedule");
                Check(Reject(delegate { Rules.SaveSchedule(paired, scheduleFirst, fromSchedule); }) && Json.Write(paired) == pairedBefore, "Paired creation cannot overwrite an existing todo");
                Schedule standalone = Event("不关联的日程", At(19, 0), At(20, 0), null); Rules.SaveSchedule(paired, standalone, null);
                Todo standaloneTodo = new Todo { Title = "不关联的待办" }; Rules.SaveTodo(paired, standaloneTodo, null, null);
                Check(String.IsNullOrEmpty(paired.Schedules.Single(s => s.Id == standalone.Id).TodoId) && Rules.ScheduleFor(paired, standaloneTodo.Id) == null, "Both editors still allow standalone creation");
                AgendaData invalid = Json.Copy(data); invalid.Schedules[0].TodoId = "missing"; Check(Reject(delegate { Rules.Validate(invalid); }), "Dangling task association is rejected");
                double sliceStart, sliceEnd;
                Check(TimelineMath.Slice(work, At(0, 0), out sliceStart, out sliceEnd) && sliceStart == 600 && sliceEnd == 660, "Timeline preserves the exact position and duration of an ordinary schedule");
                Check(TimelineMath.Slice(overnight, At(0, 0), out sliceStart, out sliceEnd) && sliceStart == 1380 && sliceEnd == 1440, "Overnight block is clipped at the first day's midnight");
                Check(TimelineMath.Slice(overnight, At(0, 0).AddDays(1), out sliceStart, out sliceEnd) && sliceStart == 0 && sliceEnd == 60, "Overnight block resumes at the following day's start");
                Check(!TimelineMath.Slice(midnight, At(0, 0).AddDays(1), out sliceStart, out sliceEnd), "A midnight end does not create a phantom timeline block");
                Check(!TimelineMath.Slice(work, At(0, 0).AddDays(1), out sliceStart, out sliceEnd), "Other dates' schedules stay off the current timeline");
                Check(TimelineMath.SnapMinute(607) == 600 && TimelineMath.SnapMinute(-5) == 0 && TimelineMath.SnapMinute(1450) == 1425, "Blank-space creation snaps to a valid 15-minute slot within the selected day");
                AgendaData batch = Json.Copy(paired); string[] batchIds = { pairedTodo.Id, fromSchedule.Id };
                Rules.ApplyTodoBatch(batch, batchIds, "complete", 0);
                Check(batch.Todos.Where(t => batchIds.Contains(t.Id)).All(t => t.Done && !String.IsNullOrEmpty(t.CompletedAt)) && !batch.Todos.Single(t => t.Id == standaloneTodo.Id).Done, "Batch completion affects only selected tasks and retains completion history");
                Check(batch.Schedules.Count == paired.Schedules.Count && batch.Schedules.Count(s => !String.IsNullOrEmpty(s.TodoId)) == paired.Schedules.Count(s => !String.IsNullOrEmpty(s.TodoId)), "Batch completion retains every schedule and association");
                Rules.ApplyTodoBatch(batch, batchIds, "restore", 0); Check(batch.Todos.Where(t => batchIds.Contains(t.Id)).All(t => !t.Done && t.CompletedAt == null), "Batch restore removes completion marks without deleting tasks");
                Rules.ApplyTodoBatch(batch, batchIds, "priority", 3); Check(batch.Todos.Where(t => batchIds.Contains(t.Id)).All(t => t.Priority == 3), "Batch priority moves the selected tasks to the requested quadrant");
                string batchBefore = Json.Write(batch);
                Check(Reject(delegate { Rules.ApplyTodoBatch(batch, new[] { pairedTodo.Id, "missing" }, "delete", 0); }) && Json.Write(batch) == batchBefore, "An invalid batch selection leaves all data unchanged");
                Check(Reject(delegate { Rules.ApplyTodoBatch(batch, batchIds, "priority", 4); }) && Json.Write(batch) == batchBefore, "Invalid batch priority is rejected without partial changes");
                Check(Reject(delegate { Rules.ApplyTodoBatch(batch, new string[0], "complete", 0); }) && Json.Write(batch) == batchBefore, "Empty batch actions cannot change data");
                Rules.ApplyTodoBatch(batch, batchIds, "delete", 0);
                Check(batch.Todos.All(t => !batchIds.Contains(t.Id)) && batch.Schedules.Count == paired.Schedules.Count && batch.Schedules.All(s => !batchIds.Contains(s.TodoId)), "Batch deletion retains schedules and removes only the selected todos and links");
                invalid = Json.Copy(data); invalid.Todos[0].Priority = 4; Check(Reject(delegate { Rules.Validate(invalid); }), "Invalid priority is rejected");
                invalid = Json.Copy(data); invalid.Schedules.Add(Event("存盘冲突", At(9, 0), At(12, 0), null)); Check(Reject(delegate { Rules.Validate(invalid); }), "Persistence validation also rejects conflicts");
                Rules.CompleteTodo(data, report.Id, true, At(10, 25));
                string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "v2-" + Guid.NewGuid().ToString("N")); Store store = new Store(temp); store.Save(data);
                AgendaData loaded = store.Load(); Check(loaded.Todos.Single(t => t.Id == report.Id).Done && loaded.Schedules.Count == 3, "Saving and reopening preserves completed todos and schedules");
                loaded.Todos[0].Notes = "中文备注\r\n第二行"; store.Save(loaded); Check(store.Load().Todos[0].Notes == "中文备注\r\n第二行", "Unicode multiline notes round-trip correctly");
                Check(File.Exists(store.FilePath + ".bak"), "Previous revision is retained as backup");
                string before = File.ReadAllText(store.FilePath); Check(Reject(delegate { store.Save(invalid); }) && File.ReadAllText(store.FilePath) == before, "Rejected saves do not overwrite existing data");
                File.WriteAllText(store.FilePath, "broken"); Store recovery = new Store(temp); AgendaData recovered = recovery.Load();
                Check(recovered.Todos.Single(t => t.Id == report.Id).Done && !String.IsNullOrEmpty(recovery.Notice), "Corruption recovers a valid backup with a notice");
                Check(Directory.GetFiles(temp, "*.unreadable-*").Length == 1, "Corrupted original is preserved");
                string noBackup = Path.Combine(temp, "no-backup"); Directory.CreateDirectory(noBackup); File.WriteAllText(Path.Combine(noBackup, "agenda.json"), "broken");
                Check(Reject(delegate { new Store(noBackup).Load(); }) && File.ReadAllText(Path.Combine(noBackup, "agenda.json")) == "broken", "Unrecoverable files remain untouched");
                string migrationDir = Path.Combine(temp, "migration"); Directory.CreateDirectory(migrationDir);
                LegacyData old = new LegacyData { Version = 1, AlwaysOnTop = true, Items = new List<LegacyItem> {
                    new LegacyItem { Id="old-a", Title="旧版已完成", Start=Clock.Format(At(9,0)), Done=true, AdvanceMinutes=10 },
                    new LegacyItem { Id="old-b", Title="旧版重叠", Start=Clock.Format(At(9,10)), Done=false, AdvanceMinutes=10 },
                    new LegacyItem { Id="old-c", Title="旧版未来", Start=Clock.Format(At(11,0)), Done=false, AdvanceMinutes=10 } } };
                File.WriteAllText(Path.Combine(migrationDir, "agenda.json"), Json.Write(old), Encoding.UTF8); Store migration = new Store(migrationDir); AgendaData upgraded = migration.Load();
                Check(upgraded.Version == 2 && upgraded.Todos.Count == 3 && upgraded.Schedules.Count == 2, "Migration preserves all legacy records and prevents inherited conflicts");
                Check(upgraded.Todos.Single(t => t.Id == "old-a").Done && upgraded.Todos.Single(t => t.Id == "old-b").Notes.Contains("待重新安排"), "Migration preserves completion and documents unplaced overlapping times");
                Check(upgraded.Sound && upgraded.RunAtStartup, "Migration applies requested sound and startup defaults");
                Check(Directory.GetFiles(migrationDir, "agenda-before-v2-*.json").Length == 1, "Original legacy data is archived before migration");
                Check(new Store(migrationDir).Load().Schedules.Count == 2, "Migrated data reopens without migrating again");
                byte[] wav = Chime.Wav(); Check(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "Built-in audible chime is a valid WAV");
                Check(wav.Length == BitConverter.ToInt32(wav, 4) + 8 && wav.Skip(44).Any(b => b != 0), "Chime contains non-silent PCM audio with a consistent length");
                int hour, minute;
                foreach (string input in new[] { "19:30", "19：30", "19.30", "1930", "19点30分" }) Check(TimeText.TryParse(input, out hour, out minute) && hour == 19 && minute == 30, "User's editable time input accepts " + input);
                Check(TimeText.TryParse("930", out hour, out minute) && hour == 9 && minute == 30 && TimeText.TryParse("9", out hour, out minute) && hour == 9 && minute == 0, "Compact and whole-hour time entry remains supported");
                Check(TimeText.TryParse("00:00", out hour, out minute) && hour == 0 && minute == 0 && TimeText.TryParse("23:59", out hour, out minute) && hour == 23 && minute == 59, "Time entry accepts valid day boundaries");
                Check(new[] { "", "24:00", "19:60", "-1:30", "+9:30", "19::30", "19:", ":9", "a:b", "12345" }.All(s => !TimeText.TryParse(s, out hour, out minute)), "Malformed, incomplete and out-of-range times are rejected");
                string appA = Path.Combine(temp, "app-a"), appB = Path.Combine(temp, "app-b");
                Check(DataLocation.Resolve(false, appA, null) == DataLocation.Resolve(false, appB, Path.Combine(temp, "empty")), "Production data location stays fixed across executable copies and data-dir arguments");
                Check(DataLocation.Resolve(true, appA, Path.Combine(temp, "testing")) == Path.GetFullPath(Path.Combine(temp, "testing")), "Test data remains isolated from the user's real records");
                string oldPath = Path.Combine(temp, "legacy-location"), stablePath = Path.Combine(temp, "stable-location"); Store oldStore = new Store(oldPath); oldStore.Save(data); oldStore.Save(data); string oldBytes = File.ReadAllText(oldStore.FilePath);
                Check(DataLocation.CopyLegacyIfMissing(stablePath, oldPath) && new Store(stablePath).Load().Todos.Count == data.Todos.Count && new Store(stablePath).Load().Schedules.Count == data.Schedules.Count, "First launch imports existing schedules and todos into the fixed data location");
                Check(File.ReadAllText(oldStore.FilePath) == oldBytes && File.Exists(Path.Combine(stablePath, "agenda.json.bak")), "Migration preserves the original files and the previous revision");
                Store stableStore = new Store(stablePath); AgendaData stableData = stableStore.Load(); stableData.Todos.Add(new Todo { Title = "固定目录中的新记录" }); stableStore.Save(stableData); string stableBytes = File.ReadAllText(stableStore.FilePath);
                Check(!DataLocation.CopyLegacyIfMissing(stablePath, oldPath) && File.ReadAllText(stableStore.FilePath) == stableBytes, "Later launches cannot overwrite fixed-location data with an older copy");
                File.WriteAllText(oldStore.FilePath, "broken"); string recoveredLocation = Path.Combine(temp, "recovered-location");
                Check(DataLocation.CopyLegacyIfMissing(recoveredLocation, oldPath) && new Store(recoveredLocation).Load().Todos.Count == data.Todos.Count && File.ReadAllText(oldStore.FilePath) == "broken", "Migration can read the legacy backup without changing the damaged original");
                string missingLocation = Path.Combine(temp, "no-existing-records"); Check(!DataLocation.CopyLegacyIfMissing(Path.Combine(temp, "not-created"), missingLocation), "No legacy data does not create an empty duplicate store");
                string token = "DesktopAgenda.Check." + Guid.NewGuid().ToString("N");
                using (InstanceLock primary = new InstanceLock(token))
                {
                    Check(primary.IsPrimary, "The first application instance owns the user-session lock");
                    using (InstanceLock secondary = new InstanceLock(token)) { Check(!secondary.IsPrimary, "A second launch cannot create another primary instance"); Check(primary.ReceiveSignal(1000), "A second launch signals the existing window even before its listener starts"); }
                }
                using (InstanceLock restarted = new InstanceLock(token)) Check(restarted.IsPrimary, "A full exit releases the lock so the saved program can be reopened");
                CheckArchiveAndOverdue();
                CheckTodoOrder();
                CheckPairEditing(temp);
                File.WriteAllText(output, String.Join(Environment.NewLine, results) + Environment.NewLine + "ALL " + results.Count + " CHECKS PASSED", new UTF8Encoding(false)); return 0;
            }
            catch (Exception ex) { File.WriteAllText(output, String.Join(Environment.NewLine, results) + Environment.NewLine + ex, new UTF8Encoding(false)); return 1; }
        }
        private static void CheckArchiveAndOverdue()
        {
            DateTime now = At(12, 0), yesterday = now.AddDays(-1);
            AgendaData data = new AgendaData();
            Todo undated = new Todo { Title = "无日程待办", CreatedAt = Clock.Format(now.AddDays(-4)) };
            data.Todos.Add(undated);
            Check(!Rules.TodoOverdue(data, undated, now), "An unscheduled todo never turns red, even long after creation");
            Check(data.Schedules.Count == 0 && Rules.TodoTime(data, undated) == now.AddDays(-4), "An unscheduled todo retains its creation time without creating a schedule");
            Rules.CompleteTodo(data, undated.Id, true, yesterday.AddHours(1));
            Check(Rules.TodoVisible(data, undated, now.Date, now), "A completed unscheduled todo remains visible for the first 24 hours");
            Check(Rules.TodoVisible(data, undated, now.Date, now.AddHours(1).AddSeconds(-1)), "The full 24-hour retention includes its final second");
            Check(!Rules.TodoVisible(data, undated, now.Date, now.AddHours(1)), "A completed unscheduled todo hides at the exact 24-hour boundary");
            Check(Rules.TodoVisible(data, undated, now.AddDays(-4).Date, now.AddHours(1)), "Selecting the creation date restores an archived unscheduled todo");
            Check(Rules.TodoVisible(data, undated, yesterday.Date, now.AddHours(1)), "The completion date also shows a previously archived todo");
            Check(!Rules.TodoVisible(data, undated, now.AddDays(-2).Date, now.AddHours(1)), "Unrelated historical dates do not reveal every archived todo");
            Check(Rules.NextViewChange(data, now) == now.AddHours(1), "The next 24-hour expiry refreshes automatically without a manual action");
            Rules.CompleteTodo(data, undated.Id, false, now);
            Check(Rules.TodoVisible(data, undated, now.Date, now) && !Rules.TodoOverdue(data, undated, now), "Restoring an archived unscheduled todo shows it again with ordinary text");

            Todo task = new Todo { Title = "关联待办", CreatedAt = Clock.Format(yesterday) }; data.Todos.Add(task);
            Schedule schedule = Event("关联日程", yesterday.AddHours(-3), yesterday.AddHours(-2), task.Id); Rules.PutSchedule(data, schedule);
            Check(!Rules.TodoOverdue(data, task, schedule.EndTime.AddSeconds(-1)) && !Rules.IsOverdue(data, schedule, schedule.EndTime.AddSeconds(-1)), "An unfinished schedule stays ordinary until its end time");
            Check(Rules.TodoOverdue(data, task, schedule.EndTime) && Rules.IsOverdue(data, schedule, schedule.EndTime), "An ended unfinished schedule and its todo both become overdue");
            Check(Rules.TodoVisible(data, task, now.Date, now), "Unfinished overdue todos remain visible across dates");
            Rules.CompleteSchedule(data, schedule.Id, true, yesterday);
            Check(task.Done && schedule.Done && task.CompletedAt == schedule.CompletedAt, "Completing a linked schedule also completes its todo with the same timestamp");
            Check(Rules.TodoVisible(data, task, yesterday.Date, now.AddSeconds(-1)) && !Rules.TodoOverdue(data, task, now), "Recent completed linked tasks stay struck out and never red");
            Check(!Rules.TodoVisible(data, task, now.Date, now), "A linked completed todo hides after 24 hours in the ordinary view");
            Check(Rules.TodoVisible(data, task, yesterday.Date, now) && Rules.ScheduleVisible(data, schedule, yesterday.Date, now), "Selecting the schedule date shows its completed timeline block and todo");
            Check(!Rules.ScheduleVisible(data, schedule, now.Date, now), "A historical schedule never occupies an unrelated day");
            Check(!Rules.IsDue(data, schedule, now), "Completed linked schedules do not send reminders");
            schedule.Done = false;
            Check(!Rules.IsDue(data, schedule, now), "Legacy linked records use the todo completion state even without a schedule completion flag");
            Rules.CompleteSchedule(data, schedule.Id, false, now);
            Check(!task.Done && !schedule.Done && Rules.TodoOverdue(data, task, now) && Rules.IsDue(data, schedule, now), "Restoring a linked schedule restores its overdue todo and reminder eligibility");
            Rules.CompleteTodo(data, task.Id, true, now);
            Rules.DeleteTodo(data, task.Id);
            Check(schedule.Done && String.IsNullOrEmpty(schedule.TodoId) && !Rules.IsDue(data, schedule, now), "Unlinking a completed todo preserves the schedule's completed state");

            Schedule alone = Event("独立日程", now.AddHours(-1), now.AddMinutes(-30), null); Rules.PutSchedule(data, alone);
            Rules.CompleteSchedule(data, alone.Id, true, now);
            Check(alone.Done && alone.CompletedAt == Clock.Format(now) && !Rules.IsDue(data, alone, now), "Independent schedules can be completed and stop reminders without creating todos");
            Check(Rules.ScheduleVisible(data, alone, now.Date, now.AddHours(23)), "A completed independent schedule remains on its day during the retention period");
            Check(Rules.ScheduleVisible(data, alone, now.Date, now.AddDays(1)), "A completed independent schedule is recoverable by selecting its historical date");
            Rules.CompleteSchedule(data, alone.Id, false, now);
            Check(!alone.Done && alone.CompletedAt == null && Rules.IsOverdue(data, alone, now), "An independent schedule can undo completion and become overdue again");

            Todo early = new Todo { Title = "提前完成", CreatedAt = Clock.Format(yesterday) }; data.Todos.Add(early);
            Schedule future = Event("将来日程", now.AddHours(3), now.AddHours(4), early.Id); Rules.PutSchedule(data, future);
            Rules.CompleteTodo(data, early.Id, true, now.AddDays(-2));
            Check(Rules.TodoVisible(data, early, now.Date, now) && Rules.ScheduleVisible(data, future, now.Date, now), "A future schedule remains visible until its time passes even if completed early");
            Check(!Rules.TodoVisible(data, early, now.Date, now.AddHours(4)) && !Rules.ScheduleVisible(data, future, now.Date, now.AddHours(4)), "An old completed schedule and its todo hide when the scheduled time finally ends");
            Check(data.Schedules.Count == 3 && data.Todos.Count == 2, "Visibility changes preserve all underlying records");
            AgendaData restored = Json.Read<AgendaData>(Json.Write(data)); Rules.Validate(restored);
            Check(restored.Schedules.Single(s => s.Id == future.Id).Done && restored.Todos.Single(t => t.Id == early.Id).Done, "Completion and historical visibility survive saving and reopening");
        }
        private static void CheckTodoOrder()
        {
            DateTime now = At(12, 0);
            AgendaData data = new AgendaData();
            Todo futureLater = new Todo { Title = "较晚日程", CreatedAt = Clock.Format(now.AddDays(-4)) };
            Todo undatedLater = new Todo { Title = "较晚创建的无日程待办", CreatedAt = Clock.Format(now.AddHours(-1)) };
            Todo completed = new Todo { Title = "已经完成的逾期日程", CreatedAt = Clock.Format(now.AddDays(-5)), Done = true, CompletedAt = Clock.Format(now) };
            Todo overdueLater = new Todo { Title = "今天逾期", CreatedAt = Clock.Format(now.AddDays(-3)) };
            Todo futureEarlier = new Todo { Title = "较早日程", CreatedAt = Clock.Format(now.AddHours(-1)) };
            Todo undatedEarlier = new Todo { Title = "较早创建的无日程待办", CreatedAt = Clock.Format(now.AddDays(-1)) };
            Todo overdueEarlier = new Todo { Title = "昨天逾期", CreatedAt = Clock.Format(now.AddHours(-2)) };
            Todo completedUndated = new Todo { Title = "已完成无日程待办", CreatedAt = Clock.Format(now.AddDays(-6)), Done = true, CompletedAt = Clock.Format(now) };
            data.Todos.AddRange(new[] { futureLater, undatedLater, completed, overdueLater, futureEarlier, undatedEarlier, overdueEarlier, completedUndated });
            Rules.PutSchedule(data, Event("昨天已完成", now.AddDays(-1).AddHours(-4), now.AddDays(-1).AddHours(-3), completed.Id));
            Rules.PutSchedule(data, Event("昨天逾期", now.AddDays(-1).AddHours(-2), now.AddDays(-1).AddHours(-1), overdueEarlier.Id));
            Rules.PutSchedule(data, Event("今天逾期", now.AddHours(-4), now.AddHours(-3), overdueLater.Id));
            Rules.PutSchedule(data, Event("较早日程", now.AddHours(1), now.AddHours(2), futureEarlier.Id));
            Rules.PutSchedule(data, Event("较晚日程", now.AddHours(2), now.AddHours(3), futureLater.Id));
            string original = Json.Write(data);
            List<Todo> ordered = Rules.SortTodos(data, data.Todos, now);
            Check(ordered.Take(6).Select(t => t.Id).SequenceEqual(new[] { overdueEarlier.Id, overdueLater.Id, undatedEarlier.Id, undatedLater.Id, futureEarlier.Id, futureLater.Id }), "Todo order puts overdue first, then unscheduled, then scheduled by start time regardless of creation order");
            Check(ordered.Skip(6).All(t => t.Done), "Completed tasks stay last even when their schedule is overdue or they have no schedule");
            Check(Json.Write(data) == original, "Sorting changes only the view, preserving every record and completion timestamp");
            Rules.CompleteTodo(data, overdueEarlier.Id, true, now);
            ordered = Rules.SortTodos(data, data.Todos, now);
            Check(ordered[0].Id == overdueLater.Id && ordered.Skip(5).All(t => t.Done), "Completing an overdue task moves it from the first group to the completed group");
            Rules.CompleteTodo(data, completed.Id, false, now);
            Check(Rules.SortTodos(data, data.Todos, now)[0].Id == completed.Id, "Undoing completion restores a past scheduled task to its chronological place at the front");
            ordered = Rules.SortTodos(data, data.Todos, now.AddHours(2));
            Check(ordered.IndexOf(futureEarlier) < ordered.IndexOf(undatedEarlier) && ordered.IndexOf(futureLater) > ordered.IndexOf(undatedLater), "A newly overdue task automatically moves before unscheduled tasks while remaining scheduled tasks stay below");
        }
        private static void CheckPairEditing(string temp)
        {
            AgendaData data = new AgendaData();
            Todo originalTodo = new Todo { Title = "待办原名称", Priority = 0, CreatedAt = Clock.Format(At(8, 0)), Done = true, CompletedAt = Clock.Format(At(10, 1)) };
            data.Todos.Add(originalTodo);
            Schedule originalSchedule = Event("日程原名称", At(9, 0), At(10, 0), originalTodo.Id);
            originalSchedule.LastAlert = Clock.Format(At(8, 50)); originalSchedule.AlertsStopped = true;
            Rules.PutSchedule(data, originalSchedule);
            Rules.PutSchedule(data, Event("占用时段", At(12, 0), At(13, 0), null));
            string before = Json.Write(data);

            Todo task = Json.Copy(originalTodo); task.Title = "修改待办"; task.Notes = "待办备注"; task.Priority = 2;
            task.Done = false; task.CompletedAt = null;
            Schedule item = Json.Copy(originalSchedule); item.Title = "修改日程"; item.Notes = "日程备注";
            AgendaData renamed = Rules.PreparePairEdit(data, item, task);
            Check(renamed.Todos[0].Title == task.Title && renamed.Todos[0].Priority == 2 && renamed.Todos[0].Notes == task.Notes && Rules.ScheduleFor(renamed, task.Id).Title == item.Title && Rules.ScheduleFor(renamed, task.Id).Notes == item.Notes, "The same edit saves different names, notes and todo priority for both records");
            Check(renamed.Todos[0].Done && renamed.Todos[0].CreatedAt == originalTodo.CreatedAt && renamed.Todos[0].CompletedAt == originalTodo.CompletedAt, "Pair editing preserves creation and completion history");
            Check(Rules.ScheduleFor(renamed, task.Id).LastAlert == originalSchedule.LastAlert && Rules.ScheduleFor(renamed, task.Id).AlertsStopped, "Renaming or changing priority does not reactivate an existing reminder");
            Check(Json.Write(data) == before, "Preparing edits or cancelling leaves both original records untouched");

            item.Start = Clock.Format(At(12, 15)); item.End = Clock.Format(At(12, 45));
            Check(Reject(delegate { Rules.PreparePairEdit(data, item, task); }) && Json.Write(data) == before, "A schedule conflict rejects both changes without altering the todo");
            item.Start = Clock.Format(At(10, 30)); item.End = Clock.Format(At(11, 30)); item.AdvanceMinutes = 35;
            task.Priority = 5;
            Check(Reject(delegate { Rules.PreparePairEdit(data, item, task); }) && Json.Write(data) == before, "An invalid todo rejects both changes without moving the schedule");
            task.Priority = 2;
            AgendaData edited = Rules.PreparePairEdit(data, item, task);
            Schedule moved = Rules.ScheduleFor(edited, task.Id);
            Check(moved.Id == originalSchedule.Id && moved.Start == item.Start && moved.End == item.End && moved.AdvanceMinutes == 35 && moved.LastAlert == null && !moved.AlertsStopped, "A changed time and reminder keep the existing association and recalculate reminder state");
            Schedule detached = Json.Copy(item); detached.TodoId = null;
            Check(Reject(delegate { Rules.PreparePairEdit(data, detached, task); }), "The combined editor cannot accidentally break the association");
            Store store = new Store(Path.Combine(temp, "pair-edit")); store.Save(data); store.Save(edited);
            AgendaData reopened = store.Load();
            Check(reopened.Todos.Count == 1 && reopened.Schedules.Count == 2 && reopened.Todos[0].Id == task.Id && reopened.Todos[0].Priority == 2 && Rules.ScheduleFor(reopened, task.Id).Start == item.Start, "Both edits persist together after reopening without adding duplicate records");
        }
    }
}
