using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FocusStatisticsVerification
{
    const string Flag="CatQuest.StatisticsVerification";
    static IEnumerator flow;
    static double next;
    static RewardScreenshotCapture capture;
    static string Folder=>SessionState.GetString(Flag+".folder","");
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo Size=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object Group()=>typeof(FocusLayoutVerification).GetMethod("SizeGroup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    static int Count(object g,string name)=>(int)g.GetType().GetMethod(name).Invoke(g,null);
    static FocusStatisticsVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){flow=Run();next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode){
                if(!File.Exists(Folder+"/result.txt"))File.WriteAllText(Folder+"/result.txt","INCOMPLETE: Play Mode ended before the verification finished.");
                (flow as IDisposable)?.Dispose();flow=null;
                Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
                string scene=SessionState.GetString(Flag+".scene","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
                Size.SetValue(View,SessionState.GetInt(Flag+".size",0));var group=Group();int builtin=Count(group,"GetBuiltinCount");
                for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
                EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);SessionState.SetBool(Flag,false);
                File.WriteAllText(Folder+"/done.txt","Restored test save environment, start scene, pause and Game View. No live save or test scene saved.");
            }
        };
    }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying&&SessionState.GetBool(Flag,false))
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+".deadline","0"))){Finish("FAIL: timeout");return;}
            if(flow==null||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try{if(flow.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(flow.Current);else Finish("PASS: "+(SessionState.GetBool(Flag+".uiOnly",false)?"UI-only; domain checks intentionally excluded from this run. ":"statistics boundaries/legacy/DST/deduplication and Focus domain regression; ")+"day/week/month/year stacked graphs, distinct matching legend/mesh colours, segment hover, optional tag filtering, keyboard and raycast controls, empty/reopen, long tags, foreground icon hints, relative tag bars; 1920x1080, 1280x720, 1024x768 and 2560x1080 with both sidebar states. Screenshots and button motion captured.");}
            catch(Exception e){Finish("FAIL: "+e);Debug.LogException(e);}return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-focus-statistics.request"))return;
        bool uiOnly=File.ReadAllText("Temp/verify-focus-statistics.request").Trim()=="ui-only";
        SessionState.SetBool(Flag+".uiOnly",uiOnly);File.Delete("Temp/verify-focus-statistics.request");
        string folder="Library/FocusStatisticsVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);SessionState.SetString(Flag+".folder",folder);
        try{if(!uiOnly){DataChecks();FocusImplementationVerification.Run();}File.WriteAllText(folder+"/domain.txt",uiOnly?"SKIPPED explicitly: ui-only request; run the relevant domain aggregate separately.":"PASS "+DateTime.UtcNow.ToString("O")+": boundaries, actual duration, duplicates, cancellation, old saves, tag identity, DST and FocusImplementationVerification aggregate.");}
        catch(Exception e){File.WriteAllText(folder+"/result.txt","FAIL domain: "+e);return;}
        string previous=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");SessionState.SetBool(Flag+".hadStore",previous!=null);SessionState.SetString(Flag+".store",previous??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetInt(Flag+".size",(int)Size.GetValue(View));SessionState.SetInt(Flag+".count",Count(Group(),"GetCustomCount"));SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(180).Ticks.ToString());SessionState.SetBool(Flag,true);
        File.WriteAllText(folder+"/save.json",JsonUtility.ToJson(new PlayerData{experience=2025,tutorial=new TutorialProgress{version=1,dismissed=true,taskStep=4,announcedLevel=99}}));
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(folder+"/save.json"));EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
    }
    static void Finish(string message){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+" | Unity "+Application.unityVersion+"\n"+message);EditorApplication.isPlaying=false;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Near(double value,double expected,string why)=>Check(Math.Abs(value-expected)<.001,why+": "+value+" expected "+expected);
    static FocusSession Receipt(string id,DateTime end,double seconds,string tag="study")=>new FocusSession{id=id,status=FocusSessionStatus.Completed,completedUtc=DateTime.SpecifyKind(end,DateTimeKind.Utc).ToString("O"),elapsedSeconds=seconds,eligibleSeconds=Math.Min(10800,seconds),tagIds=tag==null?null:new List<string>{tag},tagNames=tag==null?null:new List<string>{"Study"}};
    static void DataChecks()
    {
        var date=new DateTime(2026,10,30);var catalog=new[]{new QuestTag{id="study",name="Renamed study",colorHex="#3D5FC3"}};
        var cross=Receipt("cross",date.AddMinutes(30),3600);var capped=Receipt("cap",date.AddHours(12),14400);var early=Receipt("early",date.AddHours(15),1800,null);early.status=FocusSessionStatus.EndedEarly;
        var cancelled=Receipt("cancel",date.AddHours(17),3600);cancelled.status=FocusSessionStatus.Cancelled;
        var running=Receipt("running",date.AddHours(18),3600);running.status=FocusSessionStatus.Running;
        var invalid=Receipt("invalid",date.AddHours(19),3600);invalid.status=FocusSessionStatus.Invalid;
        var data=new[]{cross,capped,early,cancelled,running,invalid,capped};
        var result=FocusStatistics.Query(data,catalog,date,FocusStatisticsPeriod.Day,TimeZoneInfo.Utc);
        Near(result.seconds,18000,"Actual capped duration / status / duplicate filter");Near(result.buckets.Sum(),result.seconds,"Day conservation");Check(result.sessions==3&&result.buckets.Length==24,"Day session/bucket count");
        Near(result.tags.Single(t=>t.id=="study").seconds,16200,"Tag total");Check(result.tags[0].name=="Renamed study","Stable tag identity on rename");
        Near(FocusStatistics.Query(data,catalog,date.AddDays(-1),FocusStatisticsPeriod.Day,TimeZoneInfo.Utc).seconds,1800,"Midnight split");
        foreach(FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod))){var r=FocusStatistics.Query(data,catalog,date,period,TimeZoneInfo.Utc);Near(r.buckets.Sum(),r.seconds,"Bucket conservation "+period);}
        Check(FocusStatistics.Start(new DateTime(2026,11,1),FocusStatisticsPeriod.Week)==new DateTime(2026,10,26),"Monday-based week");
        Check(FocusStatistics.Query(data,catalog,new DateTime(2024,2,3),FocusStatisticsPeriod.Month,TimeZoneInfo.Utc).buckets.Length==29,"Leap month");
        Check(FocusStatistics.Move(new DateTime(2026,12,31),FocusStatisticsPeriod.Month,1)==new DateTime(2027,1,1),"Year rollover");
        var old=Receipt("old",date.AddHours(2),0,null);old.eligibleSeconds=0;old.eligibleMinutes=25;
        Near(FocusStatistics.Query(new[]{old},null,date,FocusStatisticsPeriod.Day,TimeZoneInfo.Utc).seconds,1500,"Legacy time fallback");
        Check(FocusStatistics.Query(new[]{cross},null,date,FocusStatisticsPeriod.Day,TimeZoneInfo.Utc).tags.Single().name=="Study","Deleted tag snapshot");
        var json=JsonUtility.ToJson(new PlayerData{focusSessions=data.ToList()});var copy=JsonUtility.FromJson<PlayerData>(json);
        Near(FocusStatistics.Query(copy.focusSessions,catalog,date,FocusStatisticsPeriod.Day,TimeZoneInfo.Utc).seconds,18000,"Reload");Check(JsonUtility.ToJson(copy)==json,"Query mutated receipts");
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        foreach(var day in new[]{new DateTime(2026,3,8),new DateTime(2026,11,1)}){
            var start=TimeZoneInfo.ConvertTimeToUtc(day,zone);var end=TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1),zone);var r=FocusStatistics.Query(new[]{Receipt("dst",end,(end-start).TotalSeconds)},null,day,FocusStatisticsPeriod.Day,zone);Near(r.buckets.Sum(),(end-start).TotalSeconds,"DST conservation");Near(r.buckets[day.Month==11?1:2],day.Month==11?7200:0,"DST local hour attribution");}
    }
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{width,height});
    static void Capture(string name){if(!capture)capture=new GameObject("Statistics capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static PointerEventData Pointer(Transform transform)
    {
        var rect=(RectTransform)transform;return new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
    }
    static void Click(Button button)
    {
        var p=Pointer(button.transform);var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(p,hits);
        Check(hits.Count>0&&ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"Blocked statistics button "+button.name+": "+string.Join(",",hits.Select(h=>h.gameObject.name)));
        ExecuteEvents.Execute(button.gameObject,p,ExecuteEvents.pointerDownHandler);ExecuteEvents.Execute(button.gameObject,p,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(button.gameObject,p,ExecuteEvents.pointerClickHandler);ExecuteEvents.Execute(button.gameObject,p,ExecuteEvents.pointerExitHandler);
    }
    static IEnumerator Run()
    {
        var manager=GameDataManager.EnsureInstance();Check(manager.SavePath.Contains("FocusStatisticsVerification"),"Isolated save required");
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Focus);yield return .5;
        var ui=nav.pages[2].GetComponentInChildren<FocusStatisticsUI>(true);Check(ui,"Statistics prefab missing");
        var focus=nav.pages[2].GetComponent<FocusPanelUI>();
        var data=manager.PlayerData;data.tags=new List<QuestTag>{new QuestTag{id="study",name="Study",colorHex="#3D5FC3"},new QuestTag{id="project",name="Final year project",colorHex="#3D5FC3"},new QuestTag{id="reading",name="Reading",colorHex="#3D5FC3"}};data.focusSessions.Clear();
        var now=manager.Tasks.LocalNow.Date;
        for(int d=0;d<35;d++)for(int tag=0;tag<3;tag++){
            var end=now.AddDays(-d).AddHours(8+tag*3).ToUniversalTime();var receipt=Receipt("fixture-"+d+"-"+tag,end,(20+(d*7+tag*13)%85)*60,data.tags[tag].id);receipt.tagNames=new List<string>{data.tags[tag].name};data.focusSessions.Add(receipt);}
        for(int month=1;month<12;month++)data.focusSessions.Add(Receipt("month-"+month,now.AddMonths(-month).AddHours(12).ToUniversalTime(),(month+2)*3600,"project"));
        manager.SaveData();
        Check(File.Exists(manager.SavePath),"Fixture persistence failed");
        foreach(int width in new[]{1920,1280,1024,2560})
        {
            int height=width==1280?720:width==1024?768:1080;
            Resolution(width,height);yield return .5;
            Capture("focus-entry-"+width);yield return .1;
            Click(ui.open);yield return .3;Check(ui.panel.activeSelf,"Statistics not open");
            Check(EventSystem.current.currentSelectedGameObject==null,"Opening insights selected a control without keyboard navigation");
            var closeHint=ui.close.GetComponent<ControlHint>();
            Check(!closeHint.hint.activeSelf,"Close hint appeared without pointer hover or keyboard navigation");
            Capture("open-no-close-hint-"+width);yield return .1;
            var priorKeyboard=Keyboard.current;
            var testKeyboard=InputSystem.AddDevice<Keyboard>();
            try {
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(Key.Tab));yield return .2;
                Check(EventSystem.current.currentSelectedGameObject==ui.close.gameObject&&closeHint.hint.activeSelf,"Tab did not start keyboard navigation with a visible Close hint");
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState());yield return .1;
            } finally { InputSystem.RemoveDevice(testKeyboard);if(priorKeyboard!=null&&priorKeyboard.added)priorKeyboard.MakeCurrent(); }
            ui.Hide();ui.Show();yield return .2;
            Check(EventSystem.current.currentSelectedGameObject==null&&!closeHint.hint.activeSelf,"Reopening retained Close focus or hint");
            Check(!ui.panel.transform.Find("Card/AboutData") && !ui.panel.transform.Find("Card/Note"), "Removed explanation UI still exists");
            foreach(FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod)))
            {
                Click(ui.periods[(int)period]);yield return .2;
                Check(ui.Period==period&&ui.Data.seconds>0&&!ui.next.interactable,"Period / future navigation");
                Check(!ui.range.isTextOverflowing&&!ui.total.isTextOverflowing,"Statistics text clipped");
                Check(ui.graph.SeriesCount==ui.Data.tags.Count&&ui.graph.SeriesCount==3,"All tags not shown together by default");
                Check(ui.sessionCount.text==ui.Data.sessions.ToString(CultureInfo.InvariantCulture),"Session metric does not match committed data");
                foreach(var tagRow in ui.tagContent.GetComponentsInChildren<Button>()) {
                    var series=ui.Data.tags.Single(t=>t.id==tagRow.name);
                    Check(tagRow.transform.Find("Duration").GetComponent<TMPro.TMP_Text>().text==FocusStatistics.Duration(series.seconds),"Tag duration missing");
                    Near(((RectTransform)tagRow.transform.Find("TimeTrack/Fill")).anchorMax.x,series.seconds/ui.Data.tags[0].seconds,"Relative tag time bar");
                }
                var legend=ui.tagContent.GetComponentsInChildren<Button>().Select(b=>(Color32)b.transform.Find("Swatch").GetComponent<Image>().color).ToArray();
                Check(legend.Distinct().Count()==3,"Same-colour tags were not distinguished");
                var mesh=ui.graph.canvasRenderer.GetMesh();
                try {
                    foreach(var tint in legend)Check(mesh.colors32.Contains(tint),"Legend colour missing from graph mesh");
                    double peak=Enumerable.Range(0,ui.Data.buckets.Length).Max(i=>ui.Data.tags.Sum(t=>t.buckets[i]));
                    Near(ui.graph.Maximum,Math.Max(3600,Math.Ceiling(peak/3600)*3600),"Stacked scale");
                } finally { UnityEngine.Object.Destroy(mesh); }
                Capture(period.ToString().ToLowerInvariant()+"-"+width);yield return .1;
                if(period==FocusStatisticsPeriod.Week) {
                    var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
                    var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
                    bool prior=toggle.isOn;
                    foreach(bool expanded in new[]{false,true}) {
                        toggle.isOn=expanded;yield return .5;
                        var viewport=TaskCalendarPlayVerification.Viewport(ui,new Vector2Int(width,height));
                        foreach(var element in new Component[]{ui.range,ui.total,ui.sessionCount,ui.graph,ui.close,ui.today,ui.allTags}) {
                            var corners=new Vector3[4];((RectTransform)element.transform).GetWorldCorners(corners);
                            foreach(var corner in corners)Check(viewport.Contains(RectTransformUtility.WorldToScreenPoint(null,corner)),"Insights element outside viewport: "+element.name);
                        }
                        Check(!ui.range.isTextOverflowing&&!ui.total.isTextOverflowing,"Expanded sidebar clips metrics");
                        foreach(var periodButton in ui.periods) {
                            var caption=periodButton.transform.Find("Text").GetComponent<TMPro.TMP_Text>();
                            Check(!caption.isTextOverflowing&&caption.textInfo.lineCount==1,"Period caption wraps or clips");
                        }
                        Capture("overview-"+width+(expanded?"-expanded":"-collapsed"));yield return .1;
                    }
                    toggle.isOn=prior;yield return .3;
                }
            }
            Click(ui.periods[(int)FocusStatisticsPeriod.Week]);yield return .2;
            var first=ui.Data.tags[0];int bucket=Array.FindIndex(first.buckets,v=>v>0);
            var graphRect=ui.graph.rectTransform;var graphPoint=new Vector2(graphRect.rect.xMin+graphRect.rect.width*(bucket+.5f)/first.buckets.Length,
                graphRect.rect.yMin+1+(float)(first.buckets[bucket]/ui.graph.Maximum*.5)*(graphRect.rect.height-2));
            ui.graph.OnPointerMove(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,graphRect.TransformPoint(graphPoint))});
            Check(ui.detail.text.Contains(first.name)&&ui.detail.text.Contains(FocusStatistics.Duration(first.buckets[bucket])),"Segment hover did not identify tag and duration");
            Capture("stack-hover-"+width);yield return .1;
            ui.graph.OnPointerExit(new PointerEventData(EventSystem.current));
            var row=ui.tagContent.GetComponentsInChildren<Button>().First();Click(row);yield return .2;
            Check(ui.SelectedTag==row.name&&ui.chartTitle.text==ui.Data.tags.Single(t=>t.id==row.name).name,"Tag filter not applied");
            ui.graph.OnPointerMove(Pointer(ui.graph.transform));Check(ui.detail.text.Contains(" / "),"Graph hover missing");Capture("tag-filter-"+width);yield return .1;
            var currentTodayColor=ui.today.targetGraphic.color;
            Click(ui.previous);yield return .1;
            Check(ui.next.interactable,"Previous period cannot advance");
            Check(ui.today.targetGraphic.color!=currentTodayColor,"Today must change to its inactive colour outside the current period");
            Capture("today-away-"+width);yield return .1;
            foreach(var compact in new[]{ui.close,ui.previous,ui.next,ui.today,ui.allTags}) {
                var hint=compact.GetComponent<ControlHint>();Check(hint,"Compact control hint missing");
                var pointerEvent=Pointer(compact.transform);
                ExecuteEvents.Execute(compact.gameObject,pointerEvent,ExecuteEvents.pointerEnterHandler);yield return .2;
                Check(hint.hint.activeSelf&&hint.hint.GetComponent<Canvas>().overrideSorting,"Hint not foreground");
                Check(!hint.hint.GetComponentsInChildren<Graphic>().Any(g=>g.raycastTarget),"Hint intercepts clicks");
                if(compact==ui.today) { Capture("current-period-hint-"+width);yield return .1; }
                ExecuteEvents.Execute(compact.gameObject,pointerEvent,ExecuteEvents.pointerExitHandler);yield return .2;
                Check(!hint.hint.activeSelf,"Hint stuck after pointer exit");
                hint.OnSelect(new BaseEventData(EventSystem.current));Check(hint.hint.activeSelf,"Keyboard hint missing");
                hint.OnSubmit(new BaseEventData(EventSystem.current));Check(!hint.hint.activeSelf,"Hint stuck after submit");hint.OnDeselect(new BaseEventData(EventSystem.current));
            }
            var todayFeedback=ui.today.GetComponent<JuicyButton>();var todayPointer=Pointer(ui.today.transform);
            var todayRect=(RectTransform)ui.today.transform;var todayPosition=todayRect.anchoredPosition;
            todayFeedback.OnPointerEnter(todayPointer);yield return .2;Capture("today-hover-"+width);yield return .1;
            Check(todayFeedback.DrawScale.y>1,"Today hover missing");
            todayFeedback.OnPointerDown(todayPointer);yield return .2;Capture("today-press-"+width);yield return .1;
            Check(todayFeedback.DrawScale.y<1,"Today press missing");
            todayFeedback.OnPointerUp(todayPointer);todayFeedback.OnPointerExit(todayPointer);EventSystem.current.SetSelectedGameObject(null);yield return .7;
            Check(todayRect.anchoredPosition==todayPosition && Vector2.Distance(todayFeedback.DrawScale,Vector2.one)<.02f,"Today feedback did not settle");
            ui.today.OnSubmit(new BaseEventData(EventSystem.current));yield return .1;
            Check(ui.Date==manager.Tasks.LocalNow.Date && ui.today.targetGraphic.color==currentTodayColor,"Today must restore the date and selected colour");
            ui.allTags.OnSubmit(new BaseEventData(EventSystem.current));Check(ui.SelectedTag==null&&ui.graph.SeriesCount==3,"Keyboard All focus did not restore all series");
            Click(ui.close);yield return .1;Check(!ui.panel.activeSelf,"Back did not close");
            var juicy=ui.open.GetComponent<JuicyButton>();Check(juicy,"Missing shared feedback");var pointer=Pointer(ui.open.transform);var rect=(RectTransform)ui.open.transform;var pos=rect.anchoredPosition;
            EventSystem.current.SetSelectedGameObject(null);
            for(int frame=0;frame<18;frame++){
                if(frame==0)juicy.OnPointerEnter(pointer);if(frame==5){Check(juicy.DrawScale.y>1,"Hover feedback missing");juicy.OnPointerDown(pointer);}if(frame==10){Check(juicy.DrawScale.y<1,"Press feedback missing");juicy.OnPointerUp(pointer);juicy.OnPointerExit(pointer);EventSystem.current.SetSelectedGameObject(null);}
                Capture("motion-"+width+"-"+frame.ToString("D2"));yield return .05;}
            Check(rect.anchoredPosition==pos&&Vector2.Distance(juicy.DrawScale,Vector2.one)<.02f,"Motion did not reset / moved hit area");
            Click(ui.open);nav.ShowPage(MainMenuPage.Dashboard);nav.ShowPage(MainMenuPage.Focus);yield return .2;Check(!ui.panel.activeSelf,"Page reopen left statistics overlay active");
        }
        Resolution(1280,720);yield return .5;
        var legacy=manager.PlayerData.focusSessions.First();legacy.tagIds.Add("reading");legacy.tagNames.Add("Reading");manager.SaveData();
        Click(ui.open);yield return .2;
        Check(ui.Data.legacyMultipleTags && !ui.panel.transform.Find("Card/Note"),"Legacy receipts should remain supported without the removed explanation");
        Capture("legacy-1280");yield return .1;Click(ui.close);
        manager.PlayerData.tags.First(t=>t.id=="project").name="Research / literature review and dissertation preparation";manager.SaveData();
        Click(ui.open);yield return .2;
        var longRow=ui.tagContent.GetComponentsInChildren<Button>().Single(b=>b.name=="project");Click(longRow);yield return .2;
        Check(ui.chartTitle.text==manager.PlayerData.tags.First(t=>t.id=="project").name,"Long tag name lost");
        Check(longRow.transform.Find("Text").GetComponent<TMPro.TMP_Text>().overflowMode==TMPro.TextOverflowModes.Ellipsis,"Long tag must not wrap into its duration");
        Capture("long-tag-1280");yield return .1;Click(ui.close);
        manager.PlayerData.focusSessions.Clear();manager.SaveData();Click(ui.open);yield return .2;Check(ui.Data.seconds==0&&ui.empty.gameObject.activeSelf,"Empty state missing");Capture("empty-1280");yield return .1;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Buttons/Btn_MenuButton_Rectangle01-Group.prefab");
        var reference=UnityEngine.Object.Instantiate(source.transform.Find("Btn_MenuButton_Rectangle01_Light").gameObject,ui.panel.transform);
        var referenceRect=(RectTransform)reference.transform;referenceRect.anchorMin=referenceRect.anchorMax=new Vector2(.55f,.65f);referenceRect.anchoredPosition=Vector2.zero;referenceRect.localScale=Vector3.one*.5f;
        Capture("simplecasual-button-reference-1280");yield return .1;UnityEngine.Object.Destroy(reference);
        var closeSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Task/TaskFilterDialog.prefab").GetComponent<TaskFilterDialog>().close;
        var closeReference=UnityEngine.Object.Instantiate(closeSource.gameObject,ui.panel.transform);
        var closeRect=(RectTransform)closeReference.transform;closeRect.anchorMin=closeRect.anchorMax=new Vector2(.55f,.65f);closeRect.anchoredPosition=Vector2.zero;
        Capture("shared-close-reference-1280");yield return .1;UnityEngine.Object.Destroy(closeReference.gameObject);
        Click(ui.close);focus.questDropdown.onClick.Invoke();focus.questSearch.text="Study";focus.questList.GetComponentsInChildren<Button>().Single(b=>b.name=="study").onClick.Invoke();
        Check(focus.Controller.Options.tagIds.SequenceEqual(new[]{"study"})&&!focus.questPopup.gameObject.activeSelf,"Focus single tag selection");
        focus.questDropdown.onClick.Invoke();focus.questSearch.text="Reading";focus.questList.GetComponentsInChildren<Button>().Single(b=>b.name=="reading").onClick.Invoke();
        Check(focus.Controller.Options.tagIds.SequenceEqual(new[]{"reading"}),"Focus tag did not replace previous selection");
        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(ui.gameObject)==0,"Missing script");
    }
}
