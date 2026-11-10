using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Uses TaskTabsVerification's bounded, isolated and restored Play Mode lifecycle.
public static class UsabilityRoundTwoVerification
{
    static string folder;
    static RewardScreenshotCapture capture;
    public static void Check(bool value,string message) { if(!value)throw new Exception(message);File.AppendAllText(folder+"/checks.txt","PASS "+message+"\n"); }
    public static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{width,height});
    public static IEnumerator Capture(string name) { capture.Begin(Path.GetFullPath(folder+"/"+name+".png"));while(capture.Pending)yield return .05; }
    static TaskConfirmDialog Dialog(TaskSystemUI owner)=>(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static Button DialogButton(TaskSystemUI owner,string name)=>Dialog(owner).Root.transform.Find("Popup/"+name).GetComponent<Button>();
    static TMP_Text DialogMessage(TaskSystemUI owner)=>Dialog(owner).Root.transform.Find("Popup/Text_Info").GetComponent<TMP_Text>();
    public static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)button.transform;var canvas=button.GetComponentInParent<Canvas>().rootCanvas;
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center))};
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"Raycast reaches "+button.name);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator DeleteChecks(MainMenuNavigation nav,GameDataManager manager,string daily)
    {
        var owner=nav.taskSystem;var service=manager.Tasks;
        Resolution(1920,1080);nav.ShowPage(MainMenuPage.Tasks);owner.SelectTab(TaskTab.Daily);owner.SelectTask(daily);yield return .3;
        string unchanged=JsonUtility.ToJson(service.Snapshot);
        Click(owner.detailsRoot.Find("Button/DeleteButton").GetComponentInChildren<Button>(true));yield return .2;
        string listMessage=DialogMessage(owner).text;
        Check(listMessage.Contains("Read one chapter every day")&&listMessage.Contains("all future repeats")&&listMessage.Contains("history and rewards"),"List deletion explains the named recurring task and retained history/rewards");
        Check(EventSystem.current.currentSelectedGameObject==DialogButton(owner,"Cancel").gameObject,"Deletion defaults to Cancel");
        Click(DialogButton(owner,"Cancel"));Check(JsonUtility.ToJson(service.Snapshot)==unchanged,"Cancel deletion changes no data");
        owner.calendar.SetMode(TaskDisplayMode.Calendar);
        var day=service.LocalNow.Date;var item=service.QueryCalendar(day,day).days[day].items.First(i=>i.view.task.id==daily);
        owner.calendar.OpenItem(item);yield return .3;
        Click(owner.calendar.detailRoot.Find("Button/DeleteButton").GetComponentInChildren<Button>(true));yield return .2;
        Check(DialogMessage(owner).text==listMessage,"Calendar and List use the same deletion explanation");
        Click(DialogButton(owner,"Cancel"));owner.calendar.CloseDrawer();owner.calendar.SetMode(TaskDisplayMode.List);
        Check(service.Complete(daily,true),"Complete isolated recurring task before deletion");yield return 3;
        var completed=service.Snapshot;string history=JsonUtility.ToJson(new HistorySnapshot{items=completed.history});
        owner.CalendarDelete(daily);yield return .2;
        using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            Click(DialogButton(owner,"Confirm"));
            Check(service.Snapshot.tasks.Any(t=>t.id==daily),"Failed deletion keeps task");
            Check(JsonUtility.ToJson(service.Snapshot)==JsonUtility.ToJson(completed),"Failed deletion keeps all committed rewards/history");
        }
        Dialog(owner).Dismiss();owner.CalendarDelete(daily);yield return .2;
        var confirm=DialogButton(owner,"Confirm");Click(confirm);confirm.onClick.Invoke();
        var reloaded=new PlayerDataStore(manager.SavePath).Data;
        Check(!reloaded.tasks.Any(t=>t.id==daily)&&JsonUtility.ToJson(new HistorySnapshot{items=reloaded.history})==history,"Confirmed deletion removes template but retains history on reload");
        Check(reloaded.experience==completed.experience&&reloaded.fishCoins==completed.fishCoins&&reloaded.tickets==completed.tickets,"Deletion and duplicate confirm preserve earned rewards");
        Check(!service.QueryCalendar(day.AddDays(1),day.AddDays(7)).days.Values.SelectMany(d=>d.items).Any(i=>i.view.task.id==daily),"Deleted recurring task has no future previews");
        Check(service.Upsert(new QuestTask{title="One-time errand"},null,out var once),"Seed one-time deletion prompt");
        owner.CalendarDelete(once);yield return .2;
        Check(DialogMessage(owner).text.Contains("One-time errand")&&!DialogMessage(owner).text.Contains("future repeats"),"One-time deletion does not imply recurrence");
        Dialog(owner).Dismiss();
        Check(service.Upsert(new QuestTask{title="<b>"+new string('W',97),daily=true,scheduled=true,date=TaskService.DateKey(day)},null,out var longId),"Seed maximum-length task name");
        foreach(int width in new[]{1920,1280}) {
            Resolution(width,width==1920?1080:720);yield return .3;owner.CalendarDelete(longId);yield return .2;
            var message=DialogMessage(owner);message.ForceMeshUpdate();
            Check(!message.isTextOverflowing&&message.GetParsedText().Contains("<b>")&&message.text.Contains("all future repeats"),"Long task name remains literal; consequences fit at "+width);
            var shot=Capture("daily-delete-long-"+width);while(shot.MoveNext())yield return shot.Current;
            Dialog(owner).Dismiss();
        }
    }
    [Serializable] sealed class HistorySnapshot { public System.Collections.Generic.List<TaskCompletion> items; }
    public static IEnumerator Run(string output,bool before)
    {
        folder=output;File.WriteAllText(folder+"/checks.txt",DateTime.UtcNow.ToString("O")+" Unity "+Application.unityVersion+"\n");
        UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");yield return 2;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var manager=GameDataManager.EnsureInstance();var service=manager.Tasks;
        Check(manager.SavePath.Contains("UsabilityRoundTwoVerification"),"Isolated save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        capture=new GameObject("Usability round two capture").AddComponent<RewardScreenshotCapture>();
        var task=nav.taskSystem;
        Check(service.Upsert(new QuestTask{title="Read one chapter every day",daily=true,scheduled=true,date=TaskService.DateKey(service.LocalNow)},null,out var daily),"Seed Daily task");
        manager.PlayerData.equipmentUnlocked=true;manager.PlayerData.fishCoins=500;
        manager.PlayerData.bossProgress[0].wins=15;manager.PlayerData.bossProgress[0].pendingClaims=3;manager.SaveData();
        Check(service.ClaimEquipment("boss1-weapon"),"Seed owned equipment through real claim");
        yield return 3;
        foreach(int width in new[]{1920,1280}) {
            Resolution(width,width==1920?1080:720);yield return .5;
            nav.ShowPage(MainMenuPage.Tasks);yield return .4;
            if(!before&&width==1920)TaskContextRetentionVerificationChecks.FirstEntry(task,Check);
            task.SelectTab(TaskTab.Daily);yield return .2;
            task.CalendarDelete(daily);yield return .3;
            var shot=Capture("daily-delete-"+width);while(shot.MoveNext())yield return shot.Current;
            Dialog(task).Dismiss();
            nav.ShowPage(MainMenuPage.CatHome);yield return .5;
            var equipment=nav.pages[(int)MainMenuPage.CatHome].GetComponent<EquipmentPageUI>();
            equipment.OpenItem("boss1-weapon");yield return .3;
            shot=Capture("equipment-detail-"+width);while(shot.MoveNext())yield return shot.Current;
            equipment.OpenClaim(1);yield return .3;
            shot=Capture("equipment-claim-"+width);while(shot.MoveNext())yield return shot.Current;
            equipment.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();
        }
        if(before)yield break;
        var checks=DeleteChecks(nav,manager,daily);while(checks.MoveNext())yield return checks.Current;
        foreach(var wait in TaskContextRetentionVerificationChecks.Run(task,nav,Check))yield return wait;
        var equipmentChecks=EquipmentModalVerificationChecks.Run(nav,manager,Check);
        try { while(equipmentChecks.MoveNext())yield return equipmentChecks.Current; }
        finally { (equipmentChecks as IDisposable)?.Dispose(); }
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        nav.ShowPage(MainMenuPage.Tasks);Resolution(1920,1080);yield return .5;
        foreach(var wait in SidebarMotionVerificationChecks.Run(sidebar,Check))yield return wait;
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        toggle.isOn=false;yield return .4;
        toggle.isOn=true;
        for(int frame=0;frame<5;frame++) {
            var motion=Capture("sidebar-expand-frame-"+frame);while(motion.MoveNext())yield return motion.Current;
            yield return .03;
        }
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})foreach(bool expanded in new[]{false,true}) {
            Resolution(size.x,size.y);toggle.isOn=expanded;yield return .55;
            TaskTabsVerification.Viewport(task,size);
            var shot=Capture("tasks-layout-"+size.x+"-"+expanded);while(shot.MoveNext())yield return shot.Current;
            Click(nav.buttons[(int)MainMenuPage.CatHome]);yield return .3;
            shot=Capture("equipment-layout-"+size.x+"-"+expanded);while(shot.MoveNext())yield return shot.Current;
            Click(nav.buttons[(int)MainMenuPage.Tasks]);yield return .3;
        }
        var missing=nav.GetComponentInParent<Canvas>().rootCanvas.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        Check(missing==0,"No missing scripts in rendered application UI");
    }
}
