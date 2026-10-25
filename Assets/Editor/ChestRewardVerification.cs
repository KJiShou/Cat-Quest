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
public static class ChestRewardVerification
{
    const string Flag = "CatQuest.ChestRewardVerification";
    const string Folder = "Library/ChestRewardVerification";
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
    static ChestRewardVerification()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-chest-reward.request")) return;
        File.Delete("Temp/verify-chest-reward.request");
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
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("ChestRewardVerification"),"Isolated save");var service=manager.Tasks;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Boss);var gear=nav.pages[3].GetComponent<EquipmentPageUI>();var view=gear.claimView;
        manager.PlayerData.equipmentUnlocked=true;foreach(var p in manager.PlayerData.bossProgress){p.wins=15;p.pendingClaims=3;}manager.SaveData();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .4;
            foreach(int tier in new[]{1,2,3}){
                gear.OpenClaim(tier);yield return .4;Capture("tier-"+tier+"-"+width);yield return .2;
                var h=view.buttons[2].GetComponent<EquipmentHoverTarget>();var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,h.transform.position)};
                var before=JsonUtility.ToJson(service.Snapshot);h.OnPointerEnter(pointer);yield return .3;
                var detail=gear.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();Check(detail.IsHoverPreview&&detail.gameObject.activeSelf&&view.gameObject.activeInHierarchy,"Reward hover keeps chest modal");Check(detail.currentValue.text==EquipmentRules.Effect(EquipmentRules.Find("boss"+tier+"-head"),1),"Hover stat");Check(JsonUtility.ToJson(service.Snapshot)==before,"Hover read only");Capture("hover-"+tier+"-"+width);yield return .2;h.OnPointerExit(pointer);Check(!detail.gameObject.activeSelf,"Exit hides hover");
            }
        }
        gear.OpenClaim(3);var feedback=view.buttons[0].GetComponent<JuicyButton>();var pointerEvent=new PointerEventData(EventSystem.current);
        var hitRect=(RectTransform)view.buttons[0].transform;var originalSize=hitRect.rect.size;
        feedback.OnPointerEnter(pointerEvent);yield return .3;Check(feedback.DrawScale.x>1,"Hover feedback");Capture("chest-hover");yield return .1;
        feedback.OnPointerDown(pointerEvent);yield return .2;Check(feedback.DrawScale.y<1,"Press feedback");Capture("chest-press");yield return .1;
        for(int i=0;i<5;i++){feedback.OnPointerUp(pointerEvent);feedback.OnPointerDown(pointerEvent);}feedback.OnPointerExit(pointerEvent);yield return .6;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f&&hitRect.rect.size==originalSize,"Feedback settles without hit area movement");feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus loss reset");
        int count=service.Snapshot.equipment.Count;
        saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);view.buttons[0].onClick.Invoke();Check(service.Snapshot.equipment.Count==count&&!view.ownedMarks[0].activeSelf,"Failed save cannot show collected");saveLock.Dispose();saveLock=null;
        for(int slot=0;slot<3;slot++){
            view.buttons[slot].onClick.Invoke();Check(service.Snapshot.equipment.Count==count+slot+1&&view.ownedMarks[slot].activeSelf&&!view.chests[slot].enabled,"Chest reveals actual reward");
            view.buttons[slot].onClick.Invoke();Check(service.Snapshot.equipment.Count==count+slot+1,"Duplicate click no duplicate item");
        }
        gear.OpenClaim(3);yield return .4;Capture("collected-1280");yield return .2;
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.equipment.Count==3,"Reload collected items");
        var target=view.buttons[0].GetComponent<EquipmentHoverTarget>();target.OnPointerEnter(new PointerEventData(EventSystem.current){position=new Vector2(1000,500)});yield return .2;Check(gear.transform.Find("ItemDetail").gameObject.activeSelf,"Collected disabled chest still has stats");gear.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();Check(!gear.transform.Find("ItemDetail").gameObject.activeSelf,"Close clears hover");
        manager.PlayerData.bossProgress[0].wins=7;manager.PlayerData.bossProgress[0].pendingClaims=1;manager.SaveData();gear.OpenClaim(1);Check(view.buttons[0].interactable&&!view.buttons[1].interactable&&!view.buttons[2].interactable,"5/10/15 locks");yield return .3;Capture("locked-1280");yield return .2;
        nav.ShowPage(MainMenuPage.Tasks);Check(!view.gameObject.activeInHierarchy,"Page close");
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: three tier backgrounds at 1920/1280, unowned/collected hover actual stats and no mutation, direct chest claim/reveal, save failure/retry, duplicate claim, reload, fixed milestone locks, close cleanup.");
    }
}
