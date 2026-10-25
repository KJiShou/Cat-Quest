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

[InitializeOnLoad]
public static class TaskFilterVerification
{
    const string Flag="CatQuest.TaskFilterVerification";
    const string Folder="Library/TaskFilterVerification";
    const string StoreVariable="CATQUEST_TASK_TEST_STORE";
    static IEnumerator run;
    static double next;
    static string context;
    static RewardScreenshotCapture capture;
    static TaskFilterVerification()
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
                else Finish("PASS: combined filters, draft retention, exclusive panels, apply/reopen, dismiss/discard, clear, empty tags, keyboard submit, hover/press/reset and disable/re-enable; captures at 1920x1080 and 1280x720.");
            }
            catch(Exception e){Finish("FAIL at "+context+": "+e);}
            return;
        }
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-task-filter.request"))return;
        File.Delete("Temp/verify-task-filter.request");Directory.CreateDirectory(Folder);
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
    static IEnumerator Run()
    {
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(nav && nav.taskSystem,"Task navigation missing.");
        Check(Path.GetFullPath(GameDataManager.EnsureInstance().SavePath).StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Not using isolated save.");
        nav.ShowPage(MainMenuPage.Tasks); yield return 1;
        var owner=nav.taskSystem; owner.SelectTab(TaskTab.Todo);
        var tag=owner.Service.Snapshot.tags[0];
        string id;
        Check(owner.Service.Upsert(new QuestTask{title="Hard tagged",difficulty=TaskDifficulty.Hard,tagIds=new List<string>{tag.id}},null,out id),"Cannot seed task.");
        Check(owner.Service.Upsert(new QuestTask{title="Easy tagged",difficulty=TaskDifficulty.Easy,tagIds=new List<string>{tag.id}},null,out id),"Cannot seed task.");
        Check(owner.Service.Upsert(new QuestTask{title="Hard untagged",difficulty=TaskDifficulty.Hard},null,out id),"Cannot seed task.");
        owner.filterButton.onClick.Invoke();yield return .3;
        var d=UnityEngine.Object.FindFirstObjectByType<TaskFilterDialog>();
        Check(d && d.difficultyPanel.activeSelf && !d.tagsPanel.activeSelf,"Initial panel selection wrong.");
        foreach(var component in d.GetComponentsInChildren<Component>(true))Check(component,"Missing script in filter prefab.");
        d.difficultyOptions[3].isOn=true;
        ExecuteEvents.Execute(d.tagsTab.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
        Check(!d.difficultyPanel.activeSelf && d.tagsPanel.activeSelf,"Keyboard tab switch failed.");
        d.tagContent.Find("Tag_"+tag.id).GetComponent<Toggle>().isOn=true;
        for(int i=0;i<8;i++){d.difficultyTab.onClick.Invoke();d.tagsTab.onClick.Invoke();}
        Check(d.difficultyOptions[3].isOn && d.tagsLabel.text=="Tags (1)" && d.difficultyLabel.text=="Difficulty (1)","Switching lost draft filters.");
        d.apply.onClick.Invoke();yield return .2;
        Check(Visible(owner)==1,"Combined filters did not intersect.");
        owner.filterButton.onClick.Invoke();yield return .2;
        Check(d.difficultyOptions[3].isOn && d.tagsLabel.text=="Tags (1)","Applied filters not restored.");
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
        {
            context="Render "+size;
            typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
            yield return .8;
            foreach(bool tags in new[]{false,true})
            {
                (tags?d.tagsTab:d.difficultyTab).onClick.Invoke();yield return .3;
                Check(d.tagsFocus.activeSelf==tags && d.difficultyFocus.activeSelf!=tags,"Equipment tab underline selection wrong.");
                Canvas.ForceUpdateCanvases();
                foreach(var button in new[]{d.difficultyTab,d.tagsTab,d.apply,d.clear,d.close})CheckHit(button,(RectTransform)button.transform);
                if(tags)CheckHit(d.tagContent.Find("Tag_"+tag.id).GetComponent<Toggle>(),(RectTransform)d.tagContent.Find("Tag_"+tag.id+"/Box"));
                else foreach(var option in d.difficultyOptions)CheckHit(option,(RectTransform)option.transform.Find("Box"));
                foreach(var t in d.GetComponentsInChildren<TMPro.TMP_Text>())Check(!t.isTextOverflowing,"Clipped label: "+t.text);
                Capture((tags?"tags-":"difficulty-")+size.x+"x"+size.y);yield return .3;
            }
        }
        d.tagScroll.verticalNormalizedPosition=0;yield return .3;
        var last=d.tagContent.GetComponentsInChildren<Toggle>().Last();
        CheckHit(last,(RectTransform)last.transform.Find("Box"));
        ExecuteEvents.Execute(last.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        Check(last.isOn && d.tagsLabel.text=="Tags (2)","Scrolled tag selection failed.");
        last.isOn=false;
        d.tagScroll.verticalNormalizedPosition=1;
        var feedback=d.tagsTab.GetComponent<JuicyButton>();
        EventSystem.current.SetSelectedGameObject(null);
        feedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .4;
        Check(feedback.DrawScale.y>1.005f,"Hover feedback missing."); Capture("motion-hover");yield return .2;
        feedback.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return .3;
        Check(feedback.DrawScale.y<.98f,"Press feedback missing.");Capture("motion-press");yield return .2;
        feedback.OnPointerUp(new PointerEventData(EventSystem.current));feedback.OnPointerExit(new PointerEventData(EventSystem.current));yield return .6;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.005f,"Feedback did not settle.");
        d.tagsTab.interactable=false;feedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .3;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.005f,"Disabled feedback moved.");d.tagsTab.interactable=true;
        d.difficultyTab.onClick.Invoke();d.difficultyOptions[1].isOn=true;d.close.onClick.Invoke();
        Check(Visible(owner)==1,"Dismiss applied draft.");
        owner.filterButton.onClick.Invoke();yield return .2;
        Check(d.difficultyOptions[3].isOn,"Dismiss did not discard draft.");
        d.clear.onClick.Invoke();Check(Visible(owner)==3,"Clear did not reset both filters.");
        owner.filterButton.onClick.Invoke();yield return .2;
        d.backdrop.onClick.Invoke();Check(!d.gameObject.activeSelf,"Backdrop did not dismiss.");
        owner.filterButton.onClick.Invoke();yield return .2;
        nav.ShowPage(MainMenuPage.Dashboard);Check(!d.gameObject.activeSelf,"Page closure left modal open.");
        nav.ShowPage(MainMenuPage.Tasks);yield return .2;
        d.Show(null,new string[0],new List<QuestTag>(),(difficulty,tags)=>{});
        d.tagsTab.onClick.Invoke();yield return .3;Check(d.emptyTags.activeInHierarchy,"Empty state missing.");Capture("empty-tags");yield return .3;
        d.Dismiss();
        nav.ShowPage(MainMenuPage.CatHome);yield return .5;Capture("reference-project-equipment");yield return .3;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Equipment.prefab");
        var reference=UnityEngine.Object.Instantiate(source,owner.GetComponentInParent<Canvas>().rootCanvas.transform);
        reference.SetActive(true);reference.transform.SetAsLastSibling();yield return .5;Capture("reference-simplecasual-equipment");yield return .3;
        UnityEngine.Object.Destroy(reference);
    }
    static void CheckHit(Selectable control,RectTransform hitArea)
    {
        var canvas=control.GetComponentInParent<Canvas>().rootCanvas;
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var point=RectTransformUtility.WorldToScreenPoint(camera,hitArea.TransformPoint(hitArea.rect.center));
        var hits=new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        Check(hits.Count>0 && (hits[0].gameObject==control.gameObject || hits[0].gameObject.transform.IsChildOf(control.transform)),"Control blocked: "+control.name);
    }
    static int Visible(TaskSystemUI owner) => ((List<TaskView>)typeof(TaskSystemUI).GetField("visible",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner)).Count;
    static void Capture(string name)
    {
        if(!capture)capture=new GameObject("Filter Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));
    }
}
