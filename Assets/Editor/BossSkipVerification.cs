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
public static class BossSkipVerification
{
    const string Flag = "CatQuest.BossSkipVerification";
    const string Folder = "Library/BossSkipVerification";
    static int step;
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
    static BossSkipVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = 0; next = EditorApplication.timeSinceStartup + 3; }
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
        if (!capture) capture = new GameObject("Boss Skip Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Boss Skip Play Mode verification exceeded 180 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { Tick(); } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-boss-skip.request")) return;
        File.Delete("Temp/verify-boss-skip.request");
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
    static int spent,wins;
    static void Begin(BossPageUI panel){panel.Action();panel.Confirm();Check(panel.IsBattling,"Cannot begin battle: "+panel.message.text);}
    static void Tick(){
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var panel=nav.pages[3].GetComponent<BossPageUI>();var manager=GameDataManager.Instance;var service=manager.Tasks;var ui=panel.skipUI;
        Check(manager.SavePath.Contains("BossSkipVerification"),"Must isolate save");
        if(step==0){UnityEngine.Object.FindFirstObjectByType<TutorialController>().Skip();manager.PlayerData.experience=3000;manager.PlayerData.tickets=12;manager.SaveData();nav.ShowPage(MainMenuPage.Boss);}
        if(step==1){Check(!ui.skip.gameObject.activeSelf,"Idle skip visible");Capture("idle-1920");}
        if(step==2){Begin(panel);spent=service.Snapshot.tickets;Capture("battle-1920");}
        if(step==3){
            var expected=new BattleSimulation(panel.Simulation.Cat,panel.Simulation.Boss);expected.Advance(120);
            FocusLayoutVerification.Click(ui.skip);Check(!panel.IsBattling&&service.Snapshot.battles.Last().outcome==expected.Outcome,"Skip differs from full simulation");Check(expected.Outcome==BattleOutcome.Victory&&panel.resultPanels[0].activeSelf,"Win result missing");
            wins=service.Snapshot.bossProgress[0].wins;ui.skip.onClick.Invoke();panel.SkipBattle();Check(service.Snapshot.bossProgress[0].wins==wins&&service.Snapshot.tickets==spent,"Duplicate skip settlement");Check(!ui.confirmation.activeSelf,"Skip opened a confirmation");Capture("victory-1920");
        }
        if(panel.IsReturningFromResult)return;
        if(step==4){panel.CloseResult();Resolution(1280,720);}
        if(step==5){
            if(panel.SelectedBoss!=2){manager.PlayerData.experience=1800;manager.SaveData();panel.SelectStage(2);next=EditorApplication.timeSinceStartup+.1;return;}
            if(panel.arena.IsLoading){next=EditorApplication.timeSinceStartup+.1;return;}
            Check(panel.arena.IsReady,"Orc arena failed to load");Begin(panel);
        }
        if(step==6){FocusLayoutVerification.Click(ui.skip);Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Defeat&&panel.resultPanels[1].activeSelf,"Skip fabricated a win");Capture("defeat-1280");}
        if(step==7)panel.CloseResult();
        if(step==8){manager.PlayerData.experience=3000;manager.SaveData();panel.SelectStage(1);Begin(panel);saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);ui.skip.onClick.Invoke();Check(panel.actionLabel.text=="Retry Save"&&!ui.skip.gameObject.activeSelf,"Save failure not retryable");Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Running,"Failed save committed outcome");}
        if(step==9){saveLock.Dispose();saveLock=null;panel.Action();Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Victory&&service.Snapshot.bossProgress[0].wins==wins+1,"Retry did not settle once");panel.CloseResult();}
        if(step==10){
            Begin(panel);spent=service.Tickets;
            Check(!nav.ShowPage(MainMenuPage.Tasks)&&nav.CurrentPage==MainMenuPage.Boss&&panel.confirmation.activeSelf&&panel.IsBattling,"Leaving a live battle must ask before stopping");
            Check(EventSystem.current.currentSelectedGameObject==panel.cancel.gameObject,"Keep fighting must have initial focus");
            panel.cancel.onClick.Invoke();panel.Confirm();
            Check(panel.IsBattling&&nav.CurrentPage==MainMenuPage.Boss&&service.Tickets==spent,"Cancel/stale confirmation must keep the battle and ticket unchanged");
            nav.ShowPage(MainMenuPage.Tasks);nav.ShowPage(MainMenuPage.Dashboard);
            saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            panel.Confirm();panel.Confirm();
            Check(nav.CurrentPage==MainMenuPage.Boss&&panel.actionLabel.text=="Retry Save"&&service.Snapshot.battles.Last().outcome==BattleOutcome.Running,"Failed stop must remain on Boss with retry");
            Check(!nav.ShowPage(MainMenuPage.Dashboard)&&nav.CurrentPage==MainMenuPage.Boss,"An unsaved result must block another destination");
        }
        if(step==11){
            saveLock.Dispose();saveLock=null;panel.Action();
            Check(nav.CurrentPage==MainMenuPage.Tasks&&service.Snapshot.battles.Last().outcome==BattleOutcome.Stopped&&service.Tickets==spent,"Successful retry must stop once and use the original destination");
            var reloaded=new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock());
            Check(reloaded.Snapshot.battles.Last().outcome==BattleOutcome.Stopped&&reloaded.Tickets==spent,"Stopped entry and spent ticket must survive reload");
            panel.Confirm();nav.ShowPage(MainMenuPage.Boss);Check(!ui.skip.gameObject.activeSelf&&!panel.confirmation.activeSelf,"Stale stop/skip after reopen");
            Begin(panel);nav.ShowPage(MainMenuPage.Tasks);panel.SkipBattle();
            Check(nav.CurrentPage==MainMenuPage.Boss&&!panel.confirmation.activeSelf&&service.Snapshot.battles.Last().outcome==BattleOutcome.Victory,"A completed battle must invalidate unconfirmed navigation");
            panel.Confirm();Check(nav.CurrentPage==MainMenuPage.Boss&&panel.resultPanels[0].activeSelf,"Stale leave confirmation must not hide the result");
        }
        if(step==12){File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: single-click skip, deterministic win/loss, duplicate guard, save retry; navigation confirmation/cancel/default focus; failed stop remains visible, retry navigates once, original destination retained, reload; natural result invalidates stale leave; page close/reopen.");EditorApplication.isPlaying=false;return;}
        step++;next=EditorApplication.timeSinceStartup+1;
    }
}
