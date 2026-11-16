using System;
using System.Threading.Tasks;
using static SqlitePlayVerificationSupport;
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
    static IDisposable saveLock;
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
    static bool polling;
    static PlayerData saved;
    static async void Poll() { if(polling)return;polling=true;try{await PollAsync();}finally{polling=false;} }
    static async Task PollAsync()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Boss Skip Play Mode verification exceeded 180 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { await Tick(); } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
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
    static Vector3 defeatedPosition;
    static void CheckDeath(BossPageUI panel,bool completed)
    {
        var state=panel.actors.Cat.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0);
        Check(state.IsName("Die"),"Defeated cat must play Die instead of Run");
        Check(Vector3.Distance(panel.actors.Cat.position,defeatedPosition)<.001f,"Defeated cat must not run out of the arena");
        if(completed)Check(state.normalizedTime>=1,"Cat death clip must finish while the result stays open");
    }
    static async Task Begin(BossPageUI panel){panel.Action();panel.Confirm();saved=await DrainAsync(GameDataManager.Instance.Service);Check(panel.IsBattling,"Cannot begin battle: "+panel.message.text);}
    static async Task Tick(){
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var panel=nav.pages[3].GetComponent<BossPageUI>();var manager=GameDataManager.Instance;var service=manager.Service;var ui=panel.skipUI;saved=await DrainAsync(service);
        Check(manager.SavePath.Contains("BossSkipVerification"),"Must isolate save");
        if(step==0){UnityEngine.Object.FindFirstObjectByType<TutorialController>().Skip();saved.experience=3000;saved.tickets=12;Check((await SetFixtureAsync(saved)).Succeeded,"SQLite fixture commit");saved=await DrainAsync(service);nav.ShowPage(MainMenuPage.Boss);}
        if(step==1){Check(!ui.skip.gameObject.activeSelf,"Idle skip visible");Capture("idle-1920");}
        if(step==2){await Begin(panel);spent=saved.tickets;Capture("battle-1920");}
        if(step==3){
            var expected=new BattleSimulation(panel.Simulation.Cat,panel.Simulation.Boss);expected.Advance(120);
            FocusLayoutVerification.Click(ui.skip);saved=await DrainAsync(service);Check(!panel.IsBattling&&saved.battles.Last().outcome==expected.Outcome,"Skip differs from full simulation");Check(expected.Outcome==BattleOutcome.Victory&&panel.resultPanels[0].activeSelf,"Win result missing");
            wins=saved.bossProgress[0].wins;ui.skip.onClick.Invoke();saved=await DrainAsync(service);panel.SkipBattle();saved=await DrainAsync(service);Check(saved.bossProgress[0].wins==wins&&saved.tickets==spent,"Duplicate skip settlement");Check(!ui.confirmation.activeSelf,"Skip opened a confirmation");Capture("victory-1920");
        }
        if(panel.IsReturningFromResult)return;
        if(step==4){panel.CloseResult();Resolution(1280,720);}
        if(step==5){
            if(panel.SelectedBoss!=2){saved.experience=1800;Check((await SetFixtureAsync(saved)).Succeeded,"SQLite fixture commit");saved=await DrainAsync(service);panel.SelectStage(2);next=EditorApplication.timeSinceStartup+.1;return;}
            if(panel.arena.IsLoading){next=EditorApplication.timeSinceStartup+.1;return;}
            Check(panel.arena.IsReady,"Orc arena failed to load");await Begin(panel);
        }
        if(step==6){FocusLayoutVerification.Click(ui.skip);saved=await DrainAsync(service);Check(saved.battles.Last().outcome==BattleOutcome.Defeat&&panel.resultPanels[1].activeSelf,"Skip fabricated a win");defeatedPosition=panel.actors.Cat.position;CheckDeath(panel,false);Capture("defeat-1280");}
        if(step==7){CheckDeath(panel,true);Capture("defeat-held-1280");panel.CloseResult();}
        if(step==8){saved.experience=3000;Check((await SetFixtureAsync(saved)).Succeeded,"SQLite fixture commit");saved=await DrainAsync(service);panel.SelectStage(1);await Begin(panel);saveLock=TaskCalendarPlayVerification.FailSqliteWrites();ui.skip.onClick.Invoke();saved=await DrainAsync(service);Check(panel.actionLabel.text=="Retry Save"&&!ui.skip.gameObject.activeSelf,"Save failure not retryable");Check(saved.battles.Last().outcome==BattleOutcome.Running,"Failed save committed outcome");}
        if(step==9){saveLock.Dispose();saveLock=null;panel.Action();saved=await DrainAsync(service);Check(saved.battles.Last().outcome==BattleOutcome.Victory&&saved.bossProgress[0].wins==wins+1,"Retry did not settle once");panel.CloseResult();}
        if(step==10){
            await Begin(panel);spent=service.Tickets;
            Check(!nav.ShowPage(MainMenuPage.Tasks)&&nav.CurrentPage==MainMenuPage.Boss&&panel.confirmation.activeSelf&&panel.IsBattling,"Leaving a live battle must ask before stopping");
            Check(EventSystem.current.currentSelectedGameObject==panel.cancel.gameObject,"Keep fighting must have initial focus");
            panel.cancel.onClick.Invoke();panel.Confirm();saved=await DrainAsync(service);
            Check(panel.IsBattling&&nav.CurrentPage==MainMenuPage.Boss&&service.Tickets==spent,"Cancel/stale confirmation must keep the battle and ticket unchanged");
            nav.ShowPage(MainMenuPage.Tasks);nav.ShowPage(MainMenuPage.Dashboard);
            saveLock=TaskCalendarPlayVerification.FailSqliteWrites();
            panel.Confirm();saved=await DrainAsync(service);panel.Confirm();saved=await DrainAsync(service);
            Check(nav.CurrentPage==MainMenuPage.Boss&&panel.actionLabel.text=="Retry Save"&&saved.battles.Last().outcome==BattleOutcome.Running,"Failed stop must remain on Boss with retry");
            Check(!nav.ShowPage(MainMenuPage.Dashboard)&&nav.CurrentPage==MainMenuPage.Boss,"An unsaved result must block another destination");
        }
        if(step==11){
            saveLock.Dispose();saveLock=null;panel.Action();saved=await DrainAsync(service);
            Check(nav.CurrentPage==MainMenuPage.Tasks&&saved.battles.Last().outcome==BattleOutcome.Stopped&&service.Tickets==spent,"Successful retry must stop once and use the original destination");
            var reloaded=await Bounded(ReloadAsync());
            Check(reloaded.battles.Last().outcome==BattleOutcome.Stopped&&reloaded.tickets==spent,"Stopped entry and spent ticket must survive reload");
            panel.Confirm();saved=await DrainAsync(service);nav.ShowPage(MainMenuPage.Boss);Check(!ui.skip.gameObject.activeSelf&&!panel.confirmation.activeSelf,"Stale stop/skip after reopen");
            panel.SelectStage(1);
        }
        if(step==12){
            if(panel.arena.IsLoading){next=EditorApplication.timeSinceStartup+.1;return;}
            await Begin(panel);nav.ShowPage(MainMenuPage.Tasks);panel.SkipBattle();saved=await DrainAsync(service);
            Check(nav.CurrentPage==MainMenuPage.Boss&&!panel.confirmation.activeSelf&&saved.battles.Last().outcome==BattleOutcome.Victory,"A completed battle must invalidate unconfirmed navigation");
            panel.Confirm();saved=await DrainAsync(service);Check(nav.CurrentPage==MainMenuPage.Boss&&panel.resultPanels[0].activeSelf,"Stale leave confirmation must not hide the result");
        }
        if(step==13){panel.CloseResult();Resolution(1920,1080);}
        if(step==14){saved.experience=550;Check((await SetFixtureAsync(saved)).Succeeded,"SQLite fixture commit");saved=await DrainAsync(service);await Begin(panel);}
        if(step==15){
            if(panel.IsBattling){next=EditorApplication.timeSinceStartup+.05;return;}
            Check(saved.battles.Last().outcome==BattleOutcome.Defeat&&panel.resultPanels[1].activeSelf,"Natural defeat result missing");
            defeatedPosition=panel.actors.Cat.position;CheckDeath(panel,false);Capture("natural-defeat-1920");
        }
        if(step==16){CheckDeath(panel,true);Capture("natural-defeat-held-1920");panel.CloseResult();}
        if(step==17){
            Check(panel.actors.Cat.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Continue must restore the cat after defeat");
            File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: single-click skip, deterministic win/loss, duplicate guard, save retry; navigation confirmation/cancel/default focus; failed stop remains visible, retry navigates once, original destination retained, reload; natural result invalidates stale leave; page close/reopen; skipped and natural defeat play and hold cat death without exit movement; Continue restores Idle.");EditorApplication.isPlaying=false;return;
        }
        step++;next=EditorApplication.timeSinceStartup+(step==7||step==16?3:1);
    }
}
