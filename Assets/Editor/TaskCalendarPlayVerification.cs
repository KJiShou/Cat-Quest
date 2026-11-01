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
public static class TaskCalendarPlayVerification
{
    const string Flag="CatQuest.TaskCalendarPlayVerification";
    const string Folder="Library/TaskCalendarVerification";
    const string StoreVariable="CATQUEST_TASK_TEST_STORE";
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)};
    static IEnumerator run;
    static double next;
    static string context;
    static Vector2Int requestedSize;
    static RewardScreenshotCapture capture;
    static TaskCalendarPlayVerification()
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
                else Finish("PASS: calendar four-resolution/sidebar rendering and task flow; feedback and bounds checks.");
            }
            catch(Exception e){Finish("FAIL at "+context+": "+e);}
            return;
        }
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-task-calendar.request"))return;
        File.Delete("Temp/verify-task-calendar.request");Directory.CreateDirectory(Folder);
        var previousStore=Environment.GetEnvironmentVariable(StoreVariable);
        SessionState.SetBool(Flag+".hadStore",previousStore!=null);
        SessionState.SetString(Flag+".store",previousStore??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(GameView));
        var group=GameViewGroup();
        SessionState.SetInt(Flag+".customCount",(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null));
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(240).ToString("O"));
        SessionState.SetBool(Flag,true);
        try
        {
            File.WriteAllText(Folder+"/result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            var savePath = Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json");
            File.WriteAllText(savePath, "{\"version\":5,\"username\":\"Calendar verification\",\"tagsInitialized\":true,\"tutorial\":{\"version\":1,\"dismissed\":true,\"taskStep\":4}}");
            Environment.SetEnvironmentVariable(StoreVariable,savePath);
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
        run=null;requestedSize=default;
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
    static Rect ScreenBounds(RectTransform rect)
    {
        var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var points=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).ToArray();
        return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
    }
    internal static Rect Viewport(Component owner,Vector2Int expectedSize)
    {
        var canvas=owner.GetComponentInParent<Canvas>().rootCanvas;
        // EditorApplication.update can expose another Editor view through Screen;
        // compare UI geometry against the viewport of the Canvas being verified.
        var viewport=canvas.pixelRect;
        Check(viewport.width>0&&viewport.height>0,"Canvas has no rendered viewport.");
        if(expectedSize.x>0&&expectedSize.y>0)
            Check(Mathf.Abs(viewport.width-expectedSize.x)<=1&&Mathf.Abs(viewport.height-expectedSize.y)<=1,
                "Canvas viewport does not match requested "+expectedSize+": "+viewport);
        var bounds=ScreenBounds((RectTransform)canvas.transform);
        Check(Vector2.Distance(bounds.position,viewport.position)<=1&&Vector2.Distance(bounds.size,viewport.size)<=1,
            "Canvas geometry and viewport disagree: "+bounds+" / "+viewport);
        return viewport;
    }
    static void Contained(Rect outer,Rect inner,string message)
        =>Check(inner.xMin>=outer.xMin-1&&inner.xMax<=outer.xMax+1&&inner.yMin>=outer.yMin-1&&inner.yMax<=outer.yMax+1,message+" "+inner+" within "+outer);
    static bool Overlap(Rect a,Rect b)=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>1&&Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>1;
    static void Click(Button button, Vector2? fraction = null)
    {
        Canvas.ForceUpdateCanvases();
        var bounds = ScreenBounds((RectTransform)button.transform); var point = fraction ?? new Vector2(.5f,.5f);
        var pointer = new PointerEventData(EventSystem.current) { position = bounds.position + Vector2.Scale(bounds.size,point), button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)), "Blocked calendar hit: " + button.name + " by " + (hits.Count > 0 ? hits[0].gameObject.name : "nothing"));
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static void Readable(TMPro.TMP_Text text)
    {
        text.ForceMeshUpdate();
        Check(text.textInfo.characterInfo.Take(text.textInfo.characterCount).Any(c=>c.isVisible),"Invisible caption: "+text.name+" / "+text.text+" rect="+text.rectTransform.rect);
    }
    static IEnumerator Run()
    {
        TaskCalendarVerification.VerifyBindings();
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(nav && nav.taskSystem, "Navigation missing");
        Check(Path.GetFullPath(GameDataManager.EnsureInstance().SavePath).StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Live save must never be used");
        nav.ShowPage(MainMenuPage.Tasks); yield return 1;
        var owner=nav.taskSystem; var calendar=owner.calendar;
        Check(calendar, "Calendar binding missing");
        var now=owner.Service.LocalNow.Date;
        string[] titles={"Prepare project presentation","Review methodology chapter","Finish calendar usability testing","Read research paper","Plan next milestone"};
        foreach(var title in titles) Check(owner.Service.Upsert(new QuestTask {title=title,scheduled=true,date=TaskService.DateKey(now)},null,out _),owner.Service.LastError);
        Check(owner.Service.Upsert(new QuestTask{title="Daily reading",daily=true,scheduled=true,date=TaskService.DateKey(now)},null,out var daily),owner.Service.LastError);
        Check(owner.Service.Upsert(new QuestTask{title="Unscheduled idea"},null,out _),owner.Service.LastError);
        Check(owner.Service.Upsert(new QuestTask{title="Already completed",scheduled=true,date=TaskService.DateKey(now)},null,out var completed)&&owner.Service.Complete(completed,true),owner.Service.LastError);
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        capture=new GameObject("Calendar capture").AddComponent<RewardScreenshotCapture>();
        Click(calendar.calendarMode); yield return 1;
        Click(calendar.listMode);yield return .2;Check(!calendar.Active,"List switch did not respond");
        Click(calendar.calendarMode);yield return .2;Check(calendar.Active,"Calendar switch did not respond");
        foreach(var size in Sizes)
        {
            requestedSize=size;
            typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
            foreach(bool expanded in new[]{false,true})
            {
                context=size.x+"x"+size.y+(expanded?"-expanded":"-collapsed"); toggle.isOn=expanded; yield return 1;
                calendar.CloseDrawer();calendar.SetMode(TaskDisplayMode.List);owner.SelectTab(TaskTab.Todo);yield return .3;
                Check(calendar.listMode.image.sprite != calendar.calendarMode.image.sprite,"List selection lacks a distinct background");
                Readable(calendar.listMode.GetComponentInChildren<TMPro.TMP_Text>());
                Readable(calendar.calendarMode.GetComponentInChildren<TMPro.TMP_Text>());
                capture.Begin(Path.GetFullPath(Folder+"/list-"+context+".png"));yield return .2;
                calendar.SetMode(TaskDisplayMode.Calendar);yield return .3;
                var viewport=Viewport(owner,size);
                Contained(viewport,ScreenBounds(calendar.grid),"Grid outside viewport");
                Check(calendar.days.Count(d=>d.gameObject.activeInHierarchy)==42,"Expected six-week grid");
                foreach(var day in calendar.days)Contained(ScreenBounds(calendar.grid),ScreenBounds((RectTransform)day.transform),"Day outside grid");
                Readable((TMPro.TMP_Text)calendar.search.placeholder);Readable(calendar.noDateLabel);
                foreach(var day in calendar.days)foreach(var caption in day.taskLabels.Where(t=>t.gameObject.activeInHierarchy))Readable(caption);
                capture.Begin(Path.GetFullPath(Folder+"/month-"+context+".png"));yield return .2;
                var todayArtwork=calendar.today.image.sprite;
                Click(calendar.next);yield return .2;
                Check(calendar.SelectedDate!=now && calendar.today.image.sprite!=todayArtwork,"Today remained highlighted after navigating away");
                capture.Begin(Path.GetFullPath(Folder+"/other-month-"+context+".png"));yield return .2;
                Click(calendar.today);yield return .2;
                Check(calendar.SelectedDate==now && calendar.today.image.sprite==todayArtwork,"Today did not restore date and highlight");
                Click(calendar.days.Single(d=>d.Date==now).dateButton,new Vector2(.85f,.85f)); yield return .5;
                Contained(viewport,ScreenBounds(calendar.drawer),"Drawer outside viewport");
                foreach(var caption in calendar.rows.GetComponentsInChildren<TMPro.TMP_Text>().Where(t=>t.name=="DateTime"))Readable(caption);
                capture.Begin(Path.GetFullPath(Folder+"/day-"+context+".png"));yield return .2;
                var row=calendar.rows.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("CalendarTask_"));
                row.onClick.Invoke();yield return .4;
                Check(calendar.detailRoot.gameObject.activeInHierarchy,"Details did not open");
                capture.Begin(Path.GetFullPath(Folder+"/details-"+context+".png"));yield return .2;
                calendar.Back();yield return .2;Click(calendar.drawerAdd);yield return .5;
                Check(owner.IsEditing&&owner.editor.gameObject.activeInHierarchy,"Calendar draft missing");
                Check(owner.editor.datePicker.SelectedDate==now&&owner.editor.scheduleToggle.isOn,"Calendar draft date not inherited");
                Contained(viewport,ScreenBounds((RectTransform)owner.editor.transform),"Editor outside viewport");
                capture.Begin(Path.GetFullPath(Folder+"/edit-"+context+".png"));yield return .2;
                Click(owner.editor.datePicker.dateButton);yield return .2;
                Check(owner.editor.datePicker.overlay.activeInHierarchy,"Editor date picker hidden by old List parent");
                Contained(viewport,ScreenBounds(owner.editor.datePicker.calendar),"Editor date picker outside viewport");
                owner.editor.datePicker.dismiss.onClick.Invoke();
                owner.CancelDraft();calendar.CloseDrawer();
            }
        }
        context="no-date creation";
        calendar.OpenNoDate();yield return .2;Click(calendar.drawerAdd);yield return .2;
        Check(!owner.editor.scheduleToggle.isOn,"No Date creation unexpectedly scheduled");owner.CancelDraft();calendar.CloseDrawer();
        context="date jump";
        Click(calendar.monthButton);yield return .3;
        Check(calendar.jumpPicker.overlay.activeInHierarchy,"Date jump did not open");
        capture.Begin(Path.GetFullPath(Folder+"/date-jump.png"));yield return .2;
        calendar.jumpPicker.dismiss.onClick.Invoke();yield return .2;
        context="failed-save retry";
        calendar.SelectDate(now);calendar.AddTask();yield return .2;owner.editor.taskTitle.text="Retry calendar save";
        using(var locked=File.Open(GameDataManager.EnsureInstance().SavePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
            owner.SaveDraft();Check(owner.IsEditing&&owner.HasTaskModal,"Failed write lost draft/error feedback");
            Check(!owner.Service.Snapshot.tasks.Any(t=>t.title=="Retry calendar save"),"Failed write changed committed memory");
        }
        ((TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner)).Dismiss();
        owner.SaveDraft();yield return .3;
        Check(!owner.IsEditing&&owner.Service.Snapshot.tasks.Count(t=>t.title=="Retry calendar save")==1,"Retry did not commit once");calendar.CloseDrawer();
        context="CRUD and input";
        calendar.SelectDate(now);calendar.AddTask();yield return .2;
        owner.editor.taskTitle.text="Created from calendar";owner.SaveDraft();yield return .5;
        Check(!owner.IsEditing&&owner.Service.Snapshot.tasks.Count(t=>t.title=="Created from calendar")==1,"Draft did not save once");
        var id=owner.Service.Snapshot.tasks.Single(t=>t.title=="Created from calendar").id;
        var entry=owner.Service.QueryCalendar(now,now).days[now].items.Single(i=>i.view.task.id==id);
        calendar.OpenItem(entry);yield return .2;
        calendar.detailRoot.Find("Button/CompleteQuest").GetComponentInChildren<Button>().onClick.Invoke();yield return .4;
        Check(owner.Service.Snapshot.history.Count(h=>h.taskId==id&&!h.revoked)==1,"Completion receipt missing/duplicated");
        calendar.OpenItem(owner.Service.QueryCalendar(now,now).days[now].items.Single(i=>i.view.task.id==id));yield return .2;
        Check(calendar.undo.gameObject.activeInHierarchy,"Undo missing");calendar.undo.onClick.Invoke();yield return .3;
        Check(owner.Service.QueryCalendar(now,now).days[now].items.Single(i=>i.view.task.id==id).kind==CalendarTaskKind.Task,"Undo did not restore task");
        calendar.SelectDate(now.AddDays(1));yield return .2;
        var preview=owner.Service.QueryCalendar(now.AddDays(1),now.AddDays(1)).days[now.AddDays(1)].items.Single(i=>i.view.task.id==daily);
        calendar.OpenItem(preview);yield return .2;
        Check(!calendar.detailRoot.Find("Button/CompleteQuest").GetComponentInChildren<Button>().interactable,"Future Daily can be completed");
        calendar.CloseDrawer();
        owner.CompleteForDate(daily,TaskService.DateKey(now.AddDays(-1)));
        Check(owner.HasTaskModal&&!owner.Service.Snapshot.history.Any(h=>h.taskId==daily&&!h.revoked),"Stale daily command settled a new day");
        ((TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner)).Dismiss();
        calendar.search.text="no such task";yield return .2;calendar.SelectDate(now);yield return .2;
        Check(calendar.emptyLabel.gameObject.activeInHierarchy&&calendar.emptyLabel.text.Contains("matching"),"Filtered empty state missing");
        calendar.ClearFilters();calendar.CloseDrawer();
        var motion = new GameObject("Calendar motion capture").AddComponent<CalendarMotionCapture>();motion.Begin(Folder+"/motion");
        var button=calendar.today;var feedback=button.GetComponent<JuicyButton>();var bounds=ScreenBounds((RectTransform)button.transform);
        feedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .4;
        Check(feedback.DrawScale.y>1.005f,"Hover feedback missing");capture.Begin(Path.GetFullPath(Folder+"/motion-hover.png"));yield return .2;
        feedback.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return .3;
        Check(feedback.DrawScale.y<.98f,"Press feedback missing");capture.Begin(Path.GetFullPath(Folder+"/motion-press.png"));yield return .2;
        feedback.OnPointerUp(new PointerEventData(EventSystem.current));feedback.OnPointerExit(new PointerEventData(EventSystem.current));feedback.OnDeselect(new BaseEventData(EventSystem.current));yield return .6;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.005f,"Feedback did not settle");
        Check(Vector2.Distance(ScreenBounds((RectTransform)button.transform).size,bounds.size)<1,"Feedback changed hit area");
        for(int i=0;i<5;i++){feedback.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});feedback.OnPointerUp(new PointerEventData(EventSystem.current));}
        feedback.OnPointerExit(new PointerEventData(EventSystem.current));yield return .4;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f,"Rapid input accumulated motion");
        button.interactable=false;feedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .2;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f,"Disabled feedback did not reset");button.interactable=true;
        feedback.OnSelect(new BaseEventData(EventSystem.current));yield return .3;Check(feedback.DrawScale.y>1.005f,"Keyboard selection feedback missing");
        feedback.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.005f,"Focus-loss feedback did not reset");
        nav.ShowPage(MainMenuPage.Dashboard);yield return .2;nav.ShowPage(MainMenuPage.Tasks);yield return .4;
        Check(calendar.Active,"Calendar mode lost when returning to Tasks");
        calendar.SetMode(TaskDisplayMode.List);yield return .2;
        Check(owner.tabButtons.All(b=>b.gameObject.activeInHierarchy),"List tabs not restored");
        calendar.SetMode(TaskDisplayMode.Calendar);yield return .2;
        foreach(var control in new[]{calendar.listMode,calendar.calendarMode,calendar.previous,calendar.next,calendar.filter,calendar.noDateButton,calendar.add})
        {
            var effect=control.GetComponent<JuicyButton>();
            Check(effect,"Missing feedback on "+control.name);
            var hitSize=ScreenBounds((RectTransform)control.transform).size;
            effect.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .2;
            Check(effect.DrawScale.y>1.005f,"Missing hover on "+control.name);
            effect.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return .2;
            Check(effect.DrawScale.y<.98f,"Missing press on "+control.name);
            effect.OnPointerUp(new PointerEventData(EventSystem.current));effect.OnPointerExit(new PointerEventData(EventSystem.current));
            effect.OnDeselect(new BaseEventData(EventSystem.current));yield return .5;
            Check(Vector2.Distance(effect.DrawScale,Vector2.one)<.01f,"Feedback failed to settle on "+control.name);
            Check(Vector2.Distance(ScreenBounds((RectTransform)control.transform).size,hitSize)<1,"Feedback moved hit area on "+control.name);
            control.interactable=false;effect.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .2;
            Check(Vector2.Distance(effect.DrawScale,Vector2.one)<.01f,"Disabled feedback on "+control.name);control.interactable=true;
            effect.OnSelect(new BaseEventData(EventSystem.current));yield return .2;
            Check(effect.DrawScale.y>1.005f,"Keyboard feedback missing on "+control.name);
            effect.OnSubmit(new BaseEventData(EventSystem.current));
            effect.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
            Check(Vector2.Distance(effect.DrawScale,Vector2.one)<.005f,"Focus reset failed on "+control.name);
        }
        while(motion.Pending) yield return .1;
    }
}

// Bounded recording of actual Play Mode frames; no authored assets are changed.
public sealed class CalendarMotionCapture : MonoBehaviour
{
    public bool Pending {get;private set;}
    public void Begin(string folder){Directory.CreateDirectory(folder);Pending=true;StartCoroutine(Capture(folder));}
    IEnumerator Capture(string folder)
    {
        try {
            for(int i=0;i<48;i++){
                yield return new WaitForEndOfFrame();
                var texture=ScreenCapture.CaptureScreenshotAsTexture();
                try{File.WriteAllBytes(folder+"/"+i.ToString("D3")+".png",texture.EncodeToPNG());}finally{Destroy(texture);}
                yield return new WaitForSecondsRealtime(.05f);
            }
        } finally{Pending=false;}
    }
}
