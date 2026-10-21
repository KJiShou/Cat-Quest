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
public static class TaskTabsVerification
{
    const string Flag="CatQuest.TaskTabsVerification";
    const string Folder="Library/TaskTabsVerification";
    const string StoreVariable="CATQUEST_TASK_TEST_STORE";
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)};
    static IEnumerator run;
    static double next;
    static string context;
    static Vector2Int requestedSize;
    static RewardScreenshotCapture capture;
    static TaskTabsVerification()
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
                else Finish("PASS: header/Pet Widget viewport containment with 0/9999 balances and long player name, tab/content separation, wrapped subtask heights/nonoverlap, task editor and compressed search containment, all tab corner/center raycasts and selection, hover/press/reset and stable hit areas, keyboard selection/submit, disabled/re-enabled controls, page-close reset, simulated focus-loss reset, rapid sidebar changes; four resolutions with both sidebar states.");
            }
            catch(Exception e){Finish("FAIL at "+context+": "+e);}
            return;
        }
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-task-tabs.request"))return;
        File.Delete("Temp/verify-task-tabs.request");Directory.CreateDirectory(Folder);
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
    static void Layout(TaskSystemUI owner)
    {
        Canvas.ForceUpdateCanvases();
        var screen=Viewport(owner,requestedSize);
        var top=(RectTransform)owner.transform.parent.Find("TopInfo");
        Check(top&&top.gameObject.activeInHierarchy,"Shared header missing.");
        var status=top.Find("Status_Group");
        foreach(var path in new[]{"UserProfile/UserInfo","Status_Life","Status_Gem","Pet_Widget","Button_Setting"})
        {
            var item=status.Find(path) as RectTransform;
            Check(item,"Missing header item: "+path);
            if(item.gameObject.activeInHierarchy)Contained(screen,ScreenBounds(item),"Header item outside viewport: "+path);
        }
        foreach(var control in top.GetComponentsInChildren<Selectable>())
            Contained(screen,ScreenBounds((RectTransform)control.transform),"Header control outside viewport: "+control.name);
        var pet=top.GetComponentInChildren<PetWidgetToggle>();
        Check(pet&&pet.GetComponent<Toggle>().IsInteractable(),"Pet Widget toggle missing or disabled.");
        var petLabel=pet.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>().Single(t=>t.text=="Pet Widget");
        petLabel.ForceMeshUpdate();
        Contained(screen,ScreenBounds(petLabel.rectTransform),"Pet Widget caption outside viewport.");
        Check(!petLabel.isTextOverflowing,"Pet Widget caption clipped.");
        Hit((RectTransform)pet.transform,new Vector2(.5f,.5f),pet.gameObject,false);

        var bar=(RectTransform)owner.tabButtons[0].transform.parent;
        var header=(RectTransform)bar.parent;
        var content=(RectTransform)owner.transform.Find("Top");
        Check(content,"Task content missing.");
        foreach(var path in new[]{"Top","Top/TodayQuest","Top/TodayQuest/Content","Top/TodayQuest/Content/TaskList"})
        {
            var panel=owner.transform.Find(path) as RectTransform;
            Check(panel,"Missing task panel: "+path);
            Check(!Overlap(ScreenBounds(header),ScreenBounds(panel)),"Tab header overlaps task panel: "+path);
            Contained(screen,ScreenBounds(panel),"Task panel outside viewport: "+path);
        }
        var scroll=owner.listContent.GetComponentInParent<ScrollRect>();
        Check(scroll&&scroll.viewport,"Task scroll viewport missing.");
        Check(!Overlap(ScreenBounds(header),ScreenBounds(scroll.viewport)),"Tab header overlaps scroll viewport.");
        float width=((RectTransform)owner.tabButtons[0].transform).rect.width;
        float font=owner.tabLabels[0].fontSize;
        foreach(var button in owner.tabButtons)
        {
            var rect=(RectTransform)button.transform;
            Check(Mathf.Abs(rect.rect.width-width)<.2f,"Unequal tab widths.");
            Contained(ScreenBounds(header),ScreenBounds(rect),"Tab hit area exceeds header: "+button.name);
            Contained(screen,ScreenBounds(rect),"Tab outside viewport: "+button.name);
            Check(!Overlap(ScreenBounds(rect),ScreenBounds(content)),"Tab hit area overlaps content: "+button.name);
        }
        foreach(var label in owner.tabLabels)
        {
            Check(Mathf.Abs(label.fontSize-font)<.01f,"Tab fonts differ.");
            Check(!label.isTextOverflowing,"Tab caption clipped: "+label.text);
            Contained(ScreenBounds((RectTransform)label.transform.parent),ScreenBounds(label.rectTransform),"Caption leaves tab: "+label.text);
        }
        ActivePanel(owner.detailsRoot,"Button","ScrollRect",screen,ScreenBounds(header),ScreenBounds(top));
        ActivePanel((RectTransform)owner.editor.transform,"Footer","FormViewport",screen,ScreenBounds(header),ScreenBounds(top));
    }
    static void ActivePanel(RectTransform panel,string footerPath,string viewportPath,Rect screen,Rect tabs,Rect sharedHeader)
    {
        if(!panel||!panel.gameObject.activeInHierarchy)return;
        var bounds=ScreenBounds(panel);
        Contained(screen,bounds,"Active task panel outside viewport: "+panel.name);
        Check(!Overlap(tabs,bounds)&&!Overlap(sharedHeader,bounds),"Active task panel overlaps a header: "+panel.name);
        var viewport=panel.Find(viewportPath) as RectTransform;
        Check(viewport,"Active task panel viewport missing: "+panel.name);
        Contained(bounds,ScreenBounds(viewport),"Task panel viewport leaves its panel: "+panel.name);
        var footer=panel.Find(footerPath) as RectTransform;
        Check(footer,"Active task panel footer missing: "+panel.name);
        if(!footer.gameObject.activeInHierarchy)return;
        Contained(bounds,ScreenBounds(footer),"Task panel footer leaves its panel: "+panel.name);
        Check(!Overlap(ScreenBounds(footer),ScreenBounds(viewport)),"Task panel viewport overlaps its footer: "+panel.name);
        foreach(var control in footer.GetComponentsInChildren<Selectable>())
            Contained(ScreenBounds(footer),ScreenBounds((RectTransform)control.transform),"Footer control leaves footer: "+control.name);
    }
    internal static void SubtaskLayout(TaskSystemUI owner)
    {
        Canvas.ForceUpdateCanvases();
        var list=owner.detailsRoot.Find("ScrollRect/Content/SubTasks/TaskList");
        Rect? previous=null;
        foreach(Transform row in list)
        {
            if(!row.gameObject.activeInHierarchy)continue;
            var label=row.GetComponentInChildren<TMPro.TMP_Text>();
            label.ForceMeshUpdate();
            var bounds=ScreenBounds((RectTransform)row);
            Contained(bounds,ScreenBounds(label.rectTransform),"Subtask caption leaves its row.");
            Check(!label.isTextOverflowing,"Subtask caption clipped: "+label.text);
            Check(label.rectTransform.rect.height+.5f>=label.GetPreferredValues(label.text,label.rectTransform.rect.width,0).y,
                "Wrapped subtask text is taller than its allocated height.");
            if(previous.HasValue)Check(bounds.yMax<=previous.Value.yMin+.5f,"Subtask rows overlap.");
            previous=bounds;
        }
    }
    static void Hit(RectTransform rect,Vector2 fraction,GameObject expected,bool click)
    {
        var bounds=ScreenBounds(rect);
        var pointer=new PointerEventData(EventSystem.current){position=new Vector2(Mathf.Lerp(bounds.xMin,bounds.xMax,fraction.x),Mathf.Lerp(bounds.yMin,bounds.yMax,fraction.y)),button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0,"No raycast hit for "+expected.name+" at "+fraction);
        var target=ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        Check(target&&(target==expected||target.transform.IsChildOf(expected.transform)),"Blocked hit for "+expected.name+" by "+hits[0].gameObject.name+" at "+fraction);
        if(click)ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);
    }
    static void HeaderTextVariants(MainMenuNavigation nav)
    {
        var top=(RectTransform)nav.taskSystem.transform.parent.Find("TopInfo");
        var name=top.Find("Status_Group/UserProfile/UserInfo/Text").GetComponent<TMPro.TMP_Text>();
        string previousName=name.text,previousFish=nav.fishCoins.text,previousTickets=nav.tickets.text;
        try
        {
            foreach(var value in new[]{"0","9999"})
            {
                name.text="A Very Long Player Name For Header Layout Verification";
                nav.fishCoins.text=nav.tickets.text=value;
                LayoutRebuilder.ForceRebuildLayoutImmediate(top);
                Layout(nav.taskSystem);
                foreach(var balance in new[]{nav.fishCoins,nav.tickets})
                {
                    balance.ForceMeshUpdate();
                    Check(!balance.isTextOverflowing,"Header balance clipped: "+value);
                    Contained(ScreenBounds((RectTransform)balance.transform.parent),ScreenBounds(balance.rectTransform),"Header balance leaves its card.");
                }
            }
        }
        finally
        {
            name.text=previousName;nav.fishCoins.text=previousFish;nav.tickets.text=previousTickets;
            LayoutRebuilder.ForceRebuildLayoutImmediate(top);
        }
    }
    static void ClickTabs(TaskSystemUI owner)
    {
        var points=new[]{new Vector2(.06f,.1f),new Vector2(.94f,.1f),new Vector2(.06f,.9f),new Vector2(.94f,.9f),new Vector2(.5f,.5f)};
        for(int i=0;i<owner.tabButtons.Length;i++)foreach(var point in points)
        {
            owner.SelectTab((TaskTab)((i+1)%owner.tabButtons.Length));
            Hit((RectTransform)owner.tabButtons[i].transform,point,owner.tabButtons[i].gameObject,true);
            Check(owner.CurrentTab==(TaskTab)i,"Raycast click did not select tab "+i);
            for(int j=0;j<owner.tabFocus.Length;j++)Check(owner.tabFocus[j].activeSelf==(i==j),"Wrong selected tab underline.");
        }
    }
    static void StableHits(TaskSystemUI owner,Rect[] original)
    {
        for(int i=0;i<original.Length;i++)
        {
            var actual=ScreenBounds((RectTransform)owner.tabButtons[i].transform);
            Check(Vector2.Distance(actual.position,original[i].position)<1&&Vector2.Distance(actual.size,original[i].size)<1,"Feedback moved tab hit area "+i);
        }
    }
    static void ResetFeedback(JuicyButton[] feedback,string reason)
        =>Check(feedback.All(f=>Vector2.Distance(f.DrawScale,Vector2.one)<.005f&&Mathf.Abs(f.DrawOffset)<.1f&&f.DrawShine<.01f),reason);
    static IEnumerator Run()
    {
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(nav&&nav.taskSystem&&EventSystem.current,"CharacterHome navigation/input missing.");
        Check(Path.GetFullPath(GameDataManager.EnsureInstance().SavePath).StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Verification must use isolated save.");
        var owner=nav.taskSystem;
        Check(nav.ShowPage(MainMenuPage.Tasks),"Cannot open Tasks.");yield return 1;
        Check(owner.Service.Upsert(new QuestTask { title="Responsive task",
            subtasks=new List<QuestSubtask> {
                new QuestSubtask { id="layout-short",title="Short" },
                new QuestSubtask { id="layout-wrap",title="A long subtask should wrap into separate lines without covering the next row." },
                new QuestSubtask { id="layout-word",title=new string('W',100) }
            } },Array.Empty<string>(),out string layoutTask),owner.Service.LastError);
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        foreach(var size in Sizes)
        {
            requestedSize=size;
            typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
            foreach(bool expanded in new[]{false,true})
            {
                context=size.x+"x"+size.y+(expanded?" expanded":" collapsed");
                toggle.isOn=expanded;owner.SelectTab(TaskTab.History);yield return 1;
                Layout(owner);HeaderTextVariants(nav);ClickTabs(owner);
                owner.SelectTab(TaskTab.Todo);owner.SelectTask(layoutTask);yield return .5;
                Layout(owner);SubtaskLayout(owner);
                var edit=owner.detailsRoot.Find("Button/Edit Button").GetComponentInChildren<Button>();
                Hit((RectTransform)edit.transform,new Vector2(.5f,.5f),edit.gameObject,true);yield return .5;
                Check(owner.editor.gameObject.activeInHierarchy,"Task editor did not open.");
                Layout(owner);
                var searchBounds=ScreenBounds((RectTransform)owner.search.transform);
                var listTitle=(RectTransform)owner.transform.Find("Top/TodayQuest/Content/TaskList/Title");
                Contained(ScreenBounds(listTitle),searchBounds,"Compressed search field leaves the list header.");
                owner.CancelDraft();owner.SelectTab(TaskTab.History);yield return .5;
                var bounds=owner.tabButtons.Select(b=>ScreenBounds((RectTransform)b.transform)).ToArray();
                var feedback=owner.tabButtons.Select(b=>b.GetComponent<JuicyButton>()).ToArray();
                Check(feedback.All(f=>f&&f.isActiveAndEnabled),"A tab lacks shared feedback.");
                foreach(var f in feedback)f.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return .4;
                Check(feedback.All(f=>f.DrawScale.y>1.005f&&f.DrawOffset>.5f),"Tab hover feedback missing.");
                StableHits(owner,bounds);Layout(owner);
                foreach(var f in feedback)f.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                yield return .3;
                Check(feedback.All(f=>f.DrawScale.y<.98f&&f.DrawOffset<-.5f),"Tab press feedback missing.");
                StableHits(owner,bounds);
                EventSystem.current.SetSelectedGameObject(null);
                foreach(var f in feedback)
                {
                    f.OnPointerUp(new PointerEventData(EventSystem.current));
                    f.OnPointerExit(new PointerEventData(EventSystem.current));
                    f.OnDeselect(new BaseEventData(EventSystem.current));
                }
                yield return .6;
                ResetFeedback(feedback,"Tab feedback did not reset.");
                StableHits(owner,bounds);
                var history=owner.tabButtons[(int)TaskTab.History];
                owner.SelectTab(TaskTab.Today);EventSystem.current.SetSelectedGameObject(history.gameObject);yield return .4;
                Check(history.GetComponent<JuicyButton>().DrawScale.y>1.005f,"Keyboard selection feedback missing.");
                ExecuteEvents.Execute(history.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
                Check(owner.CurrentTab==TaskTab.History,"Keyboard submit did not select History.");
                EventSystem.current.SetSelectedGameObject(null);yield return .6;
                foreach(var button in owner.tabButtons)button.interactable=false;
                foreach(var f in feedback)
                {
                    f.OnPointerEnter(new PointerEventData(EventSystem.current));
                    f.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                }
                yield return .2;
                ResetFeedback(feedback,"Disabled tab retained active feedback.");
                for(int i=0;i<owner.tabButtons.Length;i++)
                {
                    var before=(TaskTab)((i+1)%owner.tabButtons.Length);owner.SelectTab(before);
                    ExecuteEvents.Execute(owner.tabButtons[i].gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
                    Check(owner.CurrentTab==before,"Disabled tab accepted a click: "+i);
                }
                owner.SelectTab(TaskTab.History);
                foreach(var button in owner.tabButtons)button.interactable=true;
                foreach(var f in feedback)f.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return .4;
                Check(feedback.All(f=>f.DrawScale.y>1.005f),"Re-enabled tab feedback did not recover.");
                // Exercise the lifecycle callback without claiming a real OS focus transition.
                foreach(var f in feedback)f.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
                ResetFeedback(feedback,"Simulated focus loss did not reset tab feedback.");
                StableHits(owner,bounds);
                foreach(var f in feedback)f.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return .4;
                Check(nav.ShowPage(MainMenuPage.Dashboard),"Cannot close Tasks page.");
                ResetFeedback(feedback,"Page closure did not reset tab feedback.");
                Check(nav.ShowPage(MainMenuPage.Tasks),"Cannot reopen Tasks page.");yield return .4;
                ResetFeedback(feedback,"Reopened Tasks page retained stale feedback.");
                Check(owner.tabButtons.All(b=>b.GetComponents<JuicyButton>().Length==1),"Page reopening duplicated feedback components.");
                StableHits(owner,bounds);
                Layout(owner);
                if(!capture)capture=new GameObject("Task Tab Capture").AddComponent<RewardScreenshotCapture>();
                capture.Begin(Path.GetFullPath(Folder+"/tabs-"+size.x+"x"+size.y+(expanded?"-expanded":"-collapsed")+".png"));
                yield return .2;
            }
            context=size.x+"x"+size.y+" rapid sidebar";
            for(int i=0;i<6;i++){toggle.isOn=!toggle.isOn;yield return .06;}
            toggle.isOn=true;yield return 1;
            Layout(owner);ClickTabs(owner);
        }
        owner.SelectTab(TaskTab.Todo);owner.SelectTask(layoutTask);yield return .5;
        owner.detailsRoot.Find("Button/Edit Button").GetComponentInChildren<Button>().onClick.Invoke();yield return .5;
        foreach(bool expanded in new[]{false,true})
        {
            toggle.isOn=expanded;yield return .5;
            foreach(var size in new[]{new Vector2Int(2560,1080),new Vector2Int(1024,768),new Vector2Int(1920,1080),new Vector2Int(1280,720)})
            {
                context="Resize open editor to "+size+(expanded?" expanded":" collapsed");
                requestedSize=size;
                typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
                yield return .8;
                Layout(owner);
            }
        }
        owner.CancelDraft();
        Check(owner.Service.Delete(layoutTask),"Could not remove isolated layout task.");
    }
}
