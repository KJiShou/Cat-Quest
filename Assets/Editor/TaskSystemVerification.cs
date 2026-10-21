using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TaskSystemVerification
{
    private sealed class Clock : ITaskClock
    {
        public DateTime now = new DateTime(2026,10,7,23,59,0);
        public DateTime LocalNow => now;
        public DateTime UtcNow => now.AddHours(-8);
    }
    private sealed class MemoryStore : IPlayerDataStore
    {
        public PlayerData Data { get; private set; } = new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData data,out string error)
        {
            error=fail ? "Simulated disk write failure" : null;
            if(fail) return false; Data=data; return true;
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static QuestTask Task(string title,bool daily=false,string date="2026-10-07") => new QuestTask { title=title,daily=daily,scheduled=true,date=date,minuteOfDay=9*60 };

    private static void VerifyTagIdentityAndColors(string folder)
    {
        var memory=new MemoryStore(); var service=new TaskService(memory,new Clock());
        Check(service.SaveTag(null,"Color study","#3d5fc3",out var tagId),service.LastError);
        var task=Task("Tagged live task"); task.tagIds.Add(tagId);
        Check(service.Upsert(task,null,out var taskId),service.LastError);
        var completed=Task("Tagged history"); completed.tagIds.Add(tagId);
        Check(service.Upsert(completed,null,out var completedId) && service.Complete(completedId,true),service.LastError);
        string originalHistory=JsonUtility.ToJson(service.Snapshot.history.Single());
        Check(service.SaveTag(tagId,"Renamed study","#FF9E3A",out var savedTagId),service.LastError);
        var live=service.Query(TaskTab.Todo,"Renamed study",null,new[]{tagId}).Single();
        Check(savedTagId==tagId && live.task.id==taskId && live.task.tagIds.SequenceEqual(new[]{tagId}),"Tag edit replaced a task/tag ID or lost its association.");
        Check(live.tagNames.Single()=="Renamed study" && live.tagColors.Single()=="#FF9E3A","Live query did not resolve the edited tag name/color.");
        var history=service.Query(TaskTab.History).Single();
        Check(history.tagNames.Single()=="Color study" && history.tagColors.Single()=="#3D5FC3"
            && JsonUtility.ToJson(service.Snapshot.history.Single())==originalHistory,"Tag edit rewrote historical names/colors or receipts.");
        Check(service.SaveTag(tagId,"Name-only rename",out _) && service.Snapshot.tags.Single(t=>t.id==tagId).colorHex=="#FF9E3A","Legacy rename overload erased the selected color.");
        string before=JsonUtility.ToJson(service.Snapshot);
        Check(!service.SaveTag(null,"name-only RENAME",TagColors.DefaultHex,out _),"Duplicate case-insensitive tag name accepted.");
        Check(!service.SaveTag(null,"  ",TagColors.DefaultHex,out _),"Blank tag name accepted.");
        Check(!service.SaveTag(null,new string('a',31),TagColors.DefaultHex,out _),"Long tag name accepted.");
        Check(!service.SaveTag(tagId,"Bad color","#FF000080",out _) && !service.SaveTag(tagId,"Bad color","red",out _),"Invalid or transparent tag color accepted.");
        Check(JsonUtility.ToJson(service.Snapshot)==before,"Rejected tag edits mutated saved data.");
        memory.fail=true;
        Check(!service.SaveTag(tagId,"Failed rename","#FF6B6D",out _) && !service.DeleteTag(tagId),"Failed tag save/delete reported success.");
        Check(JsonUtility.ToJson(service.Snapshot)==before,"Failed tag write changed catalog, task associations or history.");
        memory.fail=false;
        Check(service.SaveTag(tagId,"Retried rename","#FF6B6D",out _) && service.DeleteTag(tagId),service.LastError);
        Check(service.Snapshot.tasks.Count==2 && service.Snapshot.tasks.All(t=>!t.tagIds.Contains(tagId))
            && JsonUtility.ToJson(service.Snapshot.history.Single())==originalHistory,"Deleting a tag deleted tasks or changed history.");
        Check(!service.SaveTag(tagId,"Stale edit",TagColors.DefaultHex,out _) && !service.DeleteTag(tagId),"Stale tag editor recreated a deleted tag.");

        string path=Path.Combine(folder,"legacy-tags.json");
        string legacy="{\"version\":5,\"tagsInitialized\":true,\"tags\":[{\"id\":\"old-tag\",\"name\":\"Legacy tag\"}],\"tasks\":[{\"id\":\"old-task\",\"title\":\"Keep me\",\"tagIds\":[\"old-tag\"]}]}";
        File.WriteAllText(path,legacy);
        var disk=new PlayerDataStore(path); var persisted=new TaskService(disk,new Clock());
        Check(disk.Data.tags.Single().id=="old-tag" && disk.Data.tags.Single().colorHex==TagColors.DefaultHex
            && disk.Data.tasks.Single().tagIds.Single()=="old-tag","Legacy tag color default changed existing IDs or associations.");
        Check(persisted.SaveTag("old-tag","Updated legacy","#2A3957",out _),persisted.LastError);
        var reload=new PlayerDataStore(path);
        Check(reload.Data.tags.Single().id=="old-tag" && reload.Data.tags.Single().name=="Updated legacy"
            && reload.Data.tags.Single().colorHex=="#2A3957" && reload.Data.tasks.Single().tagIds.Single()=="old-tag","Tag rename/color/IDs did not survive reload.");
        Check(File.ReadAllText(path+".bak")==legacy,"Additive tag save failed to keep the previous save as a backup.");
        reload.Data.tags.Single().colorHex="invalid"; PlayerDataStore.Normalize(reload.Data);
        Check(reload.Data.tags.Single().colorHex==TagColors.DefaultHex,"Invalid saved tag color did not safely default.");
    }

    private static void VerifyReopeningAndOverdue()
    {
        var clock=new Clock(); var memory=new MemoryStore(); var service=new TaskService(memory,clock);
        var task=Task("Reopen ordinary"); task.subtasks.Add(new QuestSubtask { id="step",title="Keep progress" });
        Check(service.Upsert(task,null,out var id) && service.Complete(id,true),service.LastError);
        var receipt=service.Query(TaskTab.History).Single().history;
        Check(service.CanUndoCompletion(receipt.id),"Ordinary completion cannot be reopened.");
        memory.fail=true;
        Check(!service.UndoCompletion(receipt.id) && service.Query(TaskTab.History).Count==1 && service.Snapshot.occurrences.Single().completed,"Failed undo changed state.");
        memory.fail=false;
        Check(service.UndoCompletion(receipt.id) && service.UndoCompletion(receipt.id),"Undo or duplicate undo failed.");
        var reopened=service.Query(TaskTab.Todo).Single();
        Check(!reopened.Completed && reopened.occurrence.rewardGranted && reopened.occurrence.completedSubtaskIds.Contains("step"),"Undo lost reward receipt or subtask progress.");
        Check(service.Query(TaskTab.History).Count==0 && service.Snapshot.fishCoins==10 && service.Snapshot.experience==20,"Undo removed rewards or left visible history.");
        Check(service.SetSubtask(id,"step",false),service.LastError);
        Check(!service.Complete(id),"Reopened unfinished subtasks bypassed confirmation.");
        clock.now=clock.now.AddSeconds(1); memory.fail=true;
        Check(!service.Complete(id,true) && service.Query(TaskTab.History).Count==0 && !service.Query(TaskTab.Todo).Single().Completed,"Failed recompletion changed state.");
        memory.fail=false;
        Check(service.Complete(id,true) && service.Complete(id,true),"Recompletion failed.");
        var restored=service.Query(TaskTab.History).Single().history;
        Check(restored.id==receipt.id && restored.completedUtc!=receipt.completedUtc && !restored.revoked,"Recompletion did not restore the receipt and timestamp.");
        Check(service.Snapshot.history.Count==1 && service.Snapshot.fishCoins==10 && service.Snapshot.experience==20,"Recompletion paid twice or duplicated history.");
        Check(service.Delete(id) && !service.CanUndoCompletion(receipt.id) && !service.UndoCompletion(receipt.id),"Deleted task history was restored.");
        Check(!service.UndoCompletion("missing"),"Missing history accepted.");

        var daily=Task("Reopen daily",true);
        Check(service.Upsert(daily,null,out var dailyId) && service.Complete(dailyId,true),service.LastError);
        var dailyReceipt=service.Query(TaskTab.History).Single(v=>v.task.id==dailyId).history;
        Check(service.CanUndoCompletion(dailyReceipt.id) && service.UndoCompletion(dailyReceipt.id),"Today's Daily cannot be reopened.");
        Check(!service.Query(TaskTab.Daily).Single().Completed && service.Complete(dailyId,true),"Reopened Daily did not return to its list.");
        Check(service.Snapshot.fishCoins==20,"Daily recompletion paid again.");
        clock.now=clock.now.AddMinutes(2); service.RefreshDate();
        Check(!service.CanUndoCompletion(dailyReceipt.id) && !service.UndoCompletion(dailyReceipt.id),"Yesterday's Daily could be reopened.");
        Check(service.Complete(dailyId,true) && service.Snapshot.fishCoins==30,"New Daily occurrence did not earn its own reward.");

        clock.now=new DateTime(2026,10,8,9,0,0);
        var scheduled=Task("Timed",false,"2026-10-08");
        Check(service.Upsert(scheduled,null,out var scheduledId),service.LastError);
        var view=service.Query(TaskTab.Todo).Single(v=>v.task.id==scheduledId);
        Check(!service.IsOverdue(view),"Task overdue at exact scheduled time.");
        clock.now=clock.now.AddSeconds(1); Check(service.IsOverdue(view),"Today's past time is not overdue.");
        clock.now=new DateTime(2026,10,9); Check(service.IsOverdue(view),"Previous-day task is not overdue.");
        view.task.scheduled=false; Check(!service.IsOverdue(view),"Unscheduled task is overdue.");
        view.task.scheduled=true; view.occurrence.completed=true; Check(!service.IsOverdue(view),"Completed task is overdue.");
        view.occurrence.completed=false; view.history=receipt; Check(!service.IsOverdue(view),"History is overdue.");
        view.history=null; view.task.daily=true; view.task.date="2026-10-10"; Check(!service.IsOverdue(view),"Future Daily is overdue.");
        view.task.date="2026-10-01"; Check(!service.IsOverdue(view),"Daily used its start date as the deadline.");
        clock.now=new DateTime(2026,10,9,9,0,1); Check(service.IsOverdue(view),"Daily did not use today's scheduled time.");
        var legacy=JsonUtility.FromJson<TaskCompletion>("{\"id\":\"old\",\"taskId\":\"old-task\",\"day\":\"once\"}");
        Check(!legacy.revoked,"Old history defaults to revoked.");
        var roundTrip=PlayerDataStore.Copy(service.Snapshot);
        Check(roundTrip.history.All(h=>!h.revoked),"Restored history did not survive serialization.");
        Check(service.Complete(scheduledId,true),service.LastError);
        var scheduledReceipt=service.Query(TaskTab.History).Single(v=>v.task.id==scheduledId).Key;
        Check(service.UndoCompletion(scheduledReceipt) && PlayerDataStore.Copy(service.Snapshot).history.Single(h=>h.id==scheduledReceipt).revoked,"Revoked receipt did not survive serialization.");
    }

    [MenuItem("Tools/Cat Quest/Verify Task System")]
    public static void Run()
    {
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/TaskSystemVerification",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var report=new List<string>();
        try
        {
            VerifyTagIdentityAndColors(folder);
            report.Add("PASS: stable tag IDs/name/color edits, live filters, immutable history colors, validation, failed-write rollback/retry, deletion/stale edits, additive legacy load/backup and reload.");
            VerifyReopeningAndOverdue();
            report.Add("PASS: reopen ordinary/current Daily, history restrictions, duplicate clicks, retained rewards/progress, restored receipts, failed-write rollback, precise overdue deadlines, legacy history compatibility.");
            var clock=new Clock(); var memory=new MemoryStore(); var service=new TaskService(memory,clock);
            Check(service.Snapshot.tags.Count==12 && service.Snapshot.tasks.Count==0,"Default catalog or empty task list failed.");
            Check(service.Snapshot.fishCoins==0 && service.Snapshot.experience==0 && service.Snapshot.tickets==0,"Mock balances were imported.");
            var task=Task("Write report"); task.subtasks.Add(new QuestSubtask{id="outline",title="Prepare outline"});
            string tag=service.Snapshot.tags.First().id; task.tagIds.Add(tag);
            Check(service.Upsert(task,new[]{"outline"},out var id),service.LastError);
            Check(service.Query(TaskTab.Today).Count==1 && service.Query(TaskTab.Todo).Count==1,"Today/Todo queries failed.");
            var draft=service.Query(TaskTab.Todo).Single().task; draft.title="Cancelled draft";
            Check(service.Query(TaskTab.Todo).Single().task.title=="Write report","Editing a query snapshot changed saved data.");
            Check(service.SetSubtask(id,"outline",false),service.LastError);
            Check(!service.Complete(id),"Incomplete subtasks completed without confirmation.");
            Check(service.Complete(id,true) && service.Complete(id,true),"Completion failed.");
            Check(service.Snapshot.fishCoins==10 && service.Snapshot.experience==20 && service.Snapshot.history.Count==1,"Repeated completion paid rewards twice.");
            Check(service.Snapshot.tickets==0,"Task XP became ticket rewards.");
            Check(service.Query(TaskTab.Today).Count==0 && service.Query(TaskTab.History).Count==1,"Completed task routing failed.");
            Check(service.SaveTag(tag,"Renamed",out _),service.LastError);
            Check(service.Snapshot.history.Single().tagNames.Contains("FYP"),"Renaming a tag rewrote history.");
            Check(service.Delete(id) && service.Snapshot.history.Count==1,"Deleting a task removed history.");
            report.Add("PASS: independent drafts, CRUD, subtask progress, completion confirmation, reward idempotency, immutable history.");

            var daily=Task("Daily reading",true); daily.difficulty=TaskDifficulty.Hard;
            Check(service.Upsert(daily,null,out var dailyId),service.LastError);
            Check(service.Complete(dailyId,true),service.LastError);
            clock.now=clock.now.AddMinutes(2); Check(service.RefreshDate(),service.LastError);
            Check(service.Query(TaskTab.Today).Single().task.id==dailyId && !service.Query(TaskTab.Daily).Single().Completed,"Daily state did not reset at midnight.");
            Check(service.Complete(dailyId,true),service.LastError);
            Check(service.Snapshot.fishCoins==40 && service.Snapshot.history.Count==3,"Daily rewards or history failed.");
            clock.now=clock.now.AddDays(4); service.RefreshDate();
            Check(service.Snapshot.occurrences.Count(o=>o.taskId==dailyId)==3,"Offline days were backfilled.");
            var future=Task("Future habit",true,"2027-01-01"); Check(service.Upsert(future,null,out var futureId),service.LastError);
            Check(!service.Complete(futureId,true),"Future daily task completed early.");
            Check(service.Delete(dailyId) && service.Snapshot.history.Count==3,"Stopping daily repeats erased historical rewards.");
            report.Add("PASS: midnight refresh, one reward per daily occurrence, no offline backfill, future start, stop repeating.");

            var unscheduled=new QuestTask {title="Buy groceries"}; Check(service.Upsert(unscheduled,null,out var groceryId),service.LastError);
            var overdue=Task("Overdue homework",false,"2026-10-01"); overdue.difficulty=TaskDifficulty.Easy; overdue.tagIds.Add(tag);
            Check(service.Upsert(overdue,null,out var overdueId),service.LastError);
            Check(service.Query(TaskTab.Today).Any(v=>v.task.id==overdueId) && !service.Query(TaskTab.Today).Any(v=>v.task.id==groceryId),"Schedule filtering failed.");
            Check(service.Query(TaskTab.Todo,"homework",TaskDifficulty.Easy,new[]{tag}).Count==1,"Combined search/difficulty/tag filter failed.");
            Check(service.SaveTag(null,"Second",out var secondTag),service.LastError);
            Check(service.Query(TaskTab.Todo,"",null,new[]{tag,secondTag}).Count==0,"Multi-tag filter is not AND.");
            Check(!service.SaveTag(null,"renamed",out _),"Duplicate tag name accepted.");
            Check(service.DeleteTag(tag) && !service.Snapshot.tasks.Any(t=>t.tagIds.Contains(tag)),"Deleting a tag did not remove task associations.");
            Check(service.Snapshot.history.First().tagNames.Contains("FYP"),"Deleting a tag changed history.");
            int before=service.Snapshot.tasks.Count; memory.fail=true;
            Check(!service.Upsert(Task("Failed save"),null,out _) && service.Snapshot.tasks.Count==before,"Failed write modified in-memory state."); memory.fail=false;
            Check(!service.Upsert(new QuestTask{title="   "},null,out _),"Blank title accepted.");
            report.Add("PASS: tabs, search, AND filters, tag management, validation, failed-write rollback.");

            string path=Path.Combine(folder,"save.json"); File.WriteAllText(path,"{\"username\":\"Legacy player\"}");
            var disk=new PlayerDataStore(path); var diskService=new TaskService(disk,clock);
            Check(disk.Data.username=="Legacy player" && disk.Data.version==5,"Username-only migration failed.");
            Check(diskService.Upsert(Task("Persisted"),null,out var diskId),diskService.LastError);
            var reloaded=new PlayerDataStore(path); Check(reloaded.Data.tasks.Single().id==diskId,"Reload lost tasks or IDs.");
            diskService.Complete(diskId,true);
            File.WriteAllText(path,"{ broken save"); var recovered=new PlayerDataStore(path);
            Check(recovered.Data.username=="Legacy player" && recovered.Data.tasks.Any(t=>t.id==diskId) && recovered.LoadNotice!=null,"Backup recovery failed.");
            Check(recovered.TrySave(PlayerDataStore.Copy(recovered.Data),out _),"Recovered data could not be saved.");
            Check(Directory.GetFiles(folder,"save.json.corrupt.*").Length==1,"Corrupt primary was not preserved.");
            Check(File.Exists(path+".bak"),"Recovery overwrote or deleted the valid backup.");
            string blocked=Path.Combine(folder,"not-a-directory"); File.WriteAllText(blocked,"blocking file");
            var unwritable=new PlayerDataStore(Path.Combine(blocked,"save.json"));
            var ticketFile=System.IO.Path.Combine(folder,"ticket-wallet.json");
            File.WriteAllText(ticketFile,"{\"username\":\"Ticket player\",\"experience\":90,\"tickets\":7}");
            var ticketStore=new PlayerDataStore(ticketFile); var ticketService=new TaskService(ticketStore,clock);
            var ticketTask=new QuestTask { title="Ticket separation" }; Check(ticketService.Upsert(ticketTask,null,out var ticketTaskId),ticketService.LastError);
            Check(ticketService.Complete(ticketTaskId,true),ticketService.LastError);
            var reloadedWallet=new PlayerDataStore(ticketFile).Data;
            Check(reloadedWallet.tickets==7 && reloadedWallet.experience==110,"Ticket balance was lost or mixed with XP after reload.");
            var candidate=PlayerDataStore.Copy(unwritable.Data); candidate.fishCoins=123;
            Check(!unwritable.TrySave(candidate,out var writeError) && !string.IsNullOrEmpty(writeError) && unwritable.Data.fishCoins==0,"Actual file-write failure changed data or was not reported.");
            report.Add("PASS: legacy migration, disk persistence, stable IDs, corrupt-save backup recovery.");
            File.WriteAllText(Path.Combine(Application.dataPath,"../Temp/task-system.verification"),string.Join("\n",report)+"\nALL PASS\nArtifacts: "+folder);
            Debug.Log("Task system verification: ALL PASS");
        }
        catch(Exception e)
        {
            File.WriteAllText(Path.Combine(Application.dataPath,"../Temp/task-system.verification"),string.Join("\n",report)+"\nFAIL: "+e);
            Debug.LogException(e); throw;
        }
    }
}
