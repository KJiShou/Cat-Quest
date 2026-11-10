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
public static class PomodoroCancelVerification
{
    const string Flag = "CatQuest.PomodoroCancelVerification";
    const string Folder = "Library/PomodoroCancelVerification";
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
    static PomodoroCancelVerification()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-pomodoro-cancel.request")) return;
        SessionState.SetBool(Flag+".before",File.ReadAllText("Temp/verify-pomodoro-cancel.request").Trim()=="before");
        File.Delete("Temp/verify-pomodoro-cancel.request");
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
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("PomodoroCancelVerification"),"Isolated save");var service=manager.Tasks;
        UnityEngine.Object.FindFirstObjectByType<TutorialController>().Skip();
        manager.PlayerData.experience=100;manager.SaveData();
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();Check(nav.ShowPage(MainMenuPage.Focus),"Focus unlocked");var panel=nav.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>();var focus=panel.Controller;
        var sharedPrompt=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Focus/FocusCancelConfirmation.prefab").GetComponent<FocusCancelConfirmation>();
        Check(sharedPrompt&&!sharedPrompt.rewardPreview&&!sharedPrompt.previewExperience&&!sharedPrompt.previewFish&&!sharedPrompt.previewTickets&&!sharedPrompt.previewGoal,"Shared Companion confirmation keeps its preview fields unbound");
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .4;
            var before=SessionState.GetBool(Flag+".before",false);
            var startLabel=panel.start.GetComponentInChildren<TMPro.TMP_Text>(true);
            Capture("start-"+width);yield return .2;
            foreach(var mode in new[]{FocusTimerMode.Stopwatch,FocusTimerMode.Countdown,FocusTimerMode.Pomodoro})
            {
                focus.Configure(new FocusSessionOptions{mode=mode,focusMinutes=25,restMinutes=5});
                panel.start.onClick.Invoke();yield return .3;
                Capture(mode+"-"+width);yield return .2;
                if(before){focus.Cancel();continue;}
                bool pomodoro=mode==FocusTimerMode.Pomodoro;
                Check(panel.cancel.gameObject.activeInHierarchy==pomodoro && panel.endEarly.gameObject.activeInHierarchy!=pomodoro,"Only the intended session action is visible");
                var action=pomodoro?panel.cancel:panel.endEarly;
                var label=action.GetComponentInChildren<TMPro.TMP_Text>();
                Check(label.color==startLabel.color && label.text==(pomodoro?"Cancel":"End Focus"),"Session action label and ink");
                var pointer=FocusLayoutVerification.Pointer(action,new Vector2(.5f,.5f));
                var target=FocusLayoutVerification.Hit(action,pointer);
                var feedback=action.GetComponent<JuicyButton>();
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);yield return .25;
                Check(feedback.DrawScale.y>1.01f,"Hover feedback");
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);yield return .25;
                Check(feedback.DrawScale.y<.95f,"Press feedback");Capture(mode+"-press-"+width);yield return .1;
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(null);yield return .35;
                Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.02f,"Feedback resets");
                if(pomodoro){focus.Cancel();continue;}
                focus.Advance(1);
                FocusLayoutVerification.Click(action);action.onClick.Invoke();
                Check(panel.cancelConfirmation.IsOpen && focus.Phase==FocusPhase.Focusing,"End requires confirmation");
                yield return .3;Capture(mode+"-prompt-"+width);yield return .2;
                FocusLayoutVerification.Click(panel.cancelConfirmation.keepFocusing);
                Check(!panel.cancelConfirmation.IsOpen && focus.Phase==FocusPhase.Focusing,"Keep focus continues");
                EventSystem.current.SetSelectedGameObject(action.gameObject);
                ExecuteEvents.Execute(action.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
                Check(panel.cancelConfirmation.IsOpen,"Keyboard submit opens confirmation");yield return .2;
                FocusLayoutVerification.Click(panel.cancelConfirmation.confirm);
                Check(focus.Phase==FocusPhase.Finished,"Confirmed End settles");
            }
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Ranking.prefab");
            var button=source.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Button_Global");
            var reference=UnityEngine.Object.Instantiate(button,panel.transform);
            var rect=(RectTransform)reference.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.2f);
            rect.sizeDelta=new Vector2(320,80);rect.anchoredPosition=Vector2.zero;
            Capture("ranking-reference-"+width);yield return .2;
            UnityEngine.Object.Destroy(reference.gameObject);
            if(before)continue;
            focus.Configure(new FocusSessionOptions{mode=FocusTimerMode.Pomodoro,focusMinutes=25,restMinutes=5});panel.start.onClick.Invoke();for(int i=0;i<300;i++)focus.Advance(1);
            Check(!panel.endEarly.gameObject.activeSelf&&panel.cancel.gameObject.activeSelf,"Pomodoro only Cancel");panel.endEarly.onClick.Invoke();Check(focus.Phase==FocusPhase.Focusing,"Hidden early command rejected");
            panel.cancel.onClick.Invoke();var dialog=panel.cancelConfirmation;Check(dialog.IsOpen&&focus.Phase==FocusPhase.Focusing,"Cancel requires confirmation");Check(EventSystem.current.currentSelectedGameObject==dialog.keepFocusing.gameObject,"Safe default focus");
            Check(dialog.rewardPreview&&!dialog.rewardPreview.activeSelf&&dialog.info.gameObject.activeInHierarchy&&dialog.info.text=="No rewards will be granted.","Cancel preserves the visible no-rewards message and hides the End Early estimate");
            yield return .4;Capture("confirmation-"+width);yield return .2;dialog.keepFocusing.onClick.Invoke();Check(!dialog.IsOpen&&focus.Phase==FocusPhase.Focusing,"Keep focus preserves session");
            panel.cancel.onClick.Invoke();dialog.backdrop.onClick.Invoke();Check(focus.Phase==FocusPhase.Focusing,"Backdrop dismiss");panel.cancel.onClick.Invoke();
            int xp=service.Snapshot.experience,fish=service.Snapshot.fishCoins,tickets=service.Snapshot.tickets;
            if(width==1280){saveLock=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);dialog.Confirm();Check(focus.Phase==FocusPhase.Saving,"Cancel failure retries");saveLock.Dispose();saveLock=null;panel.retry.onClick.Invoke();}else dialog.confirm.onClick.Invoke();
            dialog.Confirm();Check(focus.Phase==FocusPhase.Interrupted&&service.Snapshot.experience==xp&&service.Snapshot.fishCoins==fish&&service.Snapshot.tickets==tickets,"Cancel no rewards/rest or duplicate");yield return .3;Capture("cancelled-"+width);yield return .1;
        }
        if(SessionState.GetBool(Flag+".before",false))yield break;
        focus.Configure(new FocusSessionOptions{mode=FocusTimerMode.Pomodoro,focusMinutes=1,restMinutes=1});focus.Start();panel.cancel.onClick.Invoke();for(int i=0;i<60;i++)focus.Advance(1);Check(focus.Phase==FocusPhase.Resting&&!panel.cancelConfirmation.IsOpen,"Natural completion invalidates cancel");focus.EndRest();focus.Start();panel.cancelConfirmation.Confirm();Check(focus.Phase==FocusPhase.Focusing,"Stale confirm cannot cancel new session");panel.cancel.onClick.Invoke();nav.ShowPage(MainMenuPage.Tasks);Check(!panel.cancelConfirmation.IsOpen&&focus.Phase==FocusPhase.Focusing,"Page close dismisses only");focus.Cancel();
        nav.ShowPage(MainMenuPage.Focus);
        var host=CompanionHost.Ensure();focus.Configure(new FocusSessionOptions{mode=FocusTimerMode.Pomodoro,focusMinutes=25,restMinutes=5});focus.Start();Check(!host.Execute(new CompanionCommand{ownerId=host.OwnerId,sessionId=focus.SessionId,requestId=Guid.NewGuid().ToString("N"),action="end"})&&focus.Phase==FocusPhase.Focusing,"Host rejects old Pomodoro early command");focus.Cancel();
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: Pomodoro only Cancel; confirmation/keep/backdrop; no reward/rest; failure/retry and duplicate; natural completion/stale session; page close; confirmed Stopwatch/Countdown End Focus; host rejects legacy end; 1920/1280 captures.");
    }
}

