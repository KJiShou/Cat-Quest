using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// Called by the single SQLite verification coordinator. All paths are supplied
// beneath its disposable output directory; this never opens the live save.
public static class SqliteTaskCommandVerification
{
    sealed class Clock : ITaskClock
    {
        public DateTime Now = new DateTime(2026, 11, 16, 12, 0, 0);
        public DateTime LocalNow => Now;
        public DateTime UtcNow => DateTime.SpecifyKind(Now.AddHours(-8), DateTimeKind.Utc);
    }
    sealed class Memory : IPlayerDataStore
    {
        public PlayerData Data { get; private set; }
        public bool fail;
        public Memory(PlayerData data) { Data = data; }
        public bool TrySave(PlayerData candidate, out string error) {
            error = fail ? "Injected write failure" : null;
            if (fail) return false;
            Data = candidate; return true;
        }
    }
    static string Canonical(PlayerData data)
    {
        var copy = PlayerDataStore.Copy(data);
        for (int i = 0; i < copy.history.Count; i++) copy.history[i].id = "completion-" + i;
        for (int i = 0; i < copy.focusSessions.Count; i++) copy.focusSessions[i].id = "focus-" + i;
        return JsonUtility.ToJson(copy);
    }
    public static async Task RunAsync(string folder, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var clock = new Clock();
        var initial = new PlayerData { experience = 100, fishCoins = 50, tickets = 2, tagsInitialized = true,
            tutorial = new TutorialProgress { version = 1, dismissed = true } };
        initial.tags.Add(new QuestTag { id = "tag-a", name = "Study", colorHex = TagColors.DefaultHex });
        initial.tasks.Add(new QuestTask { id = "daily", title = "Daily study", daily = true, scheduled = true,
            date = TaskService.DateKey(clock.Now), difficulty = TaskDifficulty.Normal });
        PlayerDataStore.Normalize(initial);
        var memory = new Memory(initial);
        var old = new TaskService(memory, clock);
        string json = Path.Combine(folder, "legacy.json"); File.WriteAllText(json, JsonUtility.ToJson(memory.Data));
        var repository = new SqlitePlayerRepository(Path.Combine(folder, "commands.sqlite3"), json);
        try {
            check((await repository.InitializeAsync()).Succeeded, "Task commands initialize SQLite");
            var service = new SqlitePlayerService(repository, clock);
            var initialized=await service.InitializeAsync();
            check(initialized.Succeeded, "Task commands initialize domain state: "+initialized.Error+" "+initialized.Message+" "+repository.LastVerificationDiagnostic);
            async Task Same(string name) {
                var actual = await repository.ReadAllForVerificationAsync();
                check(actual.Succeeded, name + ": database read succeeds");
                string expectedJson = Canonical(memory.Data), actualJson = Canonical(actual.Value);
                if (expectedJson != actualJson) {
                    File.WriteAllText(Path.Combine(folder, "expected.json"), expectedJson);
                    File.WriteAllText(Path.Combine(folder, "actual.json"), actualJson);
                }
                check(expectedJson == actualJson, name + ": identical to approved legacy domain result");
            }
            await Same("Initial state");
            var draft = new QuestTask { id = "todo", title = "Unicode 研究 🐈", description = "quotes ' % _", difficulty = TaskDifficulty.Hard,
                tagIds = new List<string> { "tag-a" }, subtasks = new List<QuestSubtask> { new QuestSubtask { id = "sub", title = "Read" } } };
            check(old.Upsert(draft, Array.Empty<string>(), out _) && (await service.UpsertAsync(draft, Array.Empty<string>())).Succeeded, "Create task"); await Same("Create");
            foreach(TaskTab tab in Enum.GetValues(typeof(TaskTab)))foreach(string search in new[]{"","STUDY","研究"}) {
                var expected=tab==TaskTab.History?new List<TaskView>():old.Query(tab,search,null,new[]{"tag-a"});
                if(tab==TaskTab.Todo)expected.AddRange(old.Query(TaskTab.Daily,search,null,new[]{"tag-a"}).Where(row=>!row.Completed));
                var actual=await service.QueryBrowseAsync(tab,search,null,new[]{"tag-a"});
                check(actual.Succeeded&&actual.Version==service.ReadVersion
                    &&actual.Value.rows.Select(JsonUtility.ToJson).SequenceEqual(expected.Select(JsonUtility.ToJson))
                    &&actual.Value.counts.SequenceEqual(Enumerable.Range(0,5).Select(i=>(TaskTab)i==TaskTab.Tags?old.TagCatalog.Count:
                        old.Query((TaskTab)i).Count+((TaskTab)i==TaskTab.Todo?old.Query(TaskTab.Daily).Count(row=>!row.Completed):0))),
                    "Background browse preserves exact rows, Unicode/tag filters, ordering, counts and revision: "+tab+"/"+search);
            }
            var detachedBrowse=await service.QueryBrowseAsync(TaskTab.Todo,"研究",null,null);
            detachedBrowse.Value.rows[0].task.title="Detached display edit";
            check((await service.GetTaskAsync("todo")).Value.title==draft.title,"Async browse rows cannot mutate committed tasks");
            check(!old.Complete("todo") && !(await service.CompleteAsync("todo")).Succeeded, "Incomplete subtask completion requires confirmation"); await Same("Rejected completion");
            check(old.SetSubtask("todo", "sub", true) && (await service.SetSubtaskAsync("todo", "sub", true)).Succeeded, "Complete subtask"); await Same("Subtask");
            memory.fail = true; repository.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected write failure"); };
            check(!old.Complete("todo") && !(await service.CompleteAsync("todo")).Succeeded, "Failed task completion does not settle rewards");
            memory.fail = false; repository.FaultPoint = null; await Same("Failed save rollback");
            check(old.Complete("todo") && (await service.CompleteAsync("todo")).Succeeded, "Retry task completion"); await Same("Retry completion");
            var beforeDuplicate = Canonical(memory.Data);
            var duplicates = await Task.WhenAll(service.CompleteAsync("todo"), service.CompleteAsync("todo"));
            check(duplicates.All(r => r.Succeeded) && old.Complete("todo"), "Concurrent duplicate completion succeeds without repeated rewards");
            await Same("Duplicate completion"); check(Canonical(memory.Data) == beforeDuplicate, "Legacy duplicate remains unchanged");
            var snapshot = (await repository.ReadAllForVerificationAsync()).Value;
            string oldReceipt = memory.Data.history.Single(h => h.taskId == "todo").id;
            string newReceipt = snapshot.history.Single(h => h.taskId == "todo").id;
            check(old.UndoCompletion(oldReceipt) && (await service.UndoCompletionAsync(newReceipt)).Succeeded, "Undo completion"); await Same("Undo retains paid allowance");
            check(old.Complete("todo") && (await service.CompleteAsync("todo")).Succeeded, "Recomplete once-paid occurrence"); await Same("Recomplete cannot pay twice");
            int ticketsBeforeFirstMission = service.Tickets;
            check(old.ClaimDailyGoal(DailyRewardGoal.FirstQuest) && (await service.ClaimDailyGoalAsync(DailyRewardGoal.FirstQuest)).Succeeded, "Claim first quest reward"); await Same("First goal");
            check(service.Tickets == ticketsBeforeFirstMission + 1 && service.TodayRewards.taskTickets == 1, "First mission awards one Ticket on claim");
            for (int i = 0; i < 18; i++) {
                var task = new QuestTask { id = "cap-" + i, title = "Cap " + i, difficulty = (TaskDifficulty)(i % 3) };
                check(old.Upsert(task, null, out _) && (await service.UpsertAsync(task, null)).Succeeded, "Create cap fixture " + i);
                check(old.Complete(task.id) && (await service.CompleteAsync(task.id)).Succeeded, "Complete at cap " + i);
            }
            await Same("All difficulties, cap boundary, level-up settlement");
            int ticketsBeforeThirdMission = service.Tickets;
            repository.FaultPoint = stage => { if (stage == "before-commit") throw new IOException("Injected mission claim failure"); };
            check(!(await service.ClaimDailyGoalAsync(DailyRewardGoal.ThreeQuests)).Succeeded
                && service.Tickets == ticketsBeforeThirdMission && !service.DailyGoalClaimed(DailyRewardGoal.ThreeQuests),
                "Failed third mission claim awards nothing");
            repository.FaultPoint = null;
            var claims = await Task.WhenAll(service.ClaimDailyGoalAsync(DailyRewardGoal.ThreeQuests), service.ClaimDailyGoalAsync(DailyRewardGoal.ThreeQuests));
            check(old.ClaimDailyGoal(DailyRewardGoal.ThreeQuests) && claims.All(result => result.Succeeded)
                && service.Tickets == ticketsBeforeThirdMission + 3 && service.TodayRewards.questsTickets == 3,
                "Concurrent third mission claims award exactly three Tickets");
            await Same("Three-quest goal and concurrent duplicate");
            check(old.Complete("daily") && (await service.CompleteAsync("daily")).Succeeded, "Complete recurring occurrence");
            clock.Now = clock.Now.AddDays(1);
            check(old.RefreshDate() && (await service.RefreshDateAsync()).Succeeded, "Date rollover"); await Same("New Daily occurrence and retained history");
            check(old.Complete("daily") && (await service.CompleteAsync("daily")).Succeeded, "Next-day recurrence rewards"); await Same("Daily next day");
            var conversion = new QuestTask { id = "conversion", title = "Conversion", daily = false };
            check(old.Upsert(conversion, null, out _) && (await service.UpsertAsync(conversion, null)).Succeeded, "Create conversion fixture");
            conversion.daily = true; conversion.scheduled = true; conversion.date = TaskService.DateKey(clock.Now);
            check(old.Upsert(conversion, null, out _) && (await service.UpsertAsync(conversion, null)).Succeeded, "Convert Todo to Daily"); await Same("Task conversion occurrence state");
            check(old.Delete("todo") && (await service.DeleteAsync("todo")).Succeeded, "Delete completed task template"); await Same("Deletion retains historical snapshot");
            check(old.SaveTag("tag-a", "Research", "#ABCDEF", out _) && (await service.SaveTagAsync("tag-a", "Research", "#ABCDEF")).Succeeded, "Rename/recolor tag"); await Same("Tag change does not rewrite historical snapshots");
            check(old.DeleteTag("tag-a") && (await service.DeleteTagAsync("tag-a")).Succeeded, "Delete tag"); await Same("Tag removal preserves historic names");
            foreach (var mode in new[] { FocusTimerMode.Pomodoro, FocusTimerMode.Stopwatch, FocusTimerMode.Countdown }) {
                var options = new FocusSessionOptions { mode = mode, focusMinutes = 25, restMinutes = 5 };
                check(old.BeginFocus(options, out var oldId), "Legacy Focus starts " + mode);
                var start = await service.BeginFocusAsync(options); check(start.Succeeded, "SQLite Focus starts " + mode);
                check(!old.BeginFocus(options, out _) && !(await service.BeginFocusAsync(options)).Succeeded, "Duplicate running Focus rejected " + mode);
                check(old.FinishFocus(oldId, 90, true) && (await service.FinishFocusAsync(start.Value, 90, true)).Succeeded, "End Early " + mode);
                await Same("Focus End Early " + mode);
                check(old.FinishFocus(oldId, 90, true) && (await service.FinishFocusAsync(start.Value, 90, true)).Succeeded, "Duplicate Focus settlement " + mode); await Same("Focus duplicate " + mode);
                check(old.BeginFocus(options, out oldId), "Legacy cancel fixture " + mode);
                start = await service.BeginFocusAsync(options); check(start.Succeeded, "SQLite cancel fixture " + mode);
                check(old.CancelFocus(oldId, mode == FocusTimerMode.Countdown) && (await service.CancelFocusAsync(start.Value, mode == FocusTimerMode.Countdown)).Succeeded, "Cancel/interruption " + mode);
                await Same("No cancelled Focus reward " + mode);
            }
            for (int i = 0; i < 4; i++) {
                var options = new FocusSessionOptions { mode = FocusTimerMode.Stopwatch };
                check(old.BeginFocus(options, out string oldId), "Legacy long Focus");
                var start = await service.BeginFocusAsync(options); check(start.Succeeded, "SQLite long Focus");
                double duration = i == 0 ? 1499 : 18000;
                check(old.FinishFocus(oldId, duration, false) && (await service.FinishFocusAsync(start.Value, duration, false)).Succeeded, "Focus ticket/daily cap boundary");
            }
            var savedFocus = await repository.ReadAllForVerificationAsync();
            check(savedFocus.Succeeded, "Read SQLite Focus receipts at the new limits");
            var longSessions = savedFocus.Value.focusSessions.Skip(savedFocus.Value.focusSessions.Count - 4).ToArray();
            check(longSessions[0].rawTickets == 0 && longSessions[1].rewardedMinutes == 240 && longSessions[1].tickets == 9
                && longSessions[2].eligibleMinutes == 240 && longSessions[2].tickets == 1
                && longSessions[3].rewardedMinutes == 0 && longSessions[3].tickets == 0
                && service.TodayRewards.focusMinutes == 480 && service.TodayRewards.focusTickets == 10, "SQLite Focus reward limits");
            await Same("Focus 25-minute ticket floor, 240-minute session cap, 480-minute daily cap and 10-ticket allowance");
            check(old.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes) && (await service.ClaimDailyGoalAsync(DailyRewardGoal.Focus25Minutes)).Succeeded, "Claim Focus goal"); await Same("Focus goal amount");
            await repository.CloseAsync();
            repository = new SqlitePlayerRepository(Path.Combine(folder, "commands.sqlite3"), json);
            check((await repository.InitializeAsync()).Succeeded, "Reopen SQLite after all commands");
            await Same("Persisted reload");
            await VerifyNoDatePaging(repository, clock, check);
        } finally { await repository.CloseAsync(); }
    }
    static async Task VerifyNoDatePaging(SqlitePlayerRepository repository, Clock clock, Action<bool, string> check)
    {
        string[] titles = { "Alpha", "alpha", "ß", "SS", "Σ", "ς", "σ", "İ", "ı", "I", "𐐀", "𐐨", "😀", "猫", "é", "e\u0301" };
        var beforeActive=await repository.ReadAsync(tx=>tx.ActiveTasks().Count);
        var inserted = await repository.ExecuteAsync(tx => {
            for (int i = 0; i < 205; i++) {
                string id = "calendar-unicode-" + i;
                var task=new QuestTask { id = id, title = titles[i % titles.Length], description = "Unicode page", difficulty = (TaskDifficulty)(i % 3) };
                tx.Put(PlayerRecordKind.Tasks,task);
                tx.Put(PlayerRecordKind.Occurrences,new TaskOccurrence {taskId=id,day="once",completed=true,rewardGranted=true});
                tx.Put(PlayerRecordKind.History, new TaskCompletion {
                    id = id, taskId = id, day = "once", completedLocalDay = TaskService.DateKey(clock.Now),
                    completedUtc = clock.UtcNow.ToString("O"), rewardedLocalDay = TaskService.DateKey(clock.Now),
                    snapshot = task
                });
            }
            return true;
        });
        check(inserted.Succeeded, "Create isolated multi-page Unicode calendar fixtures");
        var archived=await repository.ReadAsync(tx=>new { Active=tx.ActiveTasks().Count,Undo=tx.UndoableCompletions(TaskService.DateKey(clock.Now),Enumerable.Range(0,205).Select(i=>"calendar-unicode-"+i)).Count });
        check(archived.Succeeded&&archived.Value.Active==beforeActive.Value&&archived.Value.Undo==205,"Completed Todo templates leave active projection while all queried receipts remain undoable");
        var full = await repository.ReadAllForVerificationAsync();
        var oracle = new TaskService(new Memory(full.Value), clock, false);
        foreach (string search in new[] { "", "ALPHA", "𐐨", "σ" }) {
            var expected = oracle.QueryCalendar(clock.Now.Date, clock.Now.Date, search).noDate.Select(r => r.Key).ToArray();
            var actual = new List<string>(); string cursor = null; int pages = 0;
            do {
                var page = await repository.QueryNoDatePageAsync(cursor, search, null, null, true);
                check(page.Succeeded, "No Date page query succeeds");
                if (!page.Succeeded) break;
                check(page.Value.Rows.Count <= 100 && page.Value.TotalCount == expected.Length, "No Date page limit and filtered count");
                actual.AddRange(page.Value.Rows.Select(r => r.Key)); cursor = page.Value.NextCursor;
            } while (cursor != null && ++pages < 10);
            check(cursor == null && actual.SequenceEqual(expected), "No Date exact legacy Unicode order/filter and page boundaries: " + search);
        }
        check((await repository.ExecuteAsync(tx=>{
            tx.Put(PlayerRecordKind.Tags,new QuestTag{id="isolation-tag",name="Original tag",colorHex="#123456"});
            tx.Put(PlayerRecordKind.Tasks,new QuestTask{id="isolation-live",title="Isolation live",description=null,
                tagIds=new List<string>{"isolation-tag"},subtasks=new List<QuestSubtask>{new QuestSubtask{id="isolation-subtask",title="Original subtask"}}});
            return true;
        })).Succeeded,"Seed detached live calendar row");
        var detached=await repository.QueryNoDatePageAsync(null,"Isolation live",null,null,false);
        check(detached.Succeeded&&detached.Value.Rows.Count==1,"Query detached live calendar fixture");
        var live=detached.Value.Rows.Single();
        string originalDescription=live.view.task.description;
        live.kind=CalendarTaskKind.History;live.date=clock.Now;
        live.view.task.title="Mutated";live.view.task.description="Mutated";live.view.task.tagIds.Clear();live.view.task.subtasks[0].title="Mutated";
        live.view.occurrence.completed=true;live.view.occurrence.completedSubtaskIds.Add("external");
        live.view.tagNames.Clear();live.view.tagColors.Clear();
        var reread=await repository.QueryNoDatePageAsync(null,"Isolation live",null,null,false);
        var isolated=reread.Value.Rows.Single();
        check(isolated.kind==CalendarTaskKind.Task&&!isolated.date.HasValue&&isolated.view.task.title=="Isolation live"
            &&isolated.view.task.description==originalDescription&&isolated.view.task.tagIds.Single()=="isolation-tag"&&isolated.view.task.subtasks[0].title=="Original subtask"
            &&!isolated.view.occurrence.completed&&isolated.view.occurrence.completedSubtaskIds.Count==0&&isolated.view.tagNames.Single()=="Original tag"
            &&isolated.view.tagColors.Single()=="#123456","Mutating returned live row cannot corrupt cached calendar tasks occurrences or tags");
        var projection=new SqlitePlayerService(repository,clock);
        check((await projection.InitializeAsync()).Succeeded,"Initialize receipt cache mutation fixture");
        var history=await projection.QueryHistoryPageAsync(null,"Unicode page");
        check(history.Succeeded&&history.Value.Rows.Count>0,"Query receipt mutation fixture");
        var receipt=history.Value.Rows[0].history;string receiptId=receipt.id,taskId=receipt.taskId,day=receipt.day;
        check(projection.CanUndoCompletion(receiptId),"Queried receipt initially undoable");
        receipt.revoked=true;receipt.taskId="external";receipt.day="external";receipt.snapshot.title="Mutated";
        check(projection.CanUndoCompletion(receiptId)&&projection.CompletionId(taskId,day)==receiptId,"Caller receipt mutation cannot alter cached undo eligibility or completion identity");
        var second=await projection.QueryHistoryPageAsync(history.Value.NextCursor,"Unicode page");
        check(second.Succeeded&&second.Value.NextCursor!=null,"Load second compact receipt page");
        string offPage=second.Value.Rows[0].history.id;
        var third=await projection.QueryHistoryPageAsync(second.Value.NextCursor,"Unicode page");
        check(third.Succeeded&&projection.CachedReceiptIdentitiesForVerification<=100+third.Value.Rows.Count,"Browsing pages retains only presentation and current query identities");
        check(!projection.CanUndoCompletion(offPage),"Evicted off-page identity cannot authorize undo");
        var calendarFirst=await projection.QueryNoDatePageAsync(null,"Unicode page");
        check(calendarFirst.Succeeded&&calendarFirst.Value.NextCursor!=null,"Load first calendar owner receipt page");
        string firstCalendarReceipt=calendarFirst.Value.Rows[0].view.history.id;
        var calendarSecond=await projection.QueryNoDatePageAsync(calendarFirst.Value.NextCursor,"Unicode page",appendVisible:true);
        string secondCalendarReceipt=calendarSecond.Value.Rows[0].view.history.id;
        check(calendarSecond.Succeeded&&projection.CanUndoCompletion(firstCalendarReceipt)&&projection.CanUndoCompletion(secondCalendarReceipt),"Appending visible calendar rows preserves earlier row undo identities");
        string visibleHistoryReceipt=third.Value.Rows[0].history.id;
        var tutorialRead=await projection.QueryHistoryPageAsync(registerVisibleReceipts:false);
        check(tutorialRead.Succeeded&&projection.CanUndoCompletion(firstCalendarReceipt)&&projection.CanUndoCompletion(secondCalendarReceipt)
            &&projection.CanUndoCompletion(visibleHistoryReceipt),"Non-publishing tutorial history lookup preserves both visible page owners");
        var emptyCalendar=await projection.QueryNoDatePageAsync(null,"no matching calendar title");
        check(emptyCalendar.Succeeded&&emptyCalendar.Value.Rows.Count==0&&projection.CanUndoCompletion(visibleHistoryReceipt)
            &&projection.CachedReceiptIdentitiesForVerification<=100+third.Value.Rows.Count,"Replacing calendar filter releases its old identities without evicting visible history");
        var registrationEntered=new TaskCompletionSource<bool>();var releaseRegistration=new TaskCompletionSource<bool>();
        bool blockFirst=true;
        projection.BeforeReceiptRegistrationForVerification=scope=>{
            if(scope=="history"&&blockFirst){blockFirst=false;registrationEntered.TrySetResult(true);return releaseRegistration.Task;}
            return Task.CompletedTask;
        };
        var staleHistory=projection.QueryHistoryPageAsync(null,"Unicode page");
        try {
            check(await Task.WhenAny(registrationEntered.Task,Task.Delay(5000))==registrationEntered.Task,"Older same-version history registration reaches controlled delay");
            var newestHistory=await projection.QueryHistoryPageAsync(null,"no matching history title");
            check(newestHistory.Succeeded&&newestHistory.Value.Rows.Count==0,"Newer same-version filtered page completes first");
        } finally {releaseRegistration.TrySetResult(true);}
        check((await staleHistory).Succeeded&&!projection.CanUndoCompletion(visibleHistoryReceipt),"Late superseded history result cannot replace newer visible identity scope");
        projection.BeforeReceiptRegistrationForVerification=null;
        // Prime the same filter used below before observing cache reuse.
        check((await repository.QueryNoDatePageAsync(null,"σ",null,null,true)).Succeeded,"Prime cache after mutation fixture");
        int metadataBuilds=repository.NoDateMetadataBuildsForVerification;
        check((await repository.ExecuteAsync(tx=>{tx.Put(PlayerRecordKind.Settings,tx.Read<AppSettings>(PlayerRecordKind.Settings,"settings"));return true;})).Succeeded,"Commit unrelated settings for No Date cache check");
        check((await repository.QueryNoDatePageAsync(null,"σ",null,null,true)).Succeeded&&repository.NoDateMetadataBuildsForVerification==metadataBuilds,"Unrelated settings commit preserves compact No Date metadata cache");
    }
}
