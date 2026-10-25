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
public static class MasterCardVerification
{
    const string Flag = "CatQuest.MasterCardVerification";
    const string Folder = "Library/MasterCardVerification";
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
    static MasterCardVerification()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-master-card.request")) return;
        File.Delete("Temp/verify-master-card.request");
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
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("MasterCardVerification"),"Save isolation");
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Boss);
        var panel=nav.pages[3].GetComponent<BossPageUI>();
        var dump=new System.Text.StringBuilder();
        foreach(var r in panel.stagePicker.GetComponentsInChildren<RectTransform>(true)){
            var im=r.GetComponent<Image>();var tx=r.GetComponent<TMPro.TMP_Text>();
            dump.AppendLine(r.name+" parent="+r.parent.name+" anchors="+r.anchorMin+"/"+r.anchorMax+" size="+r.sizeDelta+" pos="+r.anchoredPosition+" sprite="+(im&&im.sprite?AssetDatabase.GetAssetPath(im.sprite):"")+" text="+(tx?tx.text:""));
        }
        File.WriteAllText(FolderNow+"/hierarchy.txt",dump.ToString());
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .5;
            panel.stageButton.onClick.Invoke();yield return .5;Capture("locked-"+width);yield return .2;
            Check(!panel.masterChoice.interactable&&panel.masterLock.activeSelf,"Locked state");
            ExecuteEvents.Execute(panel.masterChoice.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
            Check(panel.stagePicker.activeSelf,"Disabled card cannot select");
            manager.PlayerData.bossProgress[2].wins=1;manager.PlayerData.masterStage=7;manager.SaveData();
            panel.closeStages.onClick.Invoke();panel.stageButton.onClick.Invoke();yield return .5;Capture("unlocked-"+width);yield return .2;
            var feedback=panel.masterChoice.GetComponent<JuicyButton>();Check(feedback,"Shared button feedback");
            var pointer=new PointerEventData(EventSystem.current);var rect=(RectTransform)panel.masterChoice.transform;var size=rect.rect.size;
            feedback.OnPointerEnter(pointer);yield return .25;Check(feedback.DrawScale.x>1,"Hover motion");Capture("hover-"+width);yield return .1;
            feedback.OnPointerDown(pointer);yield return .2;Check(feedback.DrawScale.y<1,"Press motion");Capture("press-"+width);yield return .1;
            feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);yield return .6;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f&&rect.rect.size==size,"Motion settles, stable hit area");
            for(int i=0;i<8;i++){feedback.OnPointerEnter(pointer);feedback.OnPointerDown(pointer);feedback.OnPointerExit(pointer);}
            feedback.SendMessage("OnApplicationFocus",false);yield return .2;Check(feedback.DrawScale==Vector2.one,"Focus loss resets feedback");
            EventSystem.current.SetSelectedGameObject(panel.masterChoice.gameObject);yield return .2;Capture("selected-"+width);yield return .1;
            ExecuteEvents.Execute(panel.masterChoice.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            Check(panel.SelectedBoss==1&&!panel.stagePicker.activeSelf,"Keyboard Master choice");
            Check(!panel.IsBattling&&!panel.confirmation.activeSelf&&manager.PlayerData.tickets==0,"Selection cannot spend or start battle");
            panel.stageButton.onClick.Invoke();yield return .2;panel.closeStages.onClick.Invoke();yield return .2;
            Check(!panel.stagePicker.activeSelf&&feedback.DrawScale==Vector2.one,"Back closes and resets");
            manager.PlayerData.bossProgress[2].wins=0;manager.SaveData();
        }
        var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Stage.prefab"),nav.GetComponentInParent<Canvas>().transform);
        var referenceRect=(RectTransform)reference.transform;referenceRect.anchorMin=Vector2.zero;referenceRect.anchorMax=Vector2.one;referenceRect.offsetMin=referenceRect.offsetMax=Vector2.zero;referenceRect.localScale=Vector3.one;yield return .5;Capture("simplecasual-stage-reference");yield return .2;UnityEngine.Object.Destroy(reference);
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: locked/unlocked Master entry; disabled click; hover/press/release/exit, repeated input, focus loss reset; keyboard submit, stable hit area and page dismissal; no entry/ticket consumed; 1920/1280 captures and vendor Stage reference.");
    }
}
