using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Explicit, bounded Play Mode checks; never saves the scene or uses the live save.
[InitializeOnLoad]
public static class TaskFeedbackVerification
{
    const string Flag="CatQuest.TaskFeedbackVerification";
    const string Request="Temp/verify-task-feedback.request";
    static IEnumerator run;
    static double next;
    static RewardScreenshotCapture capture;
    static Keyboard keyboard;
    static string Folder=>SessionState.GetString(Flag+".folder", "");
    static bool Before=>SessionState.GetBool(Flag+".before",false);
    static bool Intro=>SessionState.GetBool(Flag+".intro",false);
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object Group()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static TaskFeedbackVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){run=Flow();next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode)Restore();
        };
    }
    static void Restore()
    {
        run=null;
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
        var scene=SessionState.GetString(Flag+".scene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        SizeProperty.SetValue(View,SessionState.GetInt(Flag+".size",0));
        var group=Group();int builtin=Count(group,"GetBuiltinCount");
        for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
        EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);
        SessionState.SetBool(Flag,false);
        File.WriteAllText(Folder+"/restored.txt",DateTime.UtcNow.ToString("O")+" Restored save environment, start scene, Game View and pause state.");
    }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)){
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))){Finish("FAIL: timed out");return;}
            if(!EditorApplication.isPlaying||run==null)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try{if(!run.MoveNext()){Finish("PASS: "+(Before?"before/reference captures":"targeted checks and after captures"));return;}next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);}
            catch(Exception e){Finish("FAIL: "+e);}
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        string mode=File.ReadAllText(Request).Trim();bool before=mode=="before";File.Delete(Request);
        bool intro=mode=="intro" || mode=="intro-before";
        string folder="Library/TaskFeedbackVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+mode;Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);SessionState.SetBool(Flag+".before",before);
        SessionState.SetBool(Flag+".intro",intro);SessionState.SetBool(Flag+".introBefore",mode=="intro-before");
        var store=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");SessionState.SetBool(Flag+".hadStore",store!=null);SessionState.SetString(Flag+".store",store??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(View));SessionState.SetInt(Flag+".count",Count(Group(),"GetCustomCount"));SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(180).Ticks.ToString());SessionState.SetBool(Flag,true);
        try{
            File.WriteAllText(folder+"/result.txt",DateTime.UtcNow.ToString("O")+" RUNNING");
            if(!before&&!intro)RewardSystemVerification.Run();
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(folder+"/save.json"));
            if(mode=="intro")File.WriteAllText(folder+"/save.json","{\"version\":6,\"username\":\"Future player\"}");
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(intro?"Assets/Scenes/IntroStory.unity":"Assets/Scenes/CharacterHome.unity");
            EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }catch(Exception e){Finish("FAIL starting: "+e);}
    }
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+" "+result);run=null;if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else Restore();}
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Capture(string name){if(!capture)capture=new GameObject("Task feedback capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Key(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    static void Submit(Button button){ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);}
    static void Visible(RectTransform rect)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
        foreach(var corner in corners){var point=RectTransformUtility.WorldToScreenPoint(null,corner);Check(canvas.pixelRect.Contains(point),"UI extends outside viewport: "+rect.name);}
    }
    static IEnumerator Flow()
    {
        if(Intro){
            var input=UnityEngine.Object.FindFirstObjectByType<UsernameInputController>();
            Check(input&&GameDataManager.Instance.SavePath.Contains("TaskFeedbackVerification"),"Intro/isolation unavailable");
            var error=(TMPro.TMP_Text)typeof(UsernameInputController).GetField("errorText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input);
            bool before=SessionState.GetBool(Flag+".introBefore",false);
            if(!before){
                string original=File.ReadAllText(GameDataManager.Instance.SavePath);
                input.ConfirmUsername();input.ConfirmUsername();
                Check(GameDataManager.Instance.IsSaveReadOnly&&error.text.Contains("Update Cat Quest"),"Intro did not explain write protection.");
                Check(File.ReadAllText(GameDataManager.Instance.SavePath)==original&&Directory.GetFiles(Folder,"save.json*").Length==1,"Intro modified unsupported save.");
            }
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}){
                Resolution(size.x,size.y);yield return 1;Canvas.ForceUpdateCanvases();
                Check(!error.isTextOverflowing,"Version notice overflows existing error label.");Capture("intro-"+size.x);yield return .3;
            }
            yield break;
        }
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(GameDataManager.Instance.SavePath.Contains("TaskFeedbackVerification"),"Must use an isolated save.");
        Check(nav.ShowPage(MainMenuPage.Tasks),"Tasks page unavailable");yield return 1;
        var owner=nav.taskSystem;
        var dialog=(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
        if(!Before){
            keyboard=InputSystem.AddDevice<Keyboard>();
            var events=EventSystem.current;
            var popup=dialog.Root.transform.Find("Popup");
            var cancel=popup.Find("Cancel").GetComponent<Button>();var confirm=popup.Find("Confirm").GetComponent<Button>();
            events.SetSelectedGameObject(owner.filterButton.gameObject);
            int accepted=0;dialog.Show("Keyboard check","Cancel remains the safe default.",()=>accepted++);
            Check(events.currentSelectedGameObject==cancel.gameObject,"Default focus is not Cancel.");
            Key(UnityEngine.InputSystem.Key.Tab);yield return .2;Key();yield return .2;
            Check(events.currentSelectedGameObject==confirm.gameObject,"Tab did not move inside the dialog.");
            Key(UnityEngine.InputSystem.Key.LeftShift,UnityEngine.InputSystem.Key.Tab);yield return .2;Key();yield return .2;
            Check(events.currentSelectedGameObject==cancel.gameObject,"Shift+Tab did not reverse focus.");
            events.SetSelectedGameObject(owner.filterButton.gameObject);yield return .2;
            Check(events.currentSelectedGameObject==cancel.gameObject,"Focus escaped to the background.");
            Key(UnityEngine.InputSystem.Key.Escape);yield return .2;Key();yield return .2;
            Check(!dialog.Root.activeSelf&&accepted==0&&events.currentSelectedGameObject==owner.filterButton.gameObject,"Escape did not cancel/restore focus.");
            dialog.Show("Confirm once","Repeated submit must not repeat the action.",()=>accepted++);
            Submit(confirm);Submit(confirm);yield return .2;
            Check(accepted==1&&!dialog.Root.activeSelf,"Repeated submit duplicated confirmation.");
            dialog.ShowNotice("Notice","Only OK and Close should be reachable.");
            Check(!cancel.gameObject.activeSelf&&events.currentSelectedGameObject==confirm.gameObject,"Notice selected hidden Cancel.");
            Check(confirm.navigation.selectOnRight==popup.Find("Button_Close").GetComponent<Button>(),"Notice focus cycle contains hidden controls.");
            dialog.Dismiss();
            dialog.Show("Feedback states","Existing shared button feedback must reset.",()=>accepted++);yield return .3;
            var feedback=cancel.GetComponent<JuicyButton>();var pointer=new PointerEventData(events){button=PointerEventData.InputButton.Left};
            var hitRect=((RectTransform)cancel.transform).rect;
            for(int i=0;i<3;i++){
                ExecuteEvents.Execute(cancel.gameObject,pointer,ExecuteEvents.pointerEnterHandler);yield return .06;
                Capture("motion-hover-"+i);yield return .05;
                ExecuteEvents.Execute(cancel.gameObject,pointer,ExecuteEvents.pointerDownHandler);yield return .06;
                Capture("motion-press-"+i);yield return .05;
                ExecuteEvents.Execute(cancel.gameObject,pointer,ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(cancel.gameObject,pointer,ExecuteEvents.pointerExitHandler);yield return .1;
            }
            Check(((RectTransform)cancel.transform).rect==hitRect,"Feedback moved hit area.");
            cancel.interactable=false;yield return .5;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f,"Disabled button did not settle.");
            cancel.interactable=true;feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus-loss feedback not reset.");
            dialog.Dismiss();Check(feedback.DrawScale==Vector2.one,"Closing modal did not reset button.");
            dialog.Show("Backdrop","Outside click dismisses without confirming.",()=>accepted++);
            dialog.Root.transform.Find("ScreenDim").GetComponent<Button>().onClick.Invoke();Check(!dialog.Root.activeSelf&&accepted==1,"Backdrop confirmed or failed to close.");
            dialog.Show("Disable","Page closure must clear confirmation.",()=>accepted++);
            nav.ShowPage(MainMenuPage.Dashboard);yield return .2;Check(!dialog.Root.activeSelf,"Page closure left task modal open.");
            nav.ShowPage(MainMenuPage.Tasks);yield return .2;Submit(confirm);Check(accepted==1,"Closed dialog retained an action.");

            var notice=UnityEngine.Object.FindFirstObjectByType<TaskResultNotice>(FindObjectsInactive.Include);
            owner.BeginAdd();owner.QuickAddInput.text="Feedback regression task";
            Check(owner.CommitQuickAdd(true),"Quick add failed");yield return .3;
            Check(notice.gameObject.activeSelf&&notice.message.text=="Task saved."&&!dialog.Root.activeSelf,"Successful save has no nonblocking feedback.");
            Check(events.currentSelectedGameObject==owner.QuickAddInput.gameObject,"Success feedback stole quick-add focus.");
            string id=owner.Service.Snapshot.tasks.Single().id;owner.CancelQuickAdd();
            owner.CompleteSelected(id);yield return .2;
            Check(notice.message.text.Contains("+20 EXP, +10 Fish Coins"),"Receipt rewards not displayed.");
            owner.CompleteSelected(id);Check(notice.message.text.Contains("already completed"),"Duplicate completion not distinguished.");
            string receipt=owner.Service.Snapshot.history.Single().id;Check(owner.Service.UndoCompletion(receipt),"Undo failed");
            owner.CompleteSelected(id);Check(notice.message.text.Contains("previous rewards are kept"),"Recompletion not distinguished.");
            var saved=owner.Service.Snapshot;Check(saved.history.Count==1&&saved.fishCoins==10&&saved.experience==20,"Feedback changed reward settlement.");

            // Seed only this runner's isolated store at the existing cap; no production rules change.
            var day=GameDataManager.Instance.PlayerData.rewardDays.Single();day.taskExperience=owner.Service.TaskExperienceLimit;day.taskFishCoins=owner.Service.TaskFishLimit;
            GameDataManager.Instance.SaveData();
            Check(owner.Service.Upsert(new QuestTask {title="At reward limit"},null,out var capped),owner.Service.LastError);
            owner.CompleteSelected(capped);Check(notice.message.text.Contains("Daily task reward limit reached"),"Cap message is misleading.");
            Check(owner.Service.Snapshot.history.Count==2&&owner.Service.Snapshot.fishCoins==10,"Capped completion missing or rewarded.");
            var disk=new PlayerDataStore(GameDataManager.Instance.SavePath);
            Check(disk.Data.history.Count==2&&disk.Data.experience==20,"Result not persisted.");
            owner.CompleteSelected("missing-task");Check(dialog.Root.activeSelf&&!notice.gameObject.activeSelf,"Failure displayed a success notice.");dialog.Dismiss();
            Check(!notice.fade.blocksRaycasts&&!notice.fade.interactable&&notice.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"Success notice blocks input.");
            owner.Notify("Task saved.");yield return 4.4;Check(!notice.gameObject.activeSelf,"Notice did not expire.");
            owner.Notify("Task saved.");notice.SendMessage("OnApplicationFocus",false);Check(!notice.gameObject.activeSelf,"Notice persisted on focus loss.");
            owner.Notify("Task deleted.");nav.ShowPage(MainMenuPage.Dashboard);Check(!notice.gameObject.activeSelf,"Page closure left stale notice.");nav.ShowPage(MainMenuPage.Tasks);yield return .2;
            File.WriteAllText(Folder+"/checks.txt","PASS: virtual-keyboard Tab/Shift+Tab/Escape; background focus confinement; EventSystem repeated submit exactly once; notice OK focus; page disable/re-enable; quick-add focus retention; receipt/duplicate/recompletion/cap/error messages; persistence/reload; nonblocking/timeout/focus-loss notice.\n");
        }
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}){
            Resolution(size.x,size.y);yield return 1;
            dialog.Show("Complete remaining subtasks?","Some subtasks are unfinished. Confirm to complete them all and collect the task rewards.",()=>{});yield return .4;
            Capture("dialog-"+size.x);yield return .3;dialog.Dismiss();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Frames/MessageFrame_Common_Blue.prefab");
            var reference=UnityEngine.Object.Instantiate(source,owner.GetComponentInParent<Canvas>().rootCanvas.transform);
            var rect=(RectTransform)reference.transform;rect.sizeDelta=new Vector2(620,72);
            Capture("reference-message-"+size.x);yield return .3;UnityEngine.Object.Destroy(reference);
            if(!Before){
                var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
                var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
                foreach(bool expanded in new[]{false,true}){
                    toggle.isOn=expanded;yield return .3;
                    owner.Notify("Task completed. Daily task reward limit reached; no additional rewards.");yield return .3;
                    var notice=UnityEngine.Object.FindFirstObjectByType<TaskResultNotice>();
                    Visible((RectTransform)notice.transform);Check(!notice.message.isTextOverflowing,"Result text clips.");
                    Capture("notice-"+size.x+(expanded?"-expanded":"-collapsed"));yield return .3;
                    notice.Dismiss();
                    dialog.Show("Complete remaining subtasks?","Some subtasks are unfinished. Confirm to complete them all and collect the task rewards.",()=>{});yield return .3;
                    Visible((RectTransform)dialog.Root.transform.Find("Popup"));
                    Capture("keyboard-dialog-"+size.x+(expanded?"-expanded":"-collapsed"));yield return .3;dialog.Dismiss();
                }
            }
        }
    }
}
