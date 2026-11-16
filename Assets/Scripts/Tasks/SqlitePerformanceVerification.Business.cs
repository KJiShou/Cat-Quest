#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static partial class SqlitePerformanceVerification
{
    static readonly string[] BusinessOperations = {
        "SaveTag", "EditTask", "CreateTask", "DeleteTask", "CompleteTask", "UndoTask", "RecompleteTask", "Subtask", "DeleteTag", "ConfirmAiDraft",
        "BeginFocus", "FinishFocus", "EndEarly", "CancelFocus", "DailyGoal", "ClaimEquipment", "Equip", "Unequip", "AutoEquip", "Upgrade",
        "Loot1", "Loot10", "BeginBattle", "ResolveBattle", "RecoverBattle", "Settings", "Account", "Tutorial", "Rename"
    };
    sealed class BusinessFixture
    {
        internal const string TaskId="active-0", FocusId="benchmark-focus", HistoryId="benchmark-receipt", BattleId="benchmark-battle";
        internal QuestTask task=new QuestTask { id=TaskId,title="Measured task",subtasks=new List<QuestSubtask>{new QuestSubtask{id="sub",title="Measured subtask"}} };
        internal TaskOccurrence occurrence;
        internal TaskCompletion history;
        internal FocusSession focus;
        internal BattleAttempt battle;
        internal RewardDay day;
        internal BossProgress boss=new BossProgress{bossId=1,wins=15,pendingClaims=3};
        internal string operation;
        internal BusinessFixture(string operation) {
            this.operation=operation;string today="2026-11-16",utc="2026-11-16T04:00:00Z";
            bool completed=operation=="UndoTask",revoked=operation=="RecompleteTask";
            occurrence=new TaskOccurrence{taskId=TaskId,day="once",completed=completed,rewardGranted=completed||revoked};
            if(completed||revoked)history=new TaskCompletion{id=HistoryId,taskId=TaskId,day="once",snapshot=task.DetachedCopy(),completedLocalDay=today,rewardedLocalDay=today,completedUtc=utc,revoked=revoked};
            day=new RewardDay{day=today,rewardRulesInitialized=true};
            if(operation=="FinishFocus"||operation=="EndEarly"||operation=="CancelFocus"||operation=="DailyGoal")
                focus=new FocusSession{id=FocusId,status=operation=="DailyGoal"?FocusSessionStatus.Completed:FocusSessionStatus.Running,
                    mode=FocusTimerMode.Pomodoro,focusDurationSeconds=1500,restDurationSeconds=300,rewardLevel=10,startedUtc=utc,
                    rewardDay=operation=="DailyGoal"?today:null,completedUtc=operation=="DailyGoal"?utc:null,
                    elapsedSeconds=operation=="DailyGoal"?1500:0,eligibleSeconds=operation=="DailyGoal"?1500:0};
            if(operation=="ResolveBattle"||operation=="RecoverBattle")
                battle=new BattleAttempt{id=BattleId,bossId=1,cat=new CombatStats(1000,100,50,2),boss=BattleRules.Boss(1),startedUtc=utc,equipmentAbilityVersion=2};
        }
        internal void Profile(PlayerData p){p.experience=2025;p.fishCoins=100000;p.tickets=100000;p.equipmentUnlocked=true;
            p.equippedWeaponId=operation=="Unequip"?"boss1-weapon":null;p.equippedBodyOutfitId=p.equippedHeadAccessoryId=null;}
        internal void Legacy(PlayerData p){
            Profile(p);p.tasks.RemoveAll(t=>t.id==TaskId||t.id=="benchmark-new");p.tasks.Add(task);
            p.occurrences.RemoveAll(o=>o.taskId==TaskId||o.taskId=="benchmark-new");p.occurrences.Add(occurrence);
            p.history.RemoveAll(h=>h.taskId==TaskId);if(history!=null)p.history.Add(history);
            p.focusSessions.RemoveAll(f=>f.id==FocusId||f.status==FocusSessionStatus.Running);if(focus!=null)p.focusSessions.Add(focus);
            p.battles.RemoveAll(b=>b.id==BattleId||b.outcome==BattleOutcome.Running);if(battle!=null)p.battles.Add(battle);
            p.rewardDays.RemoveAll(d=>d.day==day.day);p.rewardDays.Add(day);
            p.bossProgress.RemoveAll(b=>b.bossId==1);p.bossProgress.Add(boss);
            p.equipment.RemoveAll(e=>e.id=="boss1-weapon"||e.id=="boss1-head");p.equipment.Add(new EquipmentItem{id="boss1-weapon",level=1});
            if(!p.tags.Any(t=>t.id=="benchmark-tag"))p.tags.Add(new QuestTag{id="benchmark-tag",name="Delete me"});
        }
        internal bool Sql(IPlayerTransaction tx){
            var p=tx.Read<PlayerData>(PlayerRecordKind.Profile,"profile");Profile(p);tx.Put(PlayerRecordKind.Profile,p);
            tx.Delete(PlayerRecordKind.Tasks,"benchmark-new");tx.Delete(PlayerRecordKind.Occurrences,SqlitePlayerSchema.OccurrenceId("benchmark-new","once"));
            var old=tx.FindCompletion(TaskId,"once");if(old!=null)tx.Delete(PlayerRecordKind.History,old.id);
            tx.Put(PlayerRecordKind.Tasks,task);tx.Put(PlayerRecordKind.Occurrences,occurrence);if(history!=null)tx.Put(PlayerRecordKind.History,history);
            foreach(var f in tx.Query<FocusSession>(new PlayerRecordQuery{Kind=PlayerRecordKind.FocusSessions,State=(int)FocusSessionStatus.Running}))tx.Delete(PlayerRecordKind.FocusSessions,f.id);
            tx.Delete(PlayerRecordKind.FocusSessions,FocusId);if(focus!=null)tx.Put(PlayerRecordKind.FocusSessions,focus);
            foreach(var b in tx.Query<BattleAttempt>(new PlayerRecordQuery{Kind=PlayerRecordKind.Battles,State=(int)BattleOutcome.Running}))tx.Delete(PlayerRecordKind.Battles,b.id);
            tx.Delete(PlayerRecordKind.Battles,BattleId);if(battle!=null)tx.Put(PlayerRecordKind.Battles,battle);
            tx.Put(PlayerRecordKind.RewardDays,day);tx.Put(PlayerRecordKind.BossProgress,boss);
            tx.Delete(PlayerRecordKind.Equipment,"boss1-head");tx.Put(PlayerRecordKind.Equipment,new EquipmentItem{id="boss1-weapon",level=1});
            tx.Put(PlayerRecordKind.Tags,new QuestTag{id="benchmark-tag",name="Delete me"});return true;
        }
    }
    static QuestTask NewBusinessTask()=>new QuestTask{id="benchmark-new",title="New measured task"};
    static AiTaskDraft BusinessDraft()=>new AiTaskDraft{id="benchmark-draft",day="2026-11-16",task=NewBusinessTask()};
    static bool LegacyBusiness(TaskService s,string op,int i){
        switch(op){
            case "SaveTag":return s.SaveTag("tag","Study "+i,out _);
            case "EditTask":return s.Upsert(Edit(i),null,out _);
            case "CreateTask":return s.Upsert(NewBusinessTask(),null,out _);
            case "DeleteTask":return s.Delete(BusinessFixture.TaskId);
            case "CompleteTask":case "RecompleteTask":return s.Complete(BusinessFixture.TaskId,true);
            case "UndoTask":return s.UndoCompletion(BusinessFixture.HistoryId);
            case "Subtask":return s.SetSubtask(BusinessFixture.TaskId,"sub",true);
            case "DeleteTag":return s.DeleteTag("benchmark-tag");
            case "ConfirmAiDraft":return s.ConfirmChatDraft(BusinessDraft(),out _);
            case "BeginFocus":return s.BeginFocus(out _);
            case "FinishFocus":return s.FinishFocus(BusinessFixture.FocusId,1500,false);
            case "EndEarly":return s.FinishFocus(BusinessFixture.FocusId,300,true);
            case "CancelFocus":return s.CancelFocus(BusinessFixture.FocusId);
            case "DailyGoal":return s.ClaimDailyGoal(DailyRewardGoal.Focus25Minutes);
            case "ClaimEquipment":return s.ClaimEquipment("boss1-head");
            case "Equip":return s.Equip("boss1-weapon",EquipmentSlot.Weapon);
            case "Unequip":return s.Unequip(EquipmentSlot.Weapon);
            case "AutoEquip":return s.AutoEquip();
            case "Upgrade":return s.UpgradeEquipment("boss1-weapon",1,"upgrade-"+i);
            case "Loot1":case "Loot10":return s.DrawLoot(op=="Loot1"?1:10,op+"-"+i,out _,_=>0);
            case "BeginBattle":return s.BeginBattle(1,0,out _);
            case "ResolveBattle":return s.ResolveBattle(BusinessFixture.BattleId,BattleOutcome.Victory);
            case "RecoverBattle":return s.RecoverInterruptedBattles();
            case "Settings":return s.SaveSettings(new AppSettings{soundVolume=i%2==0?.4f:.6f});
            case "Account":return s.SaveAccountSettings(new AccountPreferences{playerName="Benchmark "+i});
            case "Tutorial":return s.SaveTutorial(new TutorialProgress{version=1,dismissed=true,announcedLevel=10,taskStep=i%4});
            case "Rename":return s.RenamePlayer("Benchmark "+i);
            default:throw new ArgumentException(op);
        }
    }
    static async Task<bool> SqlBusiness(SqlitePlayerService s,string op,int i){
        switch(op){
            case "SaveTag":return (await s.SaveTagAsync("tag","Study "+i)).Succeeded;
            case "EditTask":return (await s.UpsertAsync(Edit(i),null)).Succeeded;
            case "CreateTask":return (await s.UpsertAsync(NewBusinessTask(),null)).Succeeded;
            case "DeleteTask":return (await s.DeleteAsync(BusinessFixture.TaskId)).Succeeded;
            case "CompleteTask":case "RecompleteTask":return (await s.CompleteAsync(BusinessFixture.TaskId,true)).Succeeded;
            case "UndoTask":return (await s.UndoCompletionAsync(BusinessFixture.HistoryId)).Succeeded;
            case "Subtask":return (await s.SetSubtaskAsync(BusinessFixture.TaskId,"sub",true)).Succeeded;
            case "DeleteTag":return (await s.DeleteTagAsync("benchmark-tag")).Succeeded;
            case "ConfirmAiDraft":return (await s.ConfirmChatDraftAsync(BusinessDraft())).Succeeded;
            case "BeginFocus":return (await s.BeginFocusAsync()).Succeeded;
            case "FinishFocus":return (await s.FinishFocusAsync(BusinessFixture.FocusId,1500,false)).Succeeded;
            case "EndEarly":return (await s.FinishFocusAsync(BusinessFixture.FocusId,300,true)).Succeeded;
            case "CancelFocus":return (await s.CancelFocusAsync(BusinessFixture.FocusId)).Succeeded;
            case "DailyGoal":return (await s.ClaimDailyGoalAsync(DailyRewardGoal.Focus25Minutes)).Succeeded;
            case "ClaimEquipment":return (await s.ClaimEquipmentAsync("boss1-head")).Succeeded;
            case "Equip":return (await s.EquipAsync("boss1-weapon",EquipmentSlot.Weapon)).Succeeded;
            case "Unequip":return (await s.UnequipAsync(EquipmentSlot.Weapon)).Succeeded;
            case "AutoEquip":return (await s.AutoEquipAsync()).Succeeded;
            case "Upgrade":return (await s.UpgradeEquipmentAsync("boss1-weapon",1,"upgrade-"+i)).Succeeded;
            case "Loot1":case "Loot10":return (await s.DrawLootAsync(op=="Loot1"?1:10,op+"-"+i,_=>0)).Succeeded;
            case "BeginBattle":return (await s.BeginBattleAsync(1,0)).Succeeded;
            case "ResolveBattle":return (await s.ResolveBattleAsync(BusinessFixture.BattleId,BattleOutcome.Victory)).Succeeded;
            case "RecoverBattle":return (await s.RecoverInterruptedBattlesAsync()).Succeeded;
            case "Settings":return (await s.SaveSettingsAsync(new AppSettings{soundVolume=i%2==0?.4f:.6f})).Succeeded;
            case "Account":return (await s.SaveAccountSettingsAsync(new AccountPreferences{playerName="Benchmark "+i})).Succeeded;
            case "Tutorial":return (await s.SaveTutorialAsync(new TutorialProgress{version=1,dismissed=true,announcedLevel=10,taskStep=i%4})).Succeeded;
            case "Rename":return (await s.RenamePlayerAsync("Benchmark "+i)).Succeeded;
            default:throw new ArgumentException(op);
        }
    }
    static async Task RunBusiness(string folder,Action<bool,string> check){
        Directory.CreateDirectory(folder);
        await CalibrateAllocations(check);
        string engine=CompanionProtocol.Argument("--benchmark-engine")??"SQLite";
        check(engine=="SQLite"||engine=="JSON","Known benchmark engine");
        var report=new Report{utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,device=SystemInfo.processorType,
            scope=Application.platform+" business commands; fixture preparation excluded; 100 samples + 5 warmups per operation; isolated deterministic inputs"};
        Save(folder,report);
        try {
            foreach(int active in new[]{100,1000})foreach(int count in new[]{1000,10000,100000}){
                string caseFolder=Path.Combine(folder,active+"-"+count);Directory.CreateDirectory(caseFolder);
                string source=Path.Combine(caseFolder,"source.json");var initial=Fixture(active,count);new BusinessFixture("Settings").Legacy(initial);
                File.WriteAllText(source,JsonUtility.ToJson(initial));
                SqlitePlayerRepository repo=null;SqlitePlayerService service=null;PlayerDataStore legacyStore=null;TaskService legacy=null;
                try {
                    if(engine=="SQLite"){
                        repo=new SqlitePlayerRepository(Path.Combine(caseFolder,"save.sqlite3"),source);service=new SqlitePlayerService(repo,new Clock());
                        check((await service.InitializeAsync()).Succeeded,"Business fixture migration");report.sqlite=service.StorageVersion;
                        var plans=await repo.ExplainForVerificationAsync();check(plans.Succeeded&&plans.Value.Contains("writer synchronous=2")&&plans.Value.Contains("writer journal_mode=wal")&&plans.Value.Contains("writer foreign_keys=1"),"FULL/WAL/FK retained");
                        File.WriteAllLines(Path.Combine(caseFolder,"query-plans.txt"),plans.Value);await repo.CountSqlForVerificationAsync();
                    } else {string path=Path.Combine(caseFolder,"legacy.json");File.Copy(source,path);legacyStore=new PlayerDataStore(path);legacy=new TaskService(legacyStore,new Clock());}
                    foreach(string op in BusinessOperations){
                        var metric=Begin(report,caseFolder,engine,op,active,count);var samples=new List<double>();var main=new List<double>();Save(folder,report);
                        DateTime deadline=DateTime.UtcNow.AddMinutes(5);
                        for(int i=-Warmups;i<Samples;i++){
                            if(DateTime.UtcNow>deadline)throw new TimeoutException("Business operation exceeded five minutes: "+op);
                            var fixture=new BusinessFixture(op);
                            if(repo!=null)check((await repo.ExecuteAsync(fixture.Sql)).Succeeded,"Prepare "+op);else fixture.Legacy(legacyStore.Data);
                            using var allocation=new AllocationProbe();long sql=repo?.VerificationSqlCount??0;var timer=Stopwatch.StartNew();
                            bool ok=repo!=null?await SqlBusiness(service,op,i):LegacyBusiness(legacy,op,i);
                            double elapsed=timer.Elapsed.TotalMilliseconds;long allocated=allocation.Stop();
                            check(ok,engine+" "+op+" sample "+i+": "+(service?.LastError??legacy?.LastError));
                            if(i>=0)AddSample(metric,samples,main,elapsed,repo!=null?service.LastCommandMainMilliseconds:elapsed,allocated,(repo?.VerificationSqlCount??0)-sql,service?.LastCommandStorageMilliseconds??0,service?.LastCommandQueueMilliseconds??0);
                            if(i%10==9)Save(folder,report);await Task.Yield();
                        }
                        metric.status="complete";metric.fileBytes=new FileInfo(repo?.DatabasePath??legacyStore.Path).Length;
                        if(repo!=null){metric.walBytes=File.Exists(repo.DatabasePath+"-wal")?new FileInfo(repo.DatabasePath+"-wal").Length:0;
                            check(metric.mainP95<=5&&metric.p95<=200,op+" main <=5ms / commit <=200ms P95");
                            if(count==100000){var baseline=report.metrics.Single(m=>m.operation==op&&m.activeTasks==active&&m.historicalRecords==10000);
                                var scale=new Scaling{operation=op,activeTasks=active,p95At10k=baseline.p95,p95At100k=metric.p95,allowedP95=ScalingTolerance*Math.Max(1,baseline.p95),ratio=metric.p95/Math.Max(.001,baseline.p95)};
                                scale.passed=metric.p95<=scale.allowedP95;report.scaling.Add(scale);check(scale.passed&&metric.sqlStatements<=baseline.sqlStatements+Samples,op+" bounded history scaling and SQL count");}
                        }
                        Save(folder,report);
                    }
                } finally {if(repo!=null)await repo.CloseAsync();}
            }
            report.status="passed";
        } catch(Exception error){report.status="failed";report.failure=error.ToString();throw;}
        finally{Save(folder,report);}
    }
}
#endif
