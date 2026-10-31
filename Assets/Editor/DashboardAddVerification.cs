using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class DashboardAddVerification
{
    const string Flag="CatQuest.DashboardAddVerification";
    const string Folder="Library/DashboardAddVerification";
    const string StoreVariable="CATQUEST_TASK_TEST_STORE";

    static IEnumerator run;
    static double next;
    static string context;
    static RewardScreenshotCapture capture;
    static DashboardAddVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){run=Run();next=EditorApplication.timeSinceStartup+2;}
            if(state==PlayModeStateChange.EnteredEditMode)RestoreSettings();
        };
    }
    static void Poll()
    {
        if(SessionState.GetBool(Flag,false))
        {
            if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Flag+".deadline",""),null,System.Globalization.DateTimeStyles.RoundtripKind))
            { Finish("FAIL: verification timed out at "+context);return; }
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!EditorApplication.isPlaying||run==null)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try
            {
                if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);
                else Finish("PASS: Dashboard inline input, pointer/keyboard activation, blank validation, duplicate submit, outside-blur auto-save/blank cancellation, page-switch save/failure blocking, save failure/retry, Escape cancellation, reload and Today defaults at 1920x1080 and 1280x720.");
            }
            catch(Exception e){Finish("FAIL at "+context+": "+e);}
            return;
        }
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-dashboard-add.request"))return;
        File.Delete("Temp/verify-dashboard-add.request");Directory.CreateDirectory(Folder);
        var previousStore=Environment.GetEnvironmentVariable(StoreVariable);
        SessionState.SetBool(Flag+".hadStore",previousStore!=null);
        SessionState.SetString(Flag+".store",previousStore??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(GameView));
        var group=GameViewGroup();
        SessionState.SetInt(Flag+".customCount",(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null));
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(180).ToString("O"));
        SessionState.SetBool(Flag,true);
        try
        {
            File.WriteAllText(Folder+"/result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            Environment.SetEnvironmentVariable(StoreVariable,Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            EditorApplication.isPlaying=true;
        }
        catch(Exception e){Finish("FAIL starting verification: "+e);}
    }
    static EditorWindow GameView=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty=>GameView.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object GameViewGroup()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");
        return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static void RestoreSettings()
    {
        run=null;
        Environment.SetEnvironmentVariable(StoreVariable,SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
        string previousScene=SessionState.GetString(Flag+".scene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previousScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScene);
        try
        {
            SizeProperty.SetValue(GameView,SessionState.GetInt(Flag+".size",0));
            var group=GameViewGroup();
            int count=(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null);
            int builtIn=(int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group,null);
            for(int i=count-1;i>=SessionState.GetInt(Flag+".customCount",count);i--)
                group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtIn+i});
        }
        finally { SessionState.SetBool(Flag,false); }
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+"\n"+result);
        run=null;
        if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;
        else RestoreSettings();
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Resolution(int width, int height)
    {
        var assembly = typeof(Editor).Assembly;
        var viewType = assembly.GetType("UnityEditor.GameView");
        var gameView=GameView; var sizeProperty=SizeProperty;
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var getGroup = sizesType.GetMethod("GetGroup");
        var group = getGroup.Invoke(sizes, new[] { Enum.ToObject(getGroup.GetParameters()[0].ParameterType, 0) });
        var fixedType = assembly.GetType("UnityEditor.GameViewSizeType");
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var constructor = sizeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { fixedType, typeof(int), typeof(int), typeof(string) }, null);
        var size = constructor.Invoke(new[] { Enum.ToObject(fixedType, 1), (object)width, height, "Cat Quest Verification" });
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int builtIn = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
        int custom = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
        sizeProperty.SetValue(gameView, builtIn + custom - 1);
        gameView.Show(); gameView.Focus();
    }

    static void Capture(string name) {
        if(!capture) capture=new GameObject("Dashboard Add Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));
    }
    static IEnumerator Run() {
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(nav,"Navigation missing");
        var owner=nav.pages[0].GetComponentInChildren<DashboardTasksUI>(true);
        Check(owner && owner.addTaskButton && owner.quickAddInput && owner.quickAddRoot,"Saved Dashboard add bindings missing");
        var manager=GameDataManager.EnsureInstance();
        Check(Path.GetFullPath(manager.SavePath).StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Test must use isolated save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        var service=manager.Tasks;
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(2560,1080)}) {
            context=size.ToString(); Resolution(size.x,size.y); nav.ShowPage(MainMenuPage.Dashboard); yield return .7;
            Capture("dashboard-"+size.x); yield return .2;
            var button=owner.addTaskButton;
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            Canvas.ForceUpdateCanvases(); var rect=(RectTransform)button.transform;
            var feedback=button.GetComponent<JuicyButton>();
            var card=(RectTransform)owner.transform;
            var corners=new Vector3[4];
            feedback.OnPointerEnter(pointer);
            for(int sample=0;sample<15;sample++) {
                yield return .03;
                rect.GetWorldCorners(corners);
                foreach(var corner in corners) {
                    var local=rect.InverseTransformPoint(corner);
                    local=(Vector3)rect.rect.center+Vector3.Scale(local-(Vector3)rect.rect.center,new Vector3(feedback.DrawScale.x,feedback.DrawScale.y,1))+Vector3.up*feedback.DrawOffset;
                    float x=card.InverseTransformPoint(rect.TransformPoint(local)).x;
                    Check(x>card.rect.xMin+2 && x<card.rect.xMax-2,"Add Task hover escapes card");
                }
            }
            Check(feedback.DrawScale.x>1.01f,"Hover animation missing");
            Capture("hover-"+size.x); yield return .2;
            feedback.OnPointerDown(pointer); yield return .15;
            Capture("pressed-"+size.x); yield return .2;
            feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);yield return .6;
            Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f,"Add feedback did not settle");
            pointer.position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"Add button blocked by raycast");
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler); owner.BeginAddTask(); yield return .5;
            Check(nav.CurrentPage==MainMenuPage.Dashboard && owner.quickAddRoot.activeInHierarchy && owner.quickAddInput.isFocused,"Add did not focus Dashboard input");
            Check(owner.GetComponentsInChildren<TMP_InputField>().Length==1,"Duplicate input");
            Capture("input-"+size.x); yield return .2;
            int count=service.Snapshot.tasks.Count;
            owner.quickAddInput.text="   "; owner.quickAddInput.onSubmit.Invoke(owner.quickAddInput.text);
            Check(service.Snapshot.tasks.Count==count && owner.quickAddRoot.activeSelf,"Blank input created task");
            EventSystem.current.SetSelectedGameObject(null); yield return .3;
            Check(!owner.quickAddRoot.activeSelf && service.Snapshot.tasks.Count==count,"Blank outside click did not cancel");
            owner.BeginAddTask(); yield return .2;
            owner.quickAddInput.text="Dashboard task "+size.x;
            if(size.x==1920){
                string temporary=manager.SavePath+".tmp"; Directory.CreateDirectory(temporary);
                try {
                    EventSystem.current.SetSelectedGameObject(null); yield return .2;
                    Check(!nav.ShowPage(MainMenuPage.Tasks),"Navigation discarded failed draft");
                    Check(nav.CurrentPage==MainMenuPage.Dashboard,"Save failure left Dashboard");
                }
                finally { Directory.Delete(temporary); }
                Check(service.Snapshot.tasks.Count==count && owner.quickAddInput.text=="Dashboard task "+size.x,"Failure lost draft or created task");
                Capture("save-failure"); yield return .2;
                var modal=owner.GetComponentInParent<Canvas>().rootCanvas.transform.Find("DashboardTaskConfirmDialog");
                modal.Find("Popup/Confirm").GetComponent<Button>().onClick.Invoke();
                owner.BeginAddTask(); yield return .2;
            }
            EventSystem.current.SetSelectedGameObject(null);
            owner.quickAddInput.onDeselect.Invoke(owner.quickAddInput.text); yield return .4;
            Check(nav.CurrentPage==MainMenuPage.Dashboard && !owner.quickAddRoot.activeSelf,"Save left Dashboard or kept submitted draft");
            Check(service.Snapshot.tasks.Count==count+1,"Task not created exactly once");
            Check(new PlayerDataStore(manager.SavePath).Data.tasks.Count==count+1,"Saved task missing on reload");
            var task=service.Snapshot.tasks.Last(); Check(task.scheduled && !task.daily && task.date==TaskService.DateKey(service.LocalNow),"Wrong Today defaults");
            Check(owner.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==task.title),"Created task missing on Dashboard");
            Capture("saved-"+size.x); yield return .2;
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler); yield return .4;
            Check(owner.quickAddRoot.activeSelf,"Keyboard add failed"); owner.quickAddInput.text="Discard me";
            owner.quickAddInput.ProcessEvent(Event.KeyboardEvent("escape")); owner.quickAddInput.DeactivateInputField(); yield return .3;
            Check(!owner.quickAddRoot.activeSelf && service.Snapshot.tasks.Count==count+1,"Escape did not cancel draft");
            owner.BeginAddTask(); yield return .2; owner.quickAddInput.text="Leave Dashboard "+size.x;
            Check(nav.ShowPage(MainMenuPage.Tasks),"Navigation could not save draft"); yield return .3;
            Check(service.Snapshot.tasks.Count==count+2 && !owner.quickAddRoot.activeSelf,"Leaving Dashboard did not save exactly once");
            nav.ShowPage(MainMenuPage.Dashboard); yield return .3;
            owner.BeginAddTask(); yield return .2; owner.quickAddInput.text="Submit "+size.x;
            owner.quickAddInput.onSubmit.Invoke(owner.quickAddInput.text);
            EventSystem.current.SetSelectedGameObject(null); yield return .3;
            Check(service.Snapshot.tasks.Count==count+3,"Submit followed by blur duplicated task");
        }
    }
}
