using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Opt-in check, using a disposable save rather than the player's quests or rewards.
[InitializeOnLoad]
public static class BossRewardVerification
{
    const string Flag = "CatQuest.BossRewardVerification";
    const string Folder = "Library/BossRewardVerification";
    static System.Collections.IEnumerator run;
    static double next;
    static RewardScreenshotCapture capture;
    static FileStream saveLock;
    static string FolderNow => SessionState.GetString(Flag + ".folder", Folder);
    static EditorWindow View => EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty => View.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static object SizeGroup()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static BossRewardVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { run = SessionState.GetBool(Flag+".deathOnly",false)?RunDeath():Run(); next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) {
                saveLock?.Dispose(); saveLock=null;
                SessionState.SetBool(Flag, false);
                Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
                var scene=SessionState.GetString(Flag+".scene","");
                EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
                SizeProperty.SetValue(View,SessionState.GetInt(Flag+".size",0));
                var group=SizeGroup();int builtin=Count(group,"GetBuiltinCount");
                for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
                EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);
                File.WriteAllText(FolderNow + "/done.txt", "Returned to Edit Mode; restored save environment, start scene, pause and Game View.");
            }
        };
    }
    static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
    static void Resolution(int width, int height) => typeof(DashboardRewardPlayVerification).GetMethod("Resolution", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { width, height });
    static void Capture(string name)
    {
        if (!capture) capture = new GameObject("Boss Reward Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Boss Reward Play Mode verification exceeded 180 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else EditorApplication.isPlaying=false; } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        bool deathOnly=File.Exists("Temp/verify-boss-death.request");
        if (EditorApplication.isPlayingOrWillChangePlaymode || (!deathOnly&&!File.Exists("Temp/verify-boss-reward.request"))) return;
        File.Delete(deathOnly?"Temp/verify-boss-death.request":"Temp/verify-boss-reward.request");
        SessionState.SetBool(Flag+".deathOnly",deathOnly);
        string folder=Folder+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-after";Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);
        string previous=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        SessionState.SetBool(Flag+".hadStore",previous!=null);SessionState.SetString(Flag+".store",previous??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(View));SessionState.SetInt(Flag+".count",Count(SizeGroup(),"GetCustomCount"));
        SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(180).Ticks.ToString());
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(folder+"/save.json"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        SessionState.SetBool(Flag, true); Resolution(1920, 1080); EditorApplication.isPaused=false; EditorApplication.isPlaying = true;
    }
    static System.Collections.IEnumerator RunDeath(){
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var manager=GameDataManager.Instance;var service=manager.Tasks;
        Check(manager.SavePath.Contains("BossRewardVerification"),"Save isolation");
        manager.PlayerData.experience=100000;manager.PlayerData.tickets=30;
        foreach(var progress in manager.PlayerData.bossProgress)progress.wins=1;
        manager.SaveData();
        nav.ShowPage(MainMenuPage.Boss);yield return .3;
        var panel=nav.pages[3].GetComponent<BossPageUI>();
        var actors=panel.actors;var flights=panel.rewardFlights;
        flights.SendMessage("OnApplicationFocus",false); // Discard setup-only balance changes.
        foreach(int width in new[]{1920,1280})foreach(int boss in new[]{1,2,3}){
            Resolution(width,width==1920?1080:720);panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;panel.SelectStage(boss);yield return .3;
            panel.Action();panel.Confirm();yield return 1.3;
            var camera=panel.arena.StageCamera;float size=camera.orthographicSize,fov=camera.fieldOfView;
            float timeScale=Time.timeScale;int xp=service.Snapshot.experience,tickets=service.Snapshot.tickets;
            Capture("death-before-"+width+"-"+boss);yield return .1;
            panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);
            Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Victory,"Prepared victory");
            int awarded=service.Snapshot.experience;
            Check(awarded>xp&&actors.IsFinishingVictory&&!panel.resultPanels[0].activeSelf,"Commit precedes death; banner waits");
            Check(actors.Boss.GetComponentInChildren<Animator>().speed==0,"Final pose freezes");
            panel.Settle(BattleOutcome.Victory);panel.Confirm();panel.Action();panel.ShowStages();panel.SelectStage(boss%3+1);
            Check(service.Snapshot.experience==awarded&&service.Snapshot.tickets==tickets&&panel.SelectedBoss==boss&&!panel.confirmation.activeSelf&&!panel.stagePicker.activeSelf,"Repeated input cannot restart or settle during death");
            Check(!flights.GetComponentsInChildren<Image>().Any(i=>i.name=="Reward Icon"&&i.gameObject.activeSelf),"Rewards wait for banner");
            // A short sequence of actual rendered frames, rather than a single pose.
            for(int frame=0;frame<10;frame++){
                Capture("death-motion-"+width+"-"+boss+"-"+frame.ToString("00"));yield return .08;
            }
            yield return 1.1;
            Check(panel.resultPanels[0].activeSelf&&!actors.IsFinishingVictory,"Banner follows bounded death");
            Check(Mathf.Approximately(camera.orthographicSize,size)&&Mathf.Approximately(camera.fieldOfView,fov)&&Time.timeScale==timeScale,"Projection restored; global time unchanged");
            Check(actors.Boss.GetComponentsInChildren<SpriteRenderer>().All(r=>r.color.a<.01f),"Boss remains faded behind result");
            Check(panel.bossValue.text.StartsWith("0/"),"Dead boss HP stays zero");
            Capture("death-result-"+width+"-"+boss);yield return 1.2;
            for(int frame=0;flights.ActiveCount>0&&frame<30;frame++)yield return .1;
            Check(flights.ActiveCount==0,"Delayed flights settle");
            panel.resultContinue[0].onClick.Invoke();while(panel.IsReturningFromResult)yield return .1;yield return .1;
            Check(actors.Boss.GetComponentsInChildren<SpriteRenderer>().All(r=>r.color==Color.white),"Dismiss restores actor tint");
        }
        // Interrupted presentation must never reopen a stale modal or pay twice.
        panel.SelectStage(1);panel.Action();panel.Confirm();yield return .2;
        panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);int committed=service.Snapshot.experience;
        yield return .2;actors.SendMessage("OnApplicationFocus",false);
        Check(!actors.IsFinishingVictory&&actors.Boss.GetComponentInChildren<Animator>().speed==1,"Focus loss releases visual freeze");
        nav.ShowPage(MainMenuPage.Tasks);yield return 2.0;nav.ShowPage(MainMenuPage.Boss);yield return .2;
        Check(!panel.resultPanels[0].activeSelf&&!actors.IsFinishingVictory&&service.Snapshot.experience==committed,"Page closure cancels presentation only");
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.experience==committed,"Committed victory survives reload");
        panel.Action();panel.Confirm();yield return .2;panel.Simulation.Advance(120);
        saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        panel.Settle(panel.Simulation.Outcome);
        Check(!actors.IsFinishingVictory&&!panel.resultPanels[0].activeSelf&&service.Snapshot.experience==committed,"Failed save has no victory presentation");
        saveLock.Dispose();saveLock=null;panel.Action();
        Check(actors.IsFinishingVictory&&service.Snapshot.experience>committed,"Retry starts committed presentation");
        committed=service.Snapshot.experience;panel.Settle(BattleOutcome.Victory);yield return 3.6;
        Check(service.Snapshot.experience==committed&&flights.ActiveCount==0,"Retry settles exactly once");
        panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;yield return .1;panel.Action();panel.Confirm();panel.SkipBattle();
        Check(panel.resultPanels[0].activeSelf&&!actors.IsFinishingVictory,"Explicit Skip remains immediate");
        panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;yield return .1;
        actors.Finish(BattleOutcome.Defeat);Check(!actors.IsFinishingVictory,"Defeat never plays victory effects");actors.ResetPose();
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: all three bosses at 1920/1280; final-pose freeze, rendered motion frames, bounded delayed banner, deferred reward flights, zero boss HP, exact projection/time restoration, faded corpse/dismiss reset, duplicate input, focus loss, page close/reopen, isolated reload, failed save/retry exactly once, immediate Skip, defeat excludes victory effects.");
    }
    static System.Collections.IEnumerator Run(){
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var manager=GameDataManager.Instance;var service=manager.Tasks;
        Check(manager.SavePath.Contains("BossRewardVerification"),"Save isolation");
        var panel=nav.pages[3].GetComponent<BossPageUI>();
        var flights=panel.rewardFlights;Check(flights,"Reward binding");
        manager.PlayerData.experience=3245;manager.PlayerData.tickets=12;
        manager.PlayerData.bossProgress[0].wins=1;manager.SaveData();
        nav.ShowPage(MainMenuPage.Boss);flights.SendMessage("OnApplicationFocus",false);yield return .5;
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .6;
            panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;panel.Action();panel.Confirm();Check(panel.IsBattling,"Entry");yield return .2;
            int xp=service.Snapshot.experience;
            // The first run exercises skip, the second the ordinary simulation finish.
            if(width==1920)panel.skipUI.skip.onClick.Invoke();
            else {panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);}
            Check(service.Snapshot.experience==xp+10,"Repeat EXP");
            if(width==1280){
                Check(!panel.resultPanels[0].activeSelf&&panel.actors.IsFinishingVictory,"Ordinary victory leaves room for the death sequence");
                yield return BossBattleActors.VictoryDuration+.05;
            }
            Check(panel.resultValues[0].text=="+10"&&panel.resultValues[0].transform.parent.gameObject.activeInHierarchy,"EXP strip");
            Check(panel.resultValues[0].transform.parent.GetComponentsInChildren<Image>().Any(i=>i.sprite==flights.experienceIcon),"EXP icon");
            Check(flights.ActiveCount>0,"Missing committed reward flight");
            Check(!panel.resultFishValue.transform.parent.gameObject.activeSelf,"Zero Fish reward must be hidden");
            panel.SkipBattle();panel.Settle(BattleOutcome.Victory);Check(service.Snapshot.experience==xp+10,"Duplicate UI settlement");
            Vector3? previous=null;bool moved=false;
            for(int frame=0;frame<14;frame++){
                var icon=flights.GetComponentsInChildren<Image>().FirstOrDefault(i=>i.name=="Reward Icon"&&i.gameObject.activeSelf);
                if(icon){if(previous.HasValue&&Vector3.Distance(previous.Value,icon.transform.position)>1)moved=true;previous=icon.transform.position;}
                Capture("flight-"+width+"-"+frame.ToString("00"));yield return .08;
            }
            yield return 1.0;Check(moved&&flights.ActiveCount==0,"Flight must move and settle");
            TaskService.CatLevel(service.Snapshot.experience,out int level,out int current,out int required);
            Check(flights.catStatus.level.text=="LV "+level&&flights.catStatus.experience.text==$"{current} / {required}","Header totals after flight");
            Capture("victory-"+width);yield return .2;
        }
        panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;manager.PlayerData.experience=100000;
        manager.PlayerData.bossProgress[0].wins=16;manager.PlayerData.equipmentUnlocked=true;
        manager.PlayerData.bossProgress[2].wins=1;manager.SaveData();
        foreach(var id in new[]{"boss1-weapon","boss1-body","boss1-head"})Check(service.ClaimEquipment(id),"Prepare owned boss set");
        flights.SendMessage("OnApplicationFocus",false);
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .5;
            panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;panel.SelectStage(1,width==1920?0:1);panel.Action();panel.Confirm();yield return .2;
            int coins=service.Snapshot.fishCoins;panel.SkipBattle();
            int reward=width==1920?10:30;
            Check(service.Snapshot.fishCoins==coins+reward&&panel.resultFishValue.text=="+"+reward,"Fish receipt and count");
            Check(panel.resultFishValue.transform.parent.gameObject.activeInHierarchy,"Fish reward visible");
            Check(panel.resultFishValue.transform.parent.GetComponentsInChildren<Image>().Any(i=>i.sprite==flights.fishTarget.sprite),"Fish icon");
            panel.SkipBattle();panel.Settle(BattleOutcome.Victory);Check(service.Snapshot.fishCoins==coins+reward,"Duplicate Fish settlement");
            Vector3? previous=null;bool moved=false;
            for(int frame=0;frame<16;frame++){
                var icon=flights.GetComponentsInChildren<Image>().FirstOrDefault(i=>i.name=="Reward Icon"&&i.gameObject.activeSelf&&i.sprite==flights.fishTarget.sprite);
                if(icon){if(previous.HasValue&&Vector3.Distance(previous.Value,icon.transform.position)>1)moved=true;previous=icon.transform.position;}
                Capture("fish-flight-"+width+"-"+frame.ToString("00"));yield return .08;
            }
            yield return 1.0;Check(moved&&flights.ActiveCount==0&&flights.fishValue.text==service.Snapshot.fishCoins.ToString(),"Fish flies and header settles");
            Check(!panel.resultFishValue.isTextOverflowing,"Fish number overflow");Capture("fish-victory-"+width);yield return .2;
        }
        // Master result dismissal selects the next stage but never starts/spends an entry.
        var celebration=UnityEngine.Object.FindFirstObjectByType<LevelUpCelebrationUI>();
        for(int i=0;celebration&&(celebration.IsShowing||celebration.PendingCount>0)&&i<8;i++){
            yield return 1.0;celebration.Dismiss();yield return .1;
        }
        Check(!celebration||(!celebration.IsShowing&&celebration.PendingCount==0),"Dismiss earlier level rewards before Master captures");
        foreach(int stage in new[]{2,3,4}){
            panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;int boss=1+(stage-1)%3;
            Check(panel.SelectedBoss==boss&&panel.bossTitle.text.StartsWith("Master "+stage+" "),"Master selection after dismissal");
            int tickets=service.Snapshot.tickets;
            panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;Check(service.Snapshot.tickets==tickets&&!panel.IsBattling,"Repeated close cannot enter/spend");
            panel.Action();Check(panel.confirmation.activeSelf&&service.Snapshot.tickets==tickets&&!panel.IsBattling,"Next Master requires confirmation");
            panel.cancel.onClick.Invoke();Check(service.Snapshot.tickets==tickets&&!panel.IsBattling,"Cancel next entry");
            panel.Action();panel.Confirm();Check(panel.IsBattling&&service.Snapshot.tickets==tickets-1,"Confirmed Master spends once");yield return .2;
            if(stage==3){
                saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                panel.SkipBattle();panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;
                Check(service.Snapshot.masterStage==stage&&panel.SelectedBoss==boss,"Failed Master save cannot advance selection");
                saveLock.Dispose();saveLock=null;panel.Action();
            }else panel.SkipBattle();
            Check(service.Snapshot.masterStage==stage+1&&panel.SelectedBoss==boss,"Victory persists next stage but keeps result stage");
            panel.Settle(BattleOutcome.Victory);Check(service.Snapshot.masterStage==stage+1&&service.Snapshot.tickets==tickets-1,"Duplicate Master result");
            if(stage==4){nav.ShowPage(MainMenuPage.Tasks);yield return .2;nav.ShowPage(MainMenuPage.Boss);}
            else {
                var position=panel.actors.Cat.position;
                Check(!panel.endTransition.gameObject.activeSelf,"No transition before confirmation");
                yield return .4;
                Check(panel.actors.Cat.position.x>position.x,"Cat walks out before confirmation");
                Capture("exit-before-confirm-"+stage);yield return 1.0;
                Check(panel.arena.StageCamera.WorldToViewportPoint(panel.actors.Cat.position).x>1,"Cat leaves view while result awaits confirmation");
                Check(!panel.endTransition.gameObject.activeSelf&&panel.SelectedBoss==boss,"Waiting for player preserves arena without wipe");
                panel.resultContinue[0].onClick.Invoke();
                Check(panel.IsReturningFromResult&&panel.endTransition.gameObject.activeSelf&&panel.SelectedBoss==boss,"Confirmation immediately starts closing wipe");
                Check(panel.endTransition.transform.parent==panel.arena.transform.parent&&panel.endTransition.GetComponentInParent<Mask>(),"Transition clipped by existing arena mask");
                panel.CloseResult();panel.Action();panel.Confirm();panel.SelectStage(1);
                Check(panel.IsReturningFromResult&&!panel.confirmation.activeSelf&&panel.SelectedBoss==boss&&service.Snapshot.tickets==tickets-1,"Repeated input blocked during transition");
                yield return .25;Capture("exit-close-"+stage);yield return .5;
                Capture("exit-covered-"+stage);yield return .7;
                Capture("exit-open-"+stage);
                while(panel.IsReturningFromResult)yield return .1;
            }
            yield return .4;
            Check(panel.SelectedBoss==1+stage%3&&panel.bossTitle.text.StartsWith("Master "+(stage+1)+" ")&&!panel.IsBattling,"Dismiss/reopen selects authoritative next stage");
            Resolution(stage==2?1920:1280,stage==2?1080:720);yield return .3;Capture("master-next-"+stage);yield return .2;
        }
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.masterStage==5,"Master progression reload");
        yield return 2.0;Check(flights.ActiveCount==0,"Master reward flights settle before subsequent failure checks");
        panel.Action();panel.Confirm();yield return .2;panel.Settle(BattleOutcome.Stopped);panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;
        Check(service.Snapshot.masterStage==5&&panel.bossTitle.text.StartsWith("Master 5 "),"Stopped Master stays selected");
        manager.PlayerData.experience=550;manager.SaveData();panel.Action();panel.Confirm();panel.SkipBattle();panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;
        Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Defeat&&service.Snapshot.masterStage==5&&panel.bossTitle.text.StartsWith("Master 5 "),"Defeated Master stays selected");
        manager.PlayerData.experience=100000;manager.SaveData();
        flights.SendMessage("OnApplicationFocus",false);
        panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;panel.SelectStage(1);panel.Action();panel.Confirm();yield return .2;
        int fishBefore=service.Snapshot.fishCoins;
        int before=service.Snapshot.experience;
        saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        panel.SkipBattle();Check(service.Snapshot.experience==before&&service.Snapshot.fishCoins==fishBefore&&flights.ActiveCount==0,"Failed write cannot animate or award");
        saveLock.Dispose();saveLock=null;panel.Action();Check(service.Snapshot.experience==before+10&&service.Snapshot.fishCoins==fishBefore+10&&flights.ActiveCount>0,"Retry reward flight");
        nav.ShowPage(MainMenuPage.Tasks);yield return 2.0;Check(flights.ActiveCount==0,"Page close flight settles");
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.experience==before+10,"Persisted EXP");
        nav.ShowPage(MainMenuPage.Boss);panel.CloseResult();while(panel.IsReturningFromResult)yield return .1;manager.PlayerData.experience=550;manager.SaveData();panel.SelectStage(2,5);flights.SendMessage("OnApplicationFocus",false);yield return .3;
        panel.Action();panel.Confirm();panel.SkipBattle();Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Defeat&&service.Snapshot.experience==550&&flights.ActiveCount==0,"Defeat grants no EXP");yield return .2;
        panel.CloseResult();yield return .3;
        Check(panel.IsReturningFromResult,"Defeat uses return sequence");
        nav.ShowPage(MainMenuPage.Tasks);yield return .2;
        Check(!panel.IsReturningFromResult,"Navigation cancels return");
        Check(!panel.endTransition.gameObject.activeSelf&&!panel.startTransition.gameObject.activeSelf,"Navigation clears wipes");
        nav.ShowPage(MainMenuPage.Boss);yield return .3;
        Check(panel.actors.Ready&&!panel.IsReturningFromResult,"Reopen restores preview");
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: normal and Master Fish icon/count/flights/header at 1920/1280; zero Fish hidden; repeat win icon/count; skip and ordinary settlement; moving EXP flights at 1920/1280; header balance/level; duplicate settlement; failed save has no reward/flight; retry exactly once; page close; reload; defeat without rewards; Master dismissal/reopen selects next stage, cyclic boss mapping, repeated close without spending, confirmation/cancel, failed-save retry, stop/defeat preserve stage, progression reload.");
    }
}
