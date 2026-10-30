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
            try{if(flow.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(flow.Current);else Finish("PASS: statistics boundaries/legacy/DST/deduplication, Focus domain regression, day/week/month/year stacked graphs, distinct matching legend/mesh colours, segment hover, optional tag filtering, keyboard and raycast controls, empty/reopen, 1920x1080 and 1280x720. Screenshots and button motion captured.");}
            catch(Exception e){Finish("FAIL: "+e);Debug.LogException(e);}return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-focus-statistics.request"))return;
        File.Delete("Temp/verify-focus-statistics.request");
        string folder="Library/FocusStatisticsVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);SessionState.SetString(Flag+".folder",folder);
        try{DataChecks();FocusImplementationVerification.Run();File.WriteAllText(folder+"/domain.txt","PASS "+DateTime.UtcNow.ToString("O")+": boundaries, actual duration, duplicates, cancellation, old saves, tag identity, DST and FocusImplementationVerification aggregate.");}
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
        foreach(int width in new[]{1920,1280})
        {
            Resolution(width,width==1920?1080:720);yield return .5;
            Capture("focus-entry-"+width);yield return .1;
            Click(ui.open);yield return .3;Check(ui.panel.activeSelf,"Statistics not open");
            Check(ui.about&&!ui.note.gameObject.activeSelf&&ui.detail.text=="", "Normal statistics has unnecessary explanatory copy");
            Click(ui.about);yield return .2;Check(ui.note.gameObject.activeSelf&&ui.note.text.Contains("End Early")&&!ui.note.isTextOverflowing,"Statistics definition must remain accessible and fit");
            Capture("about-data-"+width);yield return .1;
            ui.about.OnSubmit(new BaseEventData(EventSystem.current));yield return .2;
            Check(!ui.note.gameObject.activeSelf,"Keyboard submit did not close statistics definition");
            foreach(FocusStatisticsPeriod period in Enum.GetValues(typeof(FocusStatisticsPeriod)))
            {
                Click(ui.periods[(int)period]);yield return .2;
                Check(ui.Period==period&&ui.Data.seconds>0&&!ui.next.interactable,"Period / future navigation");
                Check(!ui.range.isTextOverflowing&&!ui.total.isTextOverflowing&&!ui.note.isTextOverflowing,"Statistics text clipped");
                Check(ui.graph.SeriesCount==ui.Data.tags.Count&&ui.graph.SeriesCount==3,"All tags not shown together by default");
                var legend=ui.tagContent.GetComponentsInChildren<Button>().Select(b=>(Color32)b.transform.Find("Swatch").GetComponent<Image>().color).ToArray();
                Check(legend.Distinct().Count()==3,"Same-colour tags were not distinguished");
                var mesh=ui.graph.canvasRenderer.GetMesh();
                try {
                    foreach(var tint in legend)Check(mesh.colors32.Contains(tint),"Legend colour missing from graph mesh");
                    double peak=Enumerable.Range(0,ui.Data.buckets.Length).Max(i=>ui.Data.tags.Sum(t=>t.buckets[i]));
                    Near(ui.graph.Maximum,Math.Max(3600,Math.Ceiling(peak/3600)*3600),"Stacked scale");
                } finally { UnityEngine.Object.Destroy(mesh); }
                Capture(period.ToString().ToLowerInvariant()+"-"+width);yield return .1;
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
            Click(ui.previous);yield return .1;Check(ui.next.interactable,"Previous period cannot advance");Click(ui.today);yield return .1;
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
        var legacy=manager.PlayerData.focusSessions.First();legacy.tagIds.Add("reading");legacy.tagNames.Add("Reading");manager.SaveData();
        Click(ui.open);yield return .2;
        Check(ui.Data.legacyMultipleTags&&ui.note.gameObject.activeSelf&&ui.note.text.Contains("counts each session once"),"Legacy multi-tag warning must remain visible");
        Click(ui.about);yield return .2;Check(ui.note.text.Contains("End Early")&&!ui.note.isTextOverflowing,"Legacy and ordinary definitions fit together");Capture("legacy-about-1280");yield return .1;
        Click(ui.about);Check(ui.note.gameObject.activeSelf&&!ui.note.text.Contains("End Early"),"Legacy warning survives closing the ordinary definition");Click(ui.close);
        manager.PlayerData.focusSessions.Clear();manager.SaveData();Click(ui.open);yield return .2;Check(ui.Data.seconds==0&&ui.empty.gameObject.activeSelf,"Empty state missing");Capture("empty-1280");yield return .1;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Buttons/Btn_MenuButton_Rectangle01-Group.prefab");
        var reference=UnityEngine.Object.Instantiate(source.transform.Find("Btn_MenuButton_Rectangle01_Light").gameObject,ui.panel.transform);
        var referenceRect=(RectTransform)reference.transform;referenceRect.anchorMin=referenceRect.anchorMax=new Vector2(.55f,.65f);referenceRect.anchoredPosition=Vector2.zero;referenceRect.localScale=Vector3.one*.5f;
        Capture("simplecasual-button-reference-1280");yield return .1;UnityEngine.Object.Destroy(reference);
        Click(ui.close);focus.questDropdown.onClick.Invoke();focus.questSearch.text="Study";focus.questList.GetComponentsInChildren<Button>().Single(b=>b.name=="study").onClick.Invoke();
        Check(focus.Controller.Options.tagIds.SequenceEqual(new[]{"study"})&&!focus.questPopup.gameObject.activeSelf,"Focus single tag selection");
        focus.questDropdown.onClick.Invoke();focus.questSearch.text="Reading";focus.questList.GetComponentsInChildren<Button>().Single(b=>b.name=="reading").onClick.Invoke();
        Check(focus.Controller.Options.tagIds.SequenceEqual(new[]{"reading"}),"Focus tag did not replace previous selection");
        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(ui.gameObject)==0,"Missing script");
    }
}
