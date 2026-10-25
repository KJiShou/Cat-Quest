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
public static class MasterAuraVerification
{
    const string Flag = "CatQuest.MasterAuraVerification";
    const string Folder = "Library/MasterAuraVerification";
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
    static MasterAuraVerification()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-master-aura.request")) return;
        File.Delete("Temp/verify-master-aura.request");
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
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("MasterAuraVerification"),"Save isolation");
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Boss);var panel=nav.pages[3].GetComponent<BossPageUI>();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .4;
            foreach(int id in new[]{1,2,3}){
                panel.SelectStage(id);yield return .3;Check(!panel.arena.MasterAura||!panel.arena.MasterAura.gameObject.activeSelf,"Normal hidden");Capture("normal-"+id+"-"+width);yield return .1;
                panel.SelectStage(id,id);yield return 1.0;var aura=panel.arena.MasterAura;Check(aura&&aura.gameObject.activeInHierarchy,"Master visible");
                var pos=aura.transform.position;var scale=aura.transform.localScale;var color=aura.halo.main.startColor.color;Check(id==1?color.r>color.g&&color.g>color.b:id==2?color.b>color.r&&color.r>color.g:color.r>color.b&&color.b>color.g,"Boss color");
                foreach(var sprite in panel.actors.Boss.GetComponentsInChildren<SpriteRenderer>())Check(aura.halo.GetComponent<ParticleSystemRenderer>().sortingOrder<sprite.sortingOrder,"Aura behind boss");
                Capture("master-"+id+"-"+width);yield return .8;
                Check(Vector3.Distance(pos,aura.transform.position)<.001f,"Stationary anchor");Check(Vector3.Distance(scale,aura.transform.localScale)>.0001f,"Breathing motion");Check(aura.motes.particleCount>0,"Motes active");
                Capture("motion-"+id+"-"+width);yield return .2;
                panel.actors.StartRun();panel.actors.Run(1.2f);panel.actors.StartCombat(BattleRules.Cat(100000),BattleRules.Boss(id,id));panel.actors.Hit(true);yield return .15;Capture("combat-"+id+"-"+width);yield return .3;panel.actors.ResetPose();
                panel.SelectStage(id);yield return .1;Check(!aura.gameObject.activeSelf,"Return to normal hides");panel.SelectStage(id,id);yield return .3;
                Check(panel.arena.Stage.GetComponentsInChildren<MasterBossAura>(true).Length==1,"One aura per stage");
            }
        }
        nav.ShowPage(MainMenuPage.Tasks);yield return .2;Check(!panel.arena.MasterAura.gameObject.activeInHierarchy,"Page close hides");nav.ShowPage(MainMenuPage.Boss);yield return .5;
        Check(manager.PlayerData.battles.Count==0&&manager.PlayerData.tickets==0,"Presentation cannot settle or spend");
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: three Master colors, normal hidden, fixed anchor, breathing/motes, sorting behind boss, combat/reset, repeated toggle, page closure/reopen; 1920/1280 captures; no battles/tickets.");
    }
}
