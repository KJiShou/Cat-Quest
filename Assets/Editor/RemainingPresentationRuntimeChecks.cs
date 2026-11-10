using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Behaviour checks inside the existing isolated runner; no authored asset changes.
public static class RemainingPresentationRuntimeChecks
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(object owner,string name)=>owner.GetType().GetField(name,Hidden).GetValue(owner);
    static IEnumerable<object> WaitArena(BossPageUI boss,Action<bool,string> check)
    {
        float until=Time.realtimeSinceStartup+20;
        while(boss.arena.IsLoading&&Time.realtimeSinceStartup<until)yield return .05;
        check(!boss.arena.IsLoading,"Arena request finishes within 20 seconds");
    }
    static Vector2 Origin(RewardFlightUI flights,RectTransform source)
    {
        var canvas=flights.GetComponentInParent<Canvas>().rootCanvas;
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)flights.transform,
            RectTransformUtility.WorldToScreenPoint(camera,source.TransformPoint(source.rect.center)),camera,out var point);
        return point;
    }
    static void CheckFlights(RewardFlightUI view,Vector2 expected,Action<bool,string> check)
    {
        var pool=(Array)Field(view,"flights");int count=0;
        foreach(object item in pool) {
            var type=item.GetType();if(!(bool)type.GetField("active").GetValue(item))continue;count++;
            check(Vector2.Distance((Vector2)type.GetField("origin").GetValue(item),expected)<.1f,"Committed reward starts at the completing control, including recycled rows");
        }
        check(count>0&&view.ActiveCount==count,"Positive completion creates bounded reward flights");
        check(Field(view,"frozenOrigin")==null,"Completion clears its scoped origin after settlement");
    }
    static Button TaskButton(TaskSystemUI tasks,string id)
    {
        var row=tasks.listContent.Cast<Transform>().First(t=>t.name=="Task_"+id&&t.gameObject.activeInHierarchy);
        return row.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Tick").parent.GetComponent<Button>();
    }
    static string Add(TaskService service,bool subtask,Action<bool,string> check)
    {
        var draft=new QuestTask{title="Completion feedback "+Guid.NewGuid().ToString("N").Substring(0,6),scheduled=true,date=TaskService.DateKey(service.LocalNow)};
        if(subtask)draft.subtasks.Add(new QuestSubtask{id=Guid.NewGuid().ToString("N"),title="Finish the final step"});
        check(service.Upsert(draft,Array.Empty<string>(),out var id),"Create isolated completion fixture");return id;
    }
    public static IEnumerator Run(MainMenuNavigation nav,GameDataManager manager,bool before,string output,Action<bool,string> check)
    {
        if(before)yield break;
        RemainingFixesVerification.Resolution(1920,1080);yield return .3;
        var gateType=typeof(BossArenaPreview).Assembly.GetType("PreviewPixels+ResizeGate",true);
        object gate=Activator.CreateInstance(gateType);var ready=gateType.GetMethod("Ready");
        var testTarget=new RenderTexture(100,100,0);
        bool Ready(int width)=>(bool)ready.Invoke(gate,new object[]{testTarget,new Vector2Int(width,100)});
        try {
            check(!Ready(200),"Resize begins with the existing target");yield return .1;
            check(!Ready(100),"Returning to current pixels cancels a pending resize");yield return .06;
            check(!Ready(200),"A repeated target after reversal must start a fresh stability interval");yield return .17;
            check(Ready(200),"Stable pixels become eligible after the debounce interval");
        }finally {UnityEngine.Object.Destroy(testTarget);}
        var service=manager.Tasks;var original=service.Snapshot;
        var store=(IPlayerDataStore)Field(manager,"store");
        var flights=nav.GetComponentInParent<Canvas>().rootCanvas.GetComponentInChildren<RewardFlightUI>(true);
        var boss=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        var arena=boss.arena;
        string path=(string)Field(arena,"oldForestStageResource");var oldForest=arena.oldForestStagePrefab;
        try {
            var settings=service.Settings;settings.reduceMotion=false;check(service.SaveSettings(settings),"Use ordinary reward presentation in isolated fixture");
            check(nav.ShowPage(MainMenuPage.Boss),"Open arena for asynchronous lifecycle verification");
            foreach(var wait in WaitArena(boss,check))yield return wait;
            int tickets=service.Tickets,battles=service.Snapshot.battles.Count;
            arena.oldForestStagePrefab=null;arena.lavaDungeonStagePrefab=null;
            boss.SelectStage(2);
            check(arena.IsLoading&&!boss.action.interactable&&boss.actionLabel.text.Contains("Loading"),"Cold arena request shows loading and disables Challenge");
            boss.Action();boss.Confirm();check(service.Tickets==tickets&&!boss.confirmation.activeSelf,"Loading cannot confirm an entry or spend a ticket");
            float peak=0;int frames=0,lastFrame=-1;
            Canvas.WillRenderCanvases sample=()=>{if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frames++;peak=Mathf.Max(peak,Time.unscaledDeltaTime);};
            Canvas.willRenderCanvases+=sample;
            var timer=System.Diagnostics.Stopwatch.StartNew();double dispatch;
            try {
                boss.SelectStage(3);dispatch=timer.Elapsed.TotalMilliseconds;
                foreach(var wait in WaitArena(boss,check))yield return wait;
                File.WriteAllText(output+"/arena-load.csv",string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "context,dispatchMilliseconds,readyMilliseconds,renderedFrames,largestFrameSeconds\nEditor-first-Demon-request,{0:F3},{1:F3},{2},{3:F6}\n",dispatch,timer.Elapsed.TotalMilliseconds,frames,peak));
            }finally {Canvas.willRenderCanvases-=sample;}
            check(arena.IsReady&&boss.actors.Ready&&boss.actors.Boss.name.StartsWith("Demon"),"Rapid stage switch ignores obsolete load completion");
            arena.oldForestStagePrefab=null;boss.SelectStage(2);check(arena.IsLoading,"Prepare in-flight page close");
            nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
            check(!arena.IsLoading&&(!arena.Stage||!arena.Stage.gameObject.activeSelf),"Closing during loading cancels presentation callback and keeps the arena hidden");
            nav.ShowPage(MainMenuPage.Boss);foreach(var wait in WaitArena(boss,check))yield return wait;
            check(arena.IsReady&&boss.actors.Ready,"Reopening after cancelled load produces valid actors");
            arena.oldForestStagePrefab=null;
            arena.GetType().GetField("oldForestStageResource",Hidden).SetValue(arena,"Missing/RemainingFixesArena");
            boss.SelectStage(2);foreach(var wait in WaitArena(boss,check))yield return wait;
            check(!arena.IsReady&&arena.LoadError!=null&&boss.action.interactable&&boss.actionLabel.text=="Retry Load","Missing arena displays an actionable retry state");
            var shot=RemainingFixesVerification.Capture("arena-load-error-1920");while(shot.MoveNext())yield return shot.Current;
            arena.GetType().GetField("oldForestStageResource",Hidden).SetValue(arena,path);
            boss.Action();foreach(var wait in WaitArena(boss,check))yield return wait;
            check(arena.IsReady&&boss.actors.Boss.name.StartsWith("Orc")&&service.Tickets==tickets&&service.Snapshot.battles.Count==battles,"Retry loads selected arena without fabricating or settling an entry");
            settings=service.Settings;settings.reduceMotion=true;check(service.SaveSettings(settings),"Enable reduced combat presentation");
            var cameraPosition=arena.StageCamera.transform.localPosition;
            boss.actors.StartCombat(service.CatStats,BattleRules.Boss(2));boss.actors.Hit(true);boss.actors.Hit(false);
            var health=boss.bossBar.GetComponent<PlayerHealthFeedback>();var healthScale=boss.bossBar.transform.localScale;
            health.SetHealth(.2f,true);yield return .1;
            check(!boss.actors.IsHitStopped&&Vector3.Distance(cameraPosition,arena.StageCamera.transform.localPosition)<.001f,"Reduced hits do not freeze attacks or shake the camera");
            check(Mathf.Abs(boss.bossBar.normalizedValue-.2f)<.001f&&Mathf.Abs(health.GhostValue-.2f)<.001f&&boss.bossBar.transform.localScale==healthScale,"Reduced health feedback shows committed HP without shake, punch or delayed ghost");
            var numbers=boss.GetComponent<BattleDamageNumbers>();numbers.Clear();numbers.Spawn(7,true,boss.actors.Cat,arena.StageCamera);
            var damage=boss.GetComponentsInChildren<TMPro.TMP_Text>(true).First(text=>text.name.StartsWith("DamageNumber")&&text.gameObject.activeSelf);
            var numberPosition=damage.rectTransform.anchoredPosition;yield return .2;
            check(damage.text=="−7"&&damage.rectTransform.anchoredPosition==numberPosition&&damage.rectTransform.localScale==Vector3.one,"Reduced damage remains readable without flying or scaling");
            shot=RemainingFixesVerification.Capture("reduced-combat-feedback-1920");while(shot.MoveNext())yield return shot.Current;
            numbers.Clear();boss.actors.ResetPose();health.SetHealth(1,false);
            string reducedTask=Add(service,false,check);check(service.Complete(reducedTask,true),"Reduced presentation still commits a real task reward");yield return .1;
            check(flights.ActiveCount==0&&flights.fishValue.text==service.FishCoins.ToString(),"Reduced reward presentation updates balances immediately without flight motion");
            var gains=(TMPro.TMP_Text[])Field(flights,"gains");check(gains.Any(label=>label.gameObject.activeSelf&&label.text.StartsWith("+")),"Reduced reward retains a visible positive amount");
            yield return 1.3;settings=service.Settings;settings.reduceMotion=false;check(service.SaveSettings(settings),"Restore normal reward presentation");
            check(nav.ShowPage(MainMenuPage.Tasks),"Open real Tasks completion controls");yield return .3;
            var tasks=nav.taskSystem;tasks.SelectTab(TaskTab.Today);tasks.search.text="Completion feedback";
            string id=Add(service,false,check);yield return .3;
            flights.SendMessage("OnApplicationFocus",false);var button=TaskButton(tasks,id);Canvas.ForceUpdateCanvases();var expected=Origin(flights,(RectTransform)button.transform);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            CheckFlights(flights,expected,check);
            shot=RemainingFixesVerification.Capture("task-reward-origin-keyboard");while(shot.MoveNext())yield return shot.Current;
            int exp=service.Experience,coins=service.FishCoins;tasks.CompleteSelected(id);
            check(service.Experience==exp&&service.FishCoins==coins,"Duplicate completion grants no extra reward");yield return 1.8;
            id=Add(service,true,check);yield return .2;button=TaskButton(tasks,id);Canvas.ForceUpdateCanvases();expected=Origin(flights,(RectTransform)button.transform);
            button.onClick.Invoke();yield return .2;
            var dialog=(TaskConfirmDialog)Field(tasks,"confirmation");
            check(dialog.Root.activeSelf&&flights.ActiveCount==0,"Unfinished subtasks require confirmation before reward feedback");
            dialog.Root.transform.Find("Popup/Confirm").GetComponent<Button>().onClick.Invoke();CheckFlights(flights,expected,check);yield return 1.8;
            id=Add(service,false,check);yield return .2;button=TaskButton(tasks,id);
            using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                button.onClick.Invoke();check(!string.IsNullOrEmpty(service.LastError)&&flights.ActiveCount==0&&!service.Query(TaskTab.Today).First(t=>t.task.id==id).Completed,"Failed completion does not animate or complete the task");
                check(Field(flights,"frozenOrigin")==null,"Failed settlement clears its scoped reward origin");
            }
            button.onClick.Invoke();check(service.Snapshot.history.Count(t=>t.taskId==id&&!t.revoked)==1&&flights.ActiveCount>0,"Retry settles and presents one reward");yield return 1.8;
            check(nav.ShowPage(MainMenuPage.Dashboard),"Open Dashboard completion");id=Add(service,false,check);yield return .3;
            var dashboard=nav.pages[(int)MainMenuPage.Dashboard].GetComponentInChildren<DashboardTasksUI>();
            var row=dashboard.taskScroll.content.GetComponentsInChildren<DashboardTaskRow>().First(r=>r.taskTitle.text==service.Query(TaskTab.Today).First(t=>t.task.id==id).task.title);
            Canvas.ForceUpdateCanvases();expected=Origin(flights,(RectTransform)row.completeButton.transform);row.completeButton.onClick.Invoke();CheckFlights(flights,expected,check);yield return 1.8;
            // Exhaust the existing allowance through actual completions; never alter the cap formula.
            for(int i=0;i<30;i++) {id=Add(service,false,check);var view=service.Query(TaskTab.Today).First(t=>t.task.id==id);var preview=service.PreviewReward(view);if(preview.experience==0&&preview.fishCoins==0&&preview.tickets==0)break;check(service.Complete(id,true),"Consume existing daily task allowance in isolated fixture");}
            flights.SendMessage("OnApplicationFocus",false);nav.ShowPage(MainMenuPage.Tasks);tasks.SelectTab(TaskTab.Today);tasks.search.text="Completion feedback";yield return .3;
            button=TaskButton(tasks,id);button.onClick.Invoke();check(service.Snapshot.history.Count(t=>t.taskId==id&&!t.revoked)==1&&flights.ActiveCount==0,"At the cap, completion still records and creates no reward flight");
            var notice=(TaskResultNotice)Field(tasks,"resultNotice");check(notice.message.text.Contains("limit reached"),"Zero-reward completion explains the daily limit");
            shot=RemainingFixesVerification.Capture("task-completion-cap-1920");while(shot.MoveNext())yield return shot.Current;
            var reloaded=new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock());check(reloaded.Snapshot.history.Count(t=>t.taskId==id&&!t.revoked)==1,"Zero-reward completion survives reload");
            tasks.SelectTab(TaskTab.History);tasks.search.text="Completion feedback";
            tasks.SelectTask(service.Snapshot.history.Last(t=>t.taskId==id).id);yield return .2;
            bool datesFit=true;
            foreach(int width in new[]{1920,1280}) {
                RemainingFixesVerification.Resolution(width,width==1920?1080:720);yield return .3;
                var dates=tasks.listContent.GetComponentsInChildren<TMPro.TMP_Text>().Where(text=>text.name=="DateTime").ToArray();
                datesFit &= dates.Length>0&&dates.All(text=>!text.isTextOverflowing);
                foreach(var date in dates)File.AppendAllText(output+"/history-date-layout.txt",width+" text="+date.text+" rect="+date.rectTransform.rect+" preferred="+date.GetPreferredValues()+" overflow="+date.isTextOverflowing+"\n");
                shot=RemainingFixesVerification.Capture("history-completion-dates-"+width);while(shot.MoveNext())yield return shot.Current;
            }
            check(datesFit,"Long completion dates fit History rows at both 1920 and 1280");
            tasks.search.text="";
        }finally {
            arena.GetType().GetField("oldForestStageResource",Hidden).SetValue(arena,path);if(oldForest)arena.oldForestStagePrefab=oldForest;
            check(store.TrySave(original,out var error),"Restore isolated presentation fixture: "+error);
            flights.SendMessage("OnApplicationFocus",false);nav.ShowPage(MainMenuPage.Dashboard);
        }
    }
}
