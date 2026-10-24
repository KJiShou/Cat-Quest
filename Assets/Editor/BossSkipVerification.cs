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
    static double pausedAt;static int spent,wins;static GameObject reference;
    static void Begin(BossPageUI panel){panel.CloseResult();panel.Action();panel.Confirm();Check(panel.IsBattling,"Cannot begin battle");}
    static void CheckModal(BossBattleSkipUI ui){
        var canvas=ui.GetComponentInParent<Canvas>().rootCanvas;
        Check(ui.confirmation.transform.parent==canvas.transform,"Confirmation is not at root Canvas");
        Check(!ui.confirmation.transform.Find("Popup/ImageGroup_TV").gameObject.activeSelf,"Unused story shadow still visible");
        var rect=(RectTransform)ui.skip.transform;Check(Mathf.Abs(rect.rect.width-rect.rect.height)<.1f,"Skip is not square");
        foreach(var point in new[]{new Vector2(5,5),new Vector2(Screen.width-5,Screen.height-5),new Vector2(40,Screen.height*.5f)}){
            var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Check(hits.Count>0&&hits[0].gameObject==ui.backdrop.gameObject,"Backdrop does not block full screen at "+point+" hits="+string.Join(",",hits.Select(h=>h.gameObject.name))+" rect="+ui.backdrop.GetComponent<RectTransform>().rect+" screen="+Screen.width+","+Screen.height);
        }
    }
    static void Tick(){
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var panel=nav.pages[3].GetComponent<BossPageUI>();var manager=GameDataManager.Instance;var service=manager.Tasks;var ui=panel.skipUI;
        Check(manager.SavePath.Contains("BossSkipVerification"),"Must isolate save");
        if(step==0){manager.PlayerData.experience=3000;manager.PlayerData.tickets=12;manager.SaveData();nav.ShowPage(MainMenuPage.Boss);}
        if(step==1){Check(!ui.skip.gameObject.activeSelf,"Idle skip visible");Capture("idle-1920");}
        if(step==2){Begin(panel);spent=service.Snapshot.tickets;ui.skip.onClick.Invoke();ui.skip.onClick.Invoke();pausedAt=panel.Simulation.Elapsed;Check(ui.IsConfirming,"No skip confirmation");Check(!panel.actors.Cat.GetComponentInChildren<Animator>().enabled,"Cat not paused");Canvas.ForceUpdateCanvases();Capture("confirm-1920");}
        if(step==3){CheckModal(ui);Check(panel.Simulation.Elapsed==pausedAt,"Battle advanced during confirmation");Check(ui.later.GetComponent<JuicyButton>().DrawScale.x>1,"Selection feedback did not animate");ui.later.onClick.Invoke();Check(panel.IsBattling&&!ui.IsConfirming&&panel.actors.Cat.GetComponentInChildren<Animator>().enabled,"Later did not resume");Check(service.Snapshot.tickets==spent,"Cancel consumed ticket");Check(ui.later.GetComponent<JuicyButton>().DrawScale==Vector2.one,"Feedback did not reset on dismissal");Resolution(1280,720);}
        if(step==4){ui.skip.onClick.Invoke();Canvas.ForceUpdateCanvases();Capture("confirm-1280");}
        if(step==5){
            CheckModal(ui);
            var expected=new BattleSimulation(panel.Simulation.Cat,panel.Simulation.Boss);expected.Advance(120);
            ui.yes.onClick.Invoke();Check(!panel.IsBattling&&service.Snapshot.battles.Last().outcome==expected.Outcome,"Skip differs from full simulation");Check(expected.Outcome==BattleOutcome.Victory&&panel.resultPanels[0].activeSelf,"Win result missing");
            wins=service.Snapshot.bossProgress[0].wins;ui.yes.onClick.Invoke();panel.SkipBattle();Check(service.Snapshot.bossProgress[0].wins==wins&&service.Snapshot.tickets==spent,"Duplicate skip settlement");Capture("victory-1280");
        }
        if(step==6){manager.PlayerData.experience=0;manager.SaveData();Begin(panel);ui.skip.onClick.Invoke();ui.yes.onClick.Invoke();Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Defeat&&panel.resultPanels[1].activeSelf,"Skip fabricated a win");Capture("defeat-1280");}
        if(step==7){manager.PlayerData.experience=3000;manager.SaveData();Begin(panel);ui.skip.onClick.Invoke();saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);ui.yes.onClick.Invoke();Check(panel.actionLabel.text=="Retry Save"&&!ui.skip.gameObject.activeSelf,"Save failure not retryable");Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Running,"Failed save committed outcome");}
        if(step==8){saveLock.Dispose();saveLock=null;panel.Action();Check(service.Snapshot.battles.Last().outcome==BattleOutcome.Victory&&service.Snapshot.bossProgress[0].wins==wins+1,"Retry did not settle once");}
        if(step==9){Begin(panel);ui.skip.onClick.Invoke();ui.backdrop.onClick.Invoke();Check(!ui.IsConfirming&&panel.IsBattling,"Backdrop did not cancel");ui.skip.onClick.Invoke();nav.ShowPage(MainMenuPage.Tasks);Check(!ui.IsConfirming&&service.Snapshot.battles.Last().outcome==BattleOutcome.Stopped,"Page close lifecycle incorrect");nav.ShowPage(MainMenuPage.Boss);Check(!ui.skip.gameObject.activeSelf,"Stale skip on reopen");}
        if(step==10){
            var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/IntroStory.unity");try{
                var source=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StorySkipUI>(true)).Single();var so=new SerializedObject(source);
                reference=UnityEngine.Object.Instantiate((GameObject)so.FindProperty("confirmationPanel").objectReferenceValue,nav.GetComponentInParent<Canvas>().transform);
                foreach(var c in reference.GetComponentsInChildren<StorySkipUI>(true))UnityEngine.Object.DestroyImmediate(c);reference.SetActive(true);
            }finally{EditorSceneManager.ClosePreviewScene(scene);}Capture("story-reference-1280");
        }
        if(step==11){UnityEngine.Object.Destroy(reference);File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: confirmation, pause/resume, win/loss match full simulation, duplicate guard, save failure/retry, backdrop, page-close/reopen; 1920/1280 and story reference captures.");EditorApplication.isPlaying=false;return;}
        step++;next=EditorApplication.timeSinceStartup+1;
    }
}
