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
            if (state == PlayModeStateChange.EnteredPlayMode) { run = Run(); next = EditorApplication.timeSinceStartup + 3; }
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-boss-reward.request")) return;
        File.Delete("Temp/verify-boss-reward.request");
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
            panel.CloseResult();panel.Action();panel.Confirm();Check(panel.IsBattling,"Entry");yield return .2;
            int xp=service.Snapshot.experience;
            // The first run exercises skip, the second the ordinary simulation finish.
            if(width==1920){panel.skipUI.Open();panel.skipUI.Confirm();}
            else {panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);}
            Check(service.Snapshot.experience==xp+10,"Repeat EXP");
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
        panel.CloseResult();manager.PlayerData.experience=100000;
        manager.PlayerData.bossProgress[0].wins=16;manager.PlayerData.equipmentUnlocked=true;
        manager.PlayerData.bossProgress[2].wins=1;manager.SaveData();
        foreach(var id in new[]{"boss1-weapon","boss1-body","boss1-head"})Check(service.ClaimEquipment(id),"Prepare owned boss set");
        flights.SendMessage("OnApplicationFocus",false);
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .5;
            panel.CloseResult();panel.SelectStage(1,width==1920?0:1);panel.Action();panel.Confirm();yield return .2;
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
        panel.CloseResult();panel.SelectStage(1);panel.Action();panel.Confirm();yield return .2;
        int fishBefore=service.Snapshot.fishCoins;
        int before=service.Snapshot.experience;
        saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        panel.SkipBattle();Check(service.Snapshot.experience==before&&service.Snapshot.fishCoins==fishBefore&&flights.ActiveCount==0,"Failed write cannot animate or award");
        saveLock.Dispose();saveLock=null;panel.Action();Check(service.Snapshot.experience==before+10&&service.Snapshot.fishCoins==fishBefore+10&&flights.ActiveCount>0,"Retry reward flight");
        nav.ShowPage(MainMenuPage.Tasks);yield return 2.0;Check(flights.ActiveCount==0,"Page close flight settles");
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.experience==before+10,"Persisted EXP");
        nav.ShowPage(MainMenuPage.Boss);panel.CloseResult();manager.PlayerData.experience=0;manager.SaveData();yield return .3;
        panel.Action();panel.Confirm();panel.SkipBattle();Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Defeat&&service.Snapshot.experience==0&&flights.ActiveCount==0,"Defeat grants no EXP");yield return .2;
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: normal and Master Fish icon/count/flights/header at 1920/1280; zero Fish hidden; repeat win icon/count; skip and ordinary settlement; moving EXP flights at 1920/1280; header balance/level; duplicate settlement; failed save has no reward/flight; retry exactly once; page close; reload; defeat without rewards.");
    }
}
