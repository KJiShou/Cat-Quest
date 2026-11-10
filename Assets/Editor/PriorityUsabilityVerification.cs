using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Runs through TaskTabsVerification's isolated, bounded and restored Play lifecycle.
// No asset generation or repairs. Exercises real presenters and persistence failures.
public static class PriorityUsabilityVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static string folder;
    static RewardScreenshotCapture capture;
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)};
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); File.AppendAllText(folder+"/checks.txt","PASS "+message+"\n"); }
    static TaskConfirmDialog Dialog(TaskSystemUI owner)=>(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",Hidden).GetValue(owner);
    static Button DialogButton(TaskSystemUI owner,string name)=>Dialog(owner).Root.transform.Find("Popup/"+name).GetComponent<Button>();
    static void Edit(TaskSystemUI owner,string id)
    {
        owner.SelectTask(id);
        var view=owner.Service.Query(TaskTab.Today).First(v=>v.task.id==id);
        typeof(TaskSystemUI).GetMethod("BeginEdit",Hidden).Invoke(owner,new object[]{view});
    }
    static void Resolution(Vector2Int size)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
    static void Capture(string name)=>capture.Begin(Path.GetFullPath(folder+"/"+name+".png"));
    static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)button.transform;
        var canvas=button.GetComponentInParent<Canvas>().rootCanvas;
        var point=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center));
        var pointer=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"Raycast reaches "+button.name+" at "+point+"; top="+(hits.Count>0?hits[0].gameObject.name:"none"));
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static void Inside(Component component,Vector2Int size)
    {
        Canvas.ForceUpdateCanvases();
        var canvas=component.GetComponentInParent<Canvas>().rootCanvas;
        TaskTabsVerification.Viewport(component,size);
        var corners=new Vector3[4];((RectTransform)component.transform).GetWorldCorners(corners);
        foreach(var corner in corners) {
            var p=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,corner);
            var viewport=canvas.pixelRect;
            Check(p.x>=viewport.xMin-1&&p.x<=viewport.xMax+1&&p.y>=viewport.yMin-1&&p.y<=viewport.yMax+1,"Prompt inside "+size);
        }
    }
    static IEnumerable<double> ConfirmFeedback(string name,params Button[] buttons)
    {
        var feedback=buttons.Select(b=>b.GetComponent<JuicyButton>()).ToArray();
        Check(buttons.All(b=>b.GetComponents<JuicyButton>().Length==1)&&feedback.All(f=>f&&f.isActiveAndEnabled),name+" uses one active shared feedback owner per button");
        var rects=buttons.Select(b=>(RectTransform)b.transform).ToArray();
        var sizes=rects.Select(r=>r.rect.size).ToArray();var positions=rects.Select(r=>r.anchoredPosition).ToArray();
        var enabled=buttons.Select(b=>b.interactable).ToArray();
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        var selection=new BaseEventData(EventSystem.current);
        try {
            foreach(var f in feedback){f.OnPointerExit(pointer);f.OnDeselect(selection);}
            yield return .25;
            foreach(var f in feedback)f.OnPointerEnter(pointer);
            yield return .2;
            Check(feedback.All(f=>f.DrawScale.y>1.005f),name+" hover animates");
            Capture(name+"-hover");while(capture.Pending)yield return .05;
            foreach(var f in feedback)f.OnPointerDown(pointer);
            yield return .15;
            Check(feedback.All(f=>f.DrawScale.y<.99f),name+" press animates");
            Capture(name+"-press");while(capture.Pending)yield return .05;
            foreach(var f in feedback){f.OnPointerUp(pointer);f.OnPointerExit(pointer);f.OnDeselect(selection);}
            for(int frame=0;frame<4;frame++) {
                yield return .08;
                Capture(name+"-release-"+frame);while(capture.Pending)yield return .05;
            }
            Check(feedback.All(f=>Vector2.Distance(f.DrawScale,Vector2.one)<.01f),name+" release settles");
            for(int i=0;i<6;i++)foreach(var f in feedback){f.OnPointerEnter(pointer);f.OnPointerDown(pointer);f.OnPointerUp(pointer);f.OnPointerExit(pointer);}
            yield return .3;
            Check(feedback.All(f=>Vector2.Distance(f.DrawScale,Vector2.one)<.01f),name+" rapid presses settle without drift");
            foreach(var f in feedback)f.OnSelect(selection);
            yield return .15;
            Check(feedback.All(f=>f.DrawScale.y>1.005f),name+" keyboard selection has feedback");
            // Invoke the presentation component only; no Button submit or click is dispatched.
            foreach(var f in feedback)f.OnSubmit(selection);
            yield return .05;
            foreach(var f in feedback)f.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
            Check(feedback.All(f=>f.DrawScale==Vector2.one&&f.DrawOffset==0),name+" focus loss clears submit and press feedback");
            foreach(var b in buttons)b.interactable=false;
            foreach(var f in feedback){f.OnPointerEnter(pointer);f.OnPointerDown(pointer);}
            yield return .1;
            Check(feedback.All(f=>f.DrawScale==Vector2.one),name+" disabled buttons ignore feedback");
            for(int i=0;i<buttons.Length;i++)buttons[i].interactable=enabled[i];
            yield return .1;
            Check(feedback.All(f=>f.DrawScale==Vector2.one),name+" re-enabled buttons remain settled");
            Check(rects.Select((r,i)=>Vector2.Distance(r.rect.size,sizes[i])<.01f&&Vector2.Distance(r.anchoredPosition,positions[i])<.01f).All(stable=>stable),name+" hit areas stay fixed throughout feedback");
        } finally {
            for(int i=0;i<buttons.Length;i++)if(buttons[i])buttons[i].interactable=enabled[i];
            foreach(var f in feedback)if(f)f.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
        }
    }
    static IEnumerable<double> CalendarDraftGuards(TaskSystemUI owner)
    {
        var calendar=owner.calendar;
        Check(calendar,"Calendar available for draft guard checks");
        Resolution(Sizes[0]);yield return .5;
        calendar.SetMode(TaskDisplayMode.Calendar);calendar.SelectDate(owner.Service.LocalNow.Date);
        yield return .2;
        var originalParent=owner.editor.transform.parent;
        int savedCount=owner.Service.Snapshot.tasks.Count;
        calendar.AddTask();owner.editor.taskTitle.text="Calendar Back draft";owner.editor.description.text="Keep these calendar edits";
        Check(owner.IsEditing&&owner.editor.transform.parent==calendar.drawerBody,"Calendar editor is owned by its drawer");
        calendar.Back();yield return .2;
        Check(Dialog(owner).Root.activeSelf&&owner.IsEditing,"Calendar Back asks before discarding");
        Capture("calendar-back-guard-1920");while(capture.Pending)yield return .05;
        Click(DialogButton(owner,"Cancel"));
        Check(owner.IsEditing&&calendar.DrawerOpen&&owner.editor.transform.parent==calendar.drawerBody&&owner.editor.taskTitle.text=="Calendar Back draft"&&owner.editor.description.text=="Keep these calendar edits","Calendar Back Keep editing preserves form and parent");
        calendar.Back();yield return .2;Click(DialogButton(owner,"Confirm"));
        Check(!owner.IsEditing&&owner.editor.transform.parent==originalParent&&calendar.monthInteraction.interactable&&calendar.listMode.interactable&&calendar.calendarMode.interactable,"Calendar Back discard restores parent and navigation");
        Check(owner.Service.Snapshot.tasks.Count==savedCount,"Calendar Back discard does not create a task");

        calendar.AddTask();owner.editor.taskTitle.text="Calendar Close draft";owner.editor.description.text="Preserve on Keep editing";
        calendar.CloseDrawer();yield return .2;
        Check(Dialog(owner).Root.activeSelf&&owner.IsEditing&&calendar.DrawerOpen,"Calendar Close waits for discard confirmation");
        Click(DialogButton(owner,"Cancel"));
        Check(owner.IsEditing&&owner.editor.transform.parent==calendar.drawerBody&&owner.editor.taskTitle.text=="Calendar Close draft"&&owner.editor.description.text=="Preserve on Keep editing","Calendar Close Keep editing preserves form and parent");
        calendar.CloseDrawer();yield return .2;Click(DialogButton(owner,"Confirm"));
        Check(!owner.IsEditing&&!calendar.DrawerOpen&&owner.editor.transform.parent==originalParent&&owner.Service.Snapshot.tasks.Count==savedCount,"Calendar confirmed Close discards only the draft and restores parent");
        calendar.AddTask();owner.editor.taskTitle.text="Calendar task after discard";owner.SaveDraft();
        Check(!owner.IsEditing&&owner.editor.transform.parent==originalParent&&owner.Service.Snapshot.tasks.Any(t=>t.title=="Calendar task after discard"),"Calendar can create and save normally after discarded drafts");
        calendar.CloseDrawer();

        Resolution(Sizes[1]);yield return .5;
        calendar.AddTask();owner.editor.taskTitle.text="Compact calendar draft";
        calendar.CloseDrawer();yield return .2;
        Check(Dialog(owner).Root.activeSelf&&owner.IsEditing&&calendar.DrawerOpen,"Compact Calendar Close preserves pending draft");
        Inside(DialogButton(owner,"Confirm"),Sizes[1]);Inside(DialogButton(owner,"Cancel"),Sizes[1]);
        Capture("calendar-close-guard-1280");while(capture.Pending)yield return .05;
        Click(DialogButton(owner,"Cancel"));
        Check(owner.IsEditing&&owner.editor.transform.parent==calendar.drawerBody&&owner.editor.taskTitle.text=="Compact calendar draft","Compact Calendar Keep editing preserves form and parent");
        calendar.CloseDrawer();yield return .2;Click(DialogButton(owner,"Confirm"));
        Check(!owner.IsEditing&&!calendar.DrawerOpen&&owner.editor.transform.parent==originalParent,"Compact Calendar confirmed Close restores parent");
        calendar.SetMode(TaskDisplayMode.List);yield return .2;
    }
    public static IEnumerator Run(string output)
    {
        folder=output;
        File.WriteAllText(folder+"/checks.txt",DateTime.UtcNow.ToString("O")+" Unity "+Application.unityVersion+"\n");
        UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");yield return 2;
        Resolution(Sizes[0]);yield return .5;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var manager=GameDataManager.EnsureInstance();var service=manager.Tasks;
        Check(manager.SavePath.Contains("PriorityUsabilityVerification"),"Isolated save in use");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        capture=new GameObject("Priority usability screenshots").AddComponent<RewardScreenshotCapture>();
        var owner=nav.taskSystem;
        Check(nav.ShowPage(MainMenuPage.Tasks),"Tasks available");yield return .5;
        Check(service.Upsert(new QuestTask{title="Keep this draft",scheduled=true,date=TaskService.DateKey(service.LocalNow)},null,out var taskId),"Seed editable task");
        owner.SelectTab(TaskTab.Today);Edit(owner,taskId);
        Check(nav.ShowPage(MainMenuPage.Focus)&&!Dialog(owner).Root.activeSelf,"Unchanged draft can leave without prompt");
        nav.ShowPage(MainMenuPage.Tasks);owner.SelectTab(TaskTab.Today);Edit(owner,taskId);
        owner.editor.description.text="Unsaved description survives navigation";
        Check(!nav.ShowPage(MainMenuPage.Focus)&&owner.IsEditing,"Dirty sidebar navigation stays in editor");
        Check(EventSystem.current.currentSelectedGameObject==DialogButton(owner,"Cancel").gameObject,"Keep editing has default focus");
        Check(!nav.ShowPage(MainMenuPage.Dashboard),"Repeated navigation cannot replace prompt");
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return .2;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .2;
            Check(!Dialog(owner).Root.activeSelf&&owner.IsEditing&&owner.editor.description.text.Contains("survives"),"Escape preserves draft");
            owner.SelectTab(TaskTab.History);
            Check(owner.CurrentTab==TaskTab.Today&&Dialog(owner).Root.activeSelf,"Dirty tab switch waits for decision");
            yield return .2;
            foreach(var wait in ConfirmFeedback("task-confirm-feedback",DialogButton(owner,"Cancel"),DialogButton(owner,"Confirm")))yield return wait;
            Click(DialogButton(owner,"Cancel"));
            Check(owner.IsEditing&&owner.editor.description.text.Contains("survives"),"Keep editing preserves all draft fields");
            var tutorial=UnityEngine.Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
            tutorial.OpenTopic(1);yield return .2;
            Check(nav.CurrentPage==MainMenuPage.Tasks&&Dialog(owner).Root.activeSelf&&string.IsNullOrEmpty(tutorial.CurrentTopic),"Focus Help respects dirty draft guard without stacking tutorial");
            Click(DialogButton(owner,"Cancel"));
            using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                owner.SaveDraft();
                Check(owner.IsEditing&&service.Snapshot.tasks.Single(t=>t.id==taskId).description!="Unsaved description survives navigation","Failed task save preserves committed state and draft");
                Dialog(owner).Dismiss();
            }
            owner.SaveDraft();
            Check(!owner.IsEditing&&new PlayerDataStore(manager.SavePath).Data.tasks.Single(t=>t.id==taskId).description.Contains("survives"),"Task retry persists draft and reloads");
            Edit(owner,taskId);owner.editor.taskTitle.text="Discard me";
            int navigations=0;Action<MainMenuPage> changed=_=>navigations++;nav.PageChanged+=changed;
            Check(!nav.ShowPage(MainMenuPage.Focus),"Discard navigation opens prompt");
            yield return .2;
            var confirm=DialogButton(owner,"Confirm");Click(confirm);confirm.onClick.Invoke();
            nav.PageChanged-=changed;
            Check(nav.CurrentPage==MainMenuPage.Focus&&navigations==1&&!owner.IsEditing,"Discard resumes destination exactly once");
            Check(service.Snapshot.tasks.Single(t=>t.id==taskId).title=="Keep this draft","Discard never writes task changes");
            nav.ShowPage(MainMenuPage.Tasks);owner.SelectTab(TaskTab.Today);Edit(owner,taskId);owner.editor.description.text="Tab draft";
            owner.SelectTab(TaskTab.History);yield return .2;Click(DialogButton(owner,"Confirm"));
            Check(owner.CurrentTab==TaskTab.History&&!owner.IsEditing,"Confirmed discard resumes requested tab");

            var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
            var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",Hidden).GetValue(sidebar);
            foreach(var size in Sizes)foreach(bool expanded in new[]{false,true}) {
                Resolution(size);toggle.isOn=expanded;yield return .7;
                owner.SelectTab(TaskTab.Today);Edit(owner,taskId);owner.editor.description.text="A draft that must not be lost";
                Capture("task-before-"+size.x+"-"+expanded);while(capture.Pending)yield return .1;
                Check(!nav.ShowPage(MainMenuPage.Focus),"Draft guard at "+size+" sidebar "+expanded);
                Inside(DialogButton(owner,"Confirm"),size);Inside(DialogButton(owner,"Cancel"),size);
                Capture("task-guard-"+size.x+"-"+expanded);while(capture.Pending)yield return .1;
                Click(DialogButton(owner,"Cancel"));Check(owner.IsEditing,"Prompt cancel preserves draft at "+size);
                owner.CancelDraft();
            }
            foreach(var wait in CalendarDraftGuards(owner))yield return wait;

            // Existing source popup, rendered without altering its prefab or project hierarchy.
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Popup_NameChange.prefab");
            var reference=UnityEngine.Object.Instantiate(source,nav.GetComponentInParent<Canvas>().rootCanvas.transform);
            reference.SetActive(true);reference.transform.SetAsLastSibling();Resolution(Sizes[0]);yield return .5;
            Capture("simplecasual-popup-reference");while(capture.Pending)yield return .1;
            UnityEngine.Object.Destroy(reference);yield return .2;

            nav.ShowPage(MainMenuPage.Boss);yield return 1;
            var boss=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
            boss.Action();yield return .2;
            foreach(var wait in ConfirmFeedback("boss-confirm-feedback",boss.cancel,boss.confirm))yield return wait;
            boss.Confirm();
            Check(boss.IsBattling,"Boss entry ready for leave guard");
            int tickets=service.Tickets;
            Check(!nav.ShowPage(MainMenuPage.Tasks)&&boss.confirmation.activeSelf,"Boss sidebar opens Stop confirmation");
            Check(EventSystem.current.currentSelectedGameObject==boss.cancel.gameObject,"Boss Keep fighting default focus");
            Capture("boss-leave-1920");while(capture.Pending)yield return .1;
            Resolution(Sizes[1]);yield return .5;
            Inside(boss.confirm,Sizes[1]);Inside(boss.cancel,Sizes[1]);
            Capture("boss-leave-1280");while(capture.Pending)yield return .1;
            Click(boss.cancel);Check(nav.CurrentPage==MainMenuPage.Boss&&boss.IsBattling&&service.Tickets==tickets,"Keep fighting does not spend another ticket");
            Check(!nav.ShowPage(MainMenuPage.Tasks),"Boss reopens leave confirmation");
            yield return .2;
            using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                Click(boss.confirm);Check(nav.CurrentPage==MainMenuPage.Boss&&service.HasActiveBattle,"Failed Boss stop save stays on page");
            }
            boss.Action();
            Check(nav.CurrentPage==MainMenuPage.Tasks&&!service.HasActiveBattle&&service.Tickets==tickets,"Retry commits stop then navigates without refund/extra ticket");
            boss.Confirm();Check(nav.CurrentPage==MainMenuPage.Tasks&&service.Tickets==tickets,"Stale Boss confirmation cannot repeat");

            nav.ShowPage(MainMenuPage.Focus);yield return .5;
            var panel=nav.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>();var focus=panel.Controller;
            foreach(var size in Sizes) {
                Resolution(size);toggle.isOn=false;yield return .5;
                using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                    Click(panel.start);yield return .2;
                    Check(focus.Phase!=FocusPhase.Focusing&&!string.IsNullOrEmpty(focus.ErrorMessage),"Failed Focus start reports error");
                    Check(panel.allowanceNotice.activeSelf&&panel.allowance.text.Contains("Start failed"),"Start error is visible at "+size);
                    panel.allowance.ForceMeshUpdate();
                    Check(!panel.allowance.isTextOverflowing,"Focus start error fits at "+size);
                    Capture("focus-start-error-"+size.x);while(capture.Pending)yield return .1;
                }
            }
            yield return 4.3;
            Check(panel.allowanceNotice.activeSelf&&panel.allowance.text.Contains("Start failed"),"Focus start error survives four seconds");
            nav.ShowPage(MainMenuPage.Tasks);nav.ShowPage(MainMenuPage.Focus);yield return .3;
            Check(panel.allowanceNotice.activeSelf,"Focus error survives page close/reopen");
            var options=focus.Options.Copy();options.mode=FocusTimerMode.Countdown;options.focusMinutes=25;focus.Configure(options);
            Click(panel.start);Check(focus.Phase==FocusPhase.Focusing&&string.IsNullOrEmpty(focus.ErrorMessage),"Successful Focus start clears error");
            for(int second=0;second<61;second++)focus.Advance(1);
            using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                Check(!focus.EndEarly()&&focus.Phase==FocusPhase.Saving,"End Early failure preserves pending settlement");
                yield return 4.3;
                Check(panel.allowanceNotice.activeSelf&&panel.allowance.text.Contains("Retry Save"),"Save error remains actionable past four seconds");
                foreach(var size in Sizes.Take(2)) {
                    Resolution(size);yield return .5;panel.allowance.ForceMeshUpdate();
                    Check(!panel.allowance.isTextOverflowing,"Focus save error fits at "+size);
                    Capture("focus-save-error-"+size.x);while(capture.Pending)yield return .1;
                }
                var exit=FocusRunner.EnsureInstance().GetComponent<FocusExitGuard>();
                Check(!(bool)typeof(FocusExitGuard).GetMethod("WantsToQuit",Hidden).Invoke(exit,null),"Exit pending save opens confirmation");
                var dialog=(GameObject)typeof(FocusExitGuard).GetField("dialog",Hidden).GetValue(exit);
                Check(dialog.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("Retry Save and exit")),"Saving exit explains actual retry action");
                Capture("focus-save-exit");while(capture.Pending)yield return .1;
                dialog.transform.Find("Dialog/Stay").GetComponent<Button>().onClick.Invoke();
            }
            yield return .2;
            Click(panel.retry);Check(focus.Phase==FocusPhase.Finished&&string.IsNullOrEmpty(focus.ErrorMessage),"Focus retry commits earned End Early result");
            var receipt=focus.LastResult;var committed=JsonUtility.ToJson(service.Snapshot);
            Check(receipt!=null&&receipt.eligibleMinutes>=1&&receipt.status==FocusSessionStatus.EndedEarly,"End Early retains earned minutes");
            panel.retry.onClick.Invoke();Check(JsonUtility.ToJson(service.Snapshot)==committed,"Repeated Focus retry preserves all rewards, sessions and receipt exactly");
            Check(new PlayerDataStore(manager.SavePath).Data.focusSessions.Any(s=>s.id==receipt.id),"Focus result survives reload");
            var missing=nav.GetComponentInParent<Canvas>().rootCanvas.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check(missing==0,"No missing scripts in rendered application UI");
        } finally { InputSystem.RemoveDevice(keyboard); }
    }
}
