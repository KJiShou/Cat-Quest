using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs inside TaskTabsVerification's isolated save and restored Editor environment.
public static class TaskContextMenuVerification
{
    const string Folder = "Library/TaskContextMenuVerification";
    static void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
    static TaskContextMenu Menu => UnityEngine.Object.FindFirstObjectByType<TaskContextMenu>();
    static TaskConfirmDialog Dialog(TaskSystemUI owner) => (TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static Vector2 Center(Component target)
    {
        var rect = (RectTransform)target.transform;
        var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
        return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,rect.TransformPoint(rect.rect.center));
    }
    static void Click(Component target,PointerEventData.InputButton button = PointerEventData.InputButton.Left)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(EventSystem.current) { position=Center(target),button=button };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0,"No raycast for "+target.name);
        var expected = target.GetComponent<Button>();
        if(expected) Check(hits[0].gameObject.GetComponentInParent<Button>()==expected,"Blocked raycast for "+target.name+" by "+hits[0].gameObject.name);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static void Open(TaskSystemUI owner,string id)
    {
        Canvas.ForceUpdateCanvases();
        var row=owner.TutorialTaskTarget(id); Check(row,"Task row missing");
        Canvas.ForceUpdateCanvases(); Click(row,PointerEventData.InputButton.Right);
        Check(Menu,"Right click did not open context menu");
    }
    static void Bounds(TaskContextMenu menu)
    {
        var corners=new Vector3[4];menu.panel.GetWorldCorners(corners);
        var canvas=menu.GetComponentInParent<Canvas>().rootCanvas;
        foreach(var corner in corners) {
            var p=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,corner);
            Check(p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height,"Context menu left viewport: "+p);
        }
    }
    public static IEnumerator Run()
    {
        Directory.CreateDirectory(Folder);
        foreach(var wait in TaskCalendarPlayVerification.Await(GameDataManager.EnsureInstance().Ready))yield return wait;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        Check(nav.ShowPage(MainMenuPage.Tasks),"Cannot show Tasks");yield return 1;
        var owner=nav.taskSystem;var service=owner.Service;
        string id=null,tag=null;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.UpsertAsync(new QuestTask { title="Proposal Draft",description="Keep my description",minuteOfDay=795,
            subtasks=new List<QuestSubtask>{new QuestSubtask { id="step",title="Review draft" }} },Array.Empty<string>()),value=>id=value))yield return wait;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.SaveTagAsync(null,"ECP"),value=>tag=value))yield return wait;
        var capture=new GameObject("Context menu capture").AddComponent<RewardScreenshotCapture>();
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expander=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
        {
            typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
            foreach(bool expanded in new[]{false,true})
            {
                expander.isOn=expanded;owner.SelectTab(TaskTab.Todo);yield return .5;
                string suffix=size.x+"x"+size.y+(expanded?"-expanded":"-collapsed");
                capture.Begin(Path.GetFullPath(Folder+"/before-"+suffix+".png"));while(capture.Pending)yield return .1;
                owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;yield return .4;Bounds(Menu);
                Check(EventSystem.current.currentSelectedGameObject==null,"Pointer-opened menu forced keyboard selection");
                Check(Vector2.Distance(Menu.edit.GetComponent<JuicyButton>().DrawScale,Vector2.one)<.01f,"First menu action stayed highlighted without hover");
                capture.Begin(Path.GetFullPath(Folder+"/menu-"+suffix+".png"));while(capture.Pending)yield return .1;
                var hint=Menu.complete.GetComponent<ControlHint>();
                Check(hint.expansion&&hint.expansionLabel,"Inline label bindings missing");
                var actions=new[]{Menu.edit,Menu.difficulty,Menu.tags,Menu.schedule,Menu.complete,Menu.delete};
                float columnX=Center(actions[0]).x;
                for(int i=0;i<actions.Length;i++){
                    Check(Mathf.Abs(Center(actions[i]).x-columnX)<1,"Actions are not one column");
                    if(i>0)Check(Center(actions[i]).y<Center(actions[i-1]).y,"Action order or spacing invalid");
                }
                var green=Menu.complete.targetGraphic.color;Check(green.g>green.r&&green.g>green.b,"Complete is not green");
                var hitRect=((RectTransform)Menu.complete.transform).rect;var iconCenter=Center(Menu.complete);
                var trace=new List<float>();Canvas.WillRenderCanvases sample=()=>trace.Add(hint.expansion.rect.width);
                Canvas.willRenderCanvases+=sample;
                try {
                    ExecuteEvents.Execute(Menu.complete.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
                    if(size.x==1920&&!expanded){
                        for(int frame=0;frame<8;frame++){
                            capture.Begin(Path.GetFullPath(Folder+"/reveal-"+frame.ToString("D2")+".png"));while(capture.Pending)yield return .01;
                        }
                    }
                    yield return .08;
                    capture.Begin(Path.GetFullPath(Folder+"/expanding-"+suffix+".png"));while(capture.Pending)yield return .05;
                    yield return .3;
                } finally {Canvas.willRenderCanvases-=sample;}
                Check(trace.Any(w=>w>hint.collapsedWidth+.1f&&w<hint.expandedWidth-.1f),"No rendered intermediate expansion frames");
                File.WriteAllText(Folder+"/expansion-motion-"+suffix+".txt",DateTime.UtcNow.ToString("O")+"\n"+string.Join(",",trace));
                Check(Mathf.Abs(hint.expansion.rect.width-hint.expandedWidth)<.1f&&hint.expansionLabel.alpha>.99f,"Hover label did not settle expanded");
                Check(((RectTransform)Menu.complete.transform).rect==hitRect&&Vector2.Distance(Center(Menu.complete),iconCenter)<.1f,"Hover moved the icon column");
                capture.Begin(Path.GetFullPath(Folder+"/expanded-"+suffix+".png"));while(capture.Pending)yield return .1;
                ExecuteEvents.Execute(Menu.complete.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);
                if(size.x==1920&&!expanded){for(int frame=8;frame<16;frame++){capture.Begin(Path.GetFullPath(Folder+"/reveal-"+frame.ToString("D2")+".png"));while(capture.Pending)yield return .01;}}
                yield return .4;
                Check(Mathf.Abs(hint.expansion.rect.width-hint.collapsedWidth)<.1f&&hint.expansionLabel.alpha<.01f,"Hover exit did not collapse");
                if(size.x==1920&&!expanded){
                    foreach(var button in actions){
                        var revealHint=button.GetComponent<ControlHint>();
                        ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);yield return .35;
                        var label=revealHint.expansionLabel.GetComponent<TMP_Text>();label.ForceMeshUpdate();
                        Check(!label.isTextOverflowing&&label.preferredWidth<=label.rectTransform.rect.width+.1f,"Expanded action label clips: "+label.text);
                        capture.Begin(Path.GetFullPath(Folder+"/action-"+button.name+".png"));while(capture.Pending)yield return .1;
                        ExecuteEvents.Execute(button.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);yield return .3;
                    }
                    var quick=Menu.edit.GetComponent<ControlHint>();
                    for(int turn=0;turn<3;turn++){
                        quick.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .04;
                        quick.OnPointerExit(new PointerEventData(EventSystem.current));yield return .04;
                    }
                    yield return .3;Check(Mathf.Abs(quick.expansion.rect.width-quick.collapsedWidth)<.1f,"Rapid hover reversal drifted");
                    quick.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .06;
                    Menu.Dismiss();Check(Mathf.Abs(quick.expansion.rect.width-quick.collapsedWidth)<.1f&&quick.expansionLabel.alpha==0,"Closing mid-animation did not reset reveal");
                    Open(owner,id);yield return .2;
                    var textButton=Menu.tags;var textHint=textButton.GetComponent<ControlHint>();
                    ExecuteEvents.Execute(textButton.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);yield return .35;
                    var textPoint=Center(textHint.expansionLabel);
                    var textPointer=new PointerEventData(EventSystem.current){position=textPoint,button=PointerEventData.InputButton.Left};
                    var textHits=new List<RaycastResult>();EventSystem.current.RaycastAll(textPointer,textHits);
                    Check(textHits.Count>0&&textHits[0].gameObject.GetComponentInParent<Button>()==textButton,"Expanded text is not clickable");
                    ExecuteEvents.ExecuteHierarchy(textHits[0].gameObject,textPointer,ExecuteEvents.pointerDownHandler);yield return .35;
                    textHits.Clear();EventSystem.current.RaycastAll(textPointer,textHits);
                    Check(textHits.Count>0&&textHits[0].gameObject.GetComponentInParent<Button>()==textButton,"Holding press collapsed the text release target");
                    ExecuteEvents.ExecuteHierarchy(textHits[0].gameObject,textPointer,ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.ExecuteHierarchy(textHits[0].gameObject,textPointer,ExecuteEvents.pointerClickHandler);yield return .2;
                    Check(Menu.tagsPanel.activeSelf,"Expanded text click did not open Tags");Click(Menu.back);yield return .2;
                }
                Click(Menu.difficulty);yield return .3;Click(Menu.difficultyOptions[2]);Bounds(Menu);
                Check(!owner.IsEditing,"Difficulty opened full panel");
                capture.Begin(Path.GetFullPath(Folder+"/inline-difficulty-"+suffix+".png"));while(capture.Pending)yield return .1;
                Click(Menu.back);yield return .2;Click(Menu.tags);yield return .3;Bounds(Menu);
                capture.Begin(Path.GetFullPath(Folder+"/inline-tags-"+suffix+".png"));while(capture.Pending)yield return .1;
                Click(Menu.back);yield return .2;
                Click(Menu.schedule);yield return .3;Bounds(Menu);
                var calendarFeedback=Menu.datePicker.calendar.GetComponent<JuicyButton>();
                Check(calendarFeedback&&!calendarFeedback.enabled,"Calendar background must not animate");
                var dayFeedback=Menu.datePicker.days[10].GetComponent<JuicyButton>();
                dayFeedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .4;
                Check(dayFeedback.DrawScale.y>1.005f,"Calendar day hover feedback missing");
                capture.Begin(Path.GetFullPath(Folder+"/calendar-hover-"+suffix+".png"));while(capture.Pending)yield return .1;
                dayFeedback.OnPointerExit(new PointerEventData(EventSystem.current));yield return .5;
                Check(Vector2.Distance(dayFeedback.DrawScale,Vector2.one)<.01f,"Calendar day hover did not settle");
                capture.Begin(Path.GetFullPath(Folder+"/schedule-"+suffix+".png"));while(capture.Pending)yield return .1;
                Check(Menu.datePicker.calendar.gameObject.activeInHierarchy&&!Menu.datePicker.overlay.activeSelf,"Calendar must be visible immediately inside schedule");yield return .3;
                capture.Begin(Path.GetFullPath(Folder+"/inline-calendar-"+suffix+".png"));while(capture.Pending)yield return .1;
                Click(Menu.datePicker.days[10]);yield return .2;
                Check(Menu&&Menu.datePicker.HasDate&&Menu.datePicker.calendar.gameObject.activeInHierarchy&&!Menu.datePicker.overlay.activeSelf,"Clicking a day must keep the embedded calendar open");
                capture.Begin(Path.GetFullPath(Folder+"/calendar-selected-"+suffix+".png"));while(capture.Pending)yield return .1;
                Click(Menu.datePicker.nextMonth);yield return .2;Click(Menu.datePicker.previousMonth);yield return .2;
                Click(Menu.back);yield return .2;
                Menu.backdrop.onClick.Invoke();Check(!Menu,"Backdrop did not dismiss");
                var edgeRow=owner.TutorialTaskTarget(id);
                edgeRow.GetComponent<TaskContextTarget>().OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Right,position=new Vector2(Screen.width-1,1)});
                yield return .2;Bounds(Menu);
                Menu.delete.Select();yield return .35;
                var reveal=Menu.delete.GetComponent<ControlHint>().expansion;
                var revealCorners=new Vector3[4];reveal.GetWorldCorners(revealCorners);
                var revealCanvas=Menu.GetComponentInParent<Canvas>().rootCanvas;
                foreach(var corner in revealCorners){var p=RectTransformUtility.WorldToScreenPoint(revealCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:revealCanvas.worldCamera,corner);Check(p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height,"Expanded label clipped at screen edge");}
                Menu.Dismiss();
            }
        }
        owner.SelectTab(TaskTab.Todo);yield return .3;
        var motionSettings=service.Settings;bool previousReduced=motionSettings.reduceMotion;
        System.Threading.Tasks.Task<OperationResult<bool>> restoredSettings=null;
        try {
            motionSettings.reduceMotion=true;
            foreach(var wait in TaskCalendarPlayVerification.Await(service.SaveSettingsAsync(motionSettings)))yield return wait;
            Open(owner,id);yield return .2;
            var reducedHint=Menu.edit.GetComponent<ControlHint>();reducedHint.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .1;
            Check(Mathf.Abs(reducedHint.expansion.rect.width-reducedHint.expandedWidth)<.1f,"Reduced Motion did not settle label immediately");Menu.Dismiss();
        } finally {motionSettings.reduceMotion=previousReduced;restoredSettings=service.SaveSettingsAsync(motionSettings);}
        foreach(var wait in TaskCalendarPlayVerification.Await(restoredSettings))yield return wait;
        yield return .2;
        var focusRow=owner.TutorialTaskTarget(id);
        EventSystem.current.SetSelectedGameObject(focusRow.gameObject);
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift,Key.F10));yield return .3;
            Check(Menu,"Shift+F10 did not open focused row menu");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .2;
            Click(Menu.schedule);yield return .2;Check(Menu.datePicker.calendar.gameObject.activeInHierarchy&&!Menu.datePicker.overlay.activeSelf,"Calendar must be visible immediately inside schedule");yield return .3;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return .3;
            Check(!Menu&&!owner.IsEditing,"Escape did not dismiss inline calendar menu");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .2;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return .3;
            Check(!Menu,"Escape did not dismiss context menu");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .2;
        } finally { InputSystem.RemoveDevice(keyboard); }
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;
        Menu.tags.Select();ExecuteEvents.Execute(Menu.tags.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return .3;
        Check(!owner.IsEditing&&Menu.tagsPanel.activeSelf,"Keyboard submit did not open inline tags");
        Menu.Dismiss();yield return .3;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Buttons/Button_Round01-Group.prefab");
        var sourceButton=source.GetComponentsInChildren<Button>(true).First(b=>b.name=="Btn_Rectangle00_n_WhiteCustom");
        var reference=UnityEngine.Object.Instantiate(sourceButton,Menu.transform);
        reference.name="SimpleCasual reference";
        var referenceRect=(RectTransform)reference.transform;
        referenceRect.anchorMin=referenceRect.anchorMax=new Vector2(.3f,.25f);referenceRect.anchoredPosition=Vector2.zero;referenceRect.localScale=Vector3.one*.8f;
        reference.gameObject.SetActive(true);
        ExecuteEvents.Execute(Menu.complete.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);yield return .35;
        capture.Begin(Path.GetFullPath(Folder+"/source-reference.png"));while(capture.Pending)yield return .1;
        UnityEngine.Object.Destroy(reference.gameObject);Menu.Dismiss();yield return .2;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;var menu=Menu;
        var effect=menu.edit.GetComponent<JuicyButton>();var bounds=((RectTransform)menu.edit.transform).rect;
        EventSystem.current.SetSelectedGameObject(menu.tags.gameObject);
        effect.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .4;
        Check(effect.DrawScale.y>1.005f,"Hover feedback missing");
        for(int frame=0;frame<12;frame++) {
            if(frame==3)effect.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
            if(frame==6){effect.OnPointerUp(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});effect.OnPointerExit(new PointerEventData(EventSystem.current));}
            capture.Begin(Path.GetFullPath(Folder+"/motion-"+frame.ToString("D2")+".png"));while(capture.Pending)yield return .1;
            yield return .06;
        }
        effect.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return .3;
        Check(effect.DrawScale.y<.98f,"Press feedback missing");
        effect.OnPointerUp(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});effect.OnPointerExit(new PointerEventData(EventSystem.current));yield return .6;
        Check(Vector2.Distance(effect.DrawScale,Vector2.one)<.01f&&((RectTransform)menu.edit.transform).rect==bounds,"Feedback did not settle or moved hit area: "+effect.DrawScale+" / "+bounds+" -> "+((RectTransform)menu.edit.transform).rect);
        menu.SendMessage("OnApplicationFocus",false);Check(!Menu,"Focus loss left menu open");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.difficulty);yield return .4;
        Check(!owner.IsEditing&&Menu.difficultyPanel.activeSelf,"Difficulty opened full editor");
        Click(Menu.difficultyOptions[2]);
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).difficulty==TaskDifficulty.Normal,"Draft changed committed difficulty");
        Click(Menu.back);Menu.Dismiss();yield return .2;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.difficulty);yield return .3;Click(Menu.difficultyOptions[2]);
        using(TaskCalendarPlayVerification.FailSqliteWrites()) {
            Menu.save.onClick.Invoke();
            foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;
            yield return .1;
            Check(Menu&&!owner.IsEditing&&Menu.status.gameObject.activeSelf,"Save failure lost inline draft or error");
            Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).difficulty==TaskDifficulty.Normal,"Failed write changed committed data");
        }
        yield return .3;capture.Begin(Path.GetFullPath(Folder+"/inline-save-error.png"));while(capture.Pending)yield return .1;
        var saveButton=Menu.save;Click(saveButton);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(!Menu&&!owner.IsEditing&&service.TaskSnapshot.tasks.Single(t=>t.id==id).difficulty==TaskDifficulty.Hard,"Retry did not save inline difficulty");
        int count=service.TaskSnapshot.tasks.Count;saveButton.onClick.Invoke();Check(service.TaskSnapshot.tasks.Count==count,"Duplicate Save changed task count");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.tags);yield return .4;
        Check(!owner.IsEditing&&Menu.tagsPanel.activeSelf,"Tag submenu opened full editor");
        var option=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().Single(t=>t.TagId==tag);
        option.button.Select();yield return .3;Click(option.button);Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).tagIds.Contains(tag),"Tag not saved");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.tags);yield return .3;
        option=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().Single(t=>t.TagId==tag);
        Check(option.selectedMark.enabled,"Saved tag not checked on reopen");option.button.Select();yield return .3;Click(option.button);
        Click(Menu.back);Menu.Dismiss();Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).tagIds.Contains(tag),"Tag cancellation changed save");
        // Remove a saved tag, then add two tags in one inline save.
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.tags);yield return .3;
        option=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().Single(t=>t.TagId==tag);
        option.button.Select();yield return .3;Click(option.button);Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(!service.TaskSnapshot.tasks.Single(t=>t.id==id).tagIds.Contains(tag),"Removing tag was not saved");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.tags);yield return .3;
        var firstTag=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().First();Click(firstTag.button);
        option=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().Single(t=>t.TagId==tag);
        option.button.Select();yield return .3;Click(option.button);yield return .2;
        capture.Begin(Path.GetFullPath(Folder+"/inline-tags-selected.png"));while(capture.Pending)yield return .1;
        var tagFeedback=option.button.GetComponent<JuicyButton>();
        Menu.save.Select();tagFeedback.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .3;
        Check(tagFeedback.DrawScale.y>1.005f,"Tag row lacks hover feedback");
        tagFeedback.OnPointerDown(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return .3;
        Check(tagFeedback.DrawScale.y<.98f,"Tag row lacks press feedback");
        tagFeedback.OnPointerUp(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});tagFeedback.OnPointerExit(new PointerEventData(EventSystem.current));yield return .6;
        Check(Vector2.Distance(tagFeedback.DrawScale,Vector2.one)<.01f,"Tag row feedback did not reset");
        Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).tagIds.Count==2,"Multiple tag selection was not saved");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.schedule);yield return .2;Click(Menu.tomorrow);yield return .3;
        Check(!owner.IsEditing&&Menu.datePicker.SelectedDate==service.LocalNow.Date.AddDays(1),"Inline tomorrow shortcut wrong");Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).date==TaskService.DateKey(service.LocalNow.AddDays(1)),"Tomorrow not persisted");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.schedule);yield return .2;Check(Menu.datePicker.calendar.gameObject.activeInHierarchy&&!Menu.datePicker.overlay.activeSelf,"Calendar must be visible immediately inside schedule");yield return .3;
        Check(!owner.IsEditing&&Menu.datePicker.calendar.gameObject.activeInHierarchy&&!Menu.datePicker.overlay.activeSelf,"Embedded calendar failed");
        Menu.datePicker.SelectDate(service.LocalNow.Date.AddDays(3));yield return .2;Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).date==TaskService.DateKey(service.LocalNow.AddDays(3)),"Chosen date not persisted");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.schedule);yield return .2;Click(Menu.today);yield return .3;Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        var saved=service.TaskSnapshot.tasks.Single(t=>t.id==id);
        Check(saved.date==TaskService.DateKey(service.LocalNow)&&saved.minuteOfDay==795&&saved.description=="Keep my description"&&saved.subtasks.Single().id=="step","Shortcut altered unrelated fields");
        var reload=TaskCalendarPlayVerification.ReadPersistedAsync<QuestTask>(PlayerRecordKind.Tasks,id);
        foreach(var wait in TaskCalendarPlayVerification.WaitUntil(()=>reload.IsCompleted,"Task metadata reload timed out"))yield return wait;
        Check(reload.GetAwaiter().GetResult().tagIds.Contains(tag)&&reload.GetAwaiter().GetResult().difficulty==TaskDifficulty.Hard,"Reload lost metadata edits");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.schedule);yield return .2;Click(Menu.clearDate);yield return .3;
        Check(!Menu.datePicker.HasDate,"Clear date shortcut failed");Click(Menu.back);Menu.Dismiss();
        Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).scheduled,"Cancelled clear changed schedule");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.delete);yield return .2;
        Check(Dialog(owner).Root.activeSelf,"Delete lacked confirmation");Dialog(owner).Dismiss();Check(service.TaskSnapshot.tasks.Any(t=>t.id==id),"Cancelled delete removed task");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.complete);yield return .2;
        Check(Dialog(owner).Root.activeSelf,"Incomplete subtasks lacked confirmation");
        Dialog(owner).Root.transform.Find("Popup/Confirm").GetComponent<Button>().onClick.Invoke();yield return .3;
        PlayerHistoryPage historyPage=null;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.QueryHistoryPageAsync(),value=>historyPage=value))yield return wait;
        Check(historyPage.Rows.Count(v=>v.task.id==id)==1,"Completion not settled once");
        var wallet=service.FishCoins;
        // A late duplicate callback on an inactive menu cannot pay again.
        menu.complete.onClick.Invoke();Check(service.FishCoins==wallet,"Duplicate action changed rewards");
        owner.SelectTab(TaskTab.History);yield return .3;
        var history=historyPage.Rows.Single(v=>v.task.id==id);
        Click(owner.TutorialTaskTarget(history.Key),PointerEventData.InputButton.Right);Check(!Menu,"History became editable");
        owner.CalendarUndo(history.history.id);yield return .3;
        Check(service.FishCoins==wallet,"Undo changed earned rewards");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Check(nav.ShowPage(MainMenuPage.Dashboard),"Cannot leave page");Check(!Menu,"Page closure leaked popup");
        nav.ShowPage(MainMenuPage.Tasks);yield return .3;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.SaveTagAsync(null,"External change")))yield return wait;
        Check(!Menu,"External update left stale menu open");
        string daily=null;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.UpsertAsync(new QuestTask { title="Daily preview",daily=true,scheduled=true,date=TaskService.DateKey(service.LocalNow),repeatInterval=1,subtasks=new List<QuestSubtask>{new QuestSubtask{id="daily-step",title="Daily progress"}} },Array.Empty<string>()),value=>daily=value))yield return wait;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.SetSubtaskAsync(daily,"daily-step",true)))yield return wait;
        owner.calendar.SetMode(TaskDisplayMode.Calendar);owner.calendar.SelectDate(service.LocalNow.Date.AddDays(1));yield return .5;
        var row=owner.calendar.rows.Find("CalendarTask_"+daily);Check(row,"Preview row missing");
        Click(row,PointerEventData.InputButton.Right);Check(Menu&&!Menu.complete.interactable,"Future preview can complete");yield return .3;
        var disabledHint=Menu.complete.GetComponent<ControlHint>();disabledHint.OnPointerEnter(new PointerEventData(EventSystem.current));yield return .3;
        Check(Mathf.Abs(disabledHint.expansion.rect.width-disabledHint.collapsedWidth)<.1f&&disabledHint.expansionLabel.alpha==0,"Disabled Complete expanded as available");
        capture.Begin(Path.GetFullPath(Folder+"/calendar-preview-menu.png"));while(capture.Pending)yield return .1;
        Click(Menu.difficulty);yield return .2;Click(Menu.difficultyOptions[0]);Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        Check(!owner.IsEditing&&service.Query(TaskTab.Daily).Single(v=>v.task.id==daily).occurrence.completedSubtaskIds.Contains("daily-step"),"Preview inline save lost today's progress");
        row=owner.calendar.rows.Find("CalendarTask_"+daily);Click(row,PointerEventData.InputButton.Right);yield return .3;
        Click(Menu.edit);yield return .3;Check(owner.IsEditing,"Full Edit task action failed");owner.CancelDraft();
        owner.calendar.CloseDrawer();owner.calendar.SetMode(TaskDisplayMode.List);yield return .3;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,daily);yield return .2;Click(Menu.schedule);yield return .2;Click(Menu.clearDate);yield return .2;
        Check(Menu.status.text.Contains("stop repetition"),"Clearing repeat date did not explain conversion");Click(Menu.save);foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
        var converted=service.TaskSnapshot.tasks.Single(t=>t.id==daily);
        Check(!converted.daily&&!converted.scheduled,"Clear date did not stop repetition");
        Check(service.Query(TaskTab.Todo).Single(v=>v.task.id==daily).occurrence.completedSubtaskIds.Contains("daily-step"),"Clear date lost progress");
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,id);yield return .2;Click(Menu.delete);yield return .2;
        Dialog(owner).Root.transform.Find("Popup/Confirm").GetComponent<Button>().onClick.Invoke();yield return .3;
        Check(!service.TaskSnapshot.tasks.Any(t=>t.id==id)&&service.FishCoins==wallet,"Delete altered rewards or failed");
        foreach(var existingTag in service.TagCatalog)foreach(var wait in TaskCalendarPlayVerification.Await(service.DeleteTagAsync(existingTag.id)))yield return wait;
        owner.SelectTab(TaskTab.Todo);yield return .3;Open(owner,daily);yield return .2;Click(Menu.tags);yield return .3;
        Check(Menu.emptyTags.gameObject.activeInHierarchy&&!Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().Any(),"Empty tag state failed");
        capture.Begin(Path.GetFullPath(Folder+"/inline-empty-tags.png"));while(capture.Pending)yield return .1;
        Menu.Dismiss();
        File.WriteAllText(Folder+"/checks.txt",DateTime.UtcNow.ToString("O")+"\nPASS: right-click raycasts, menu/submenu bounds and captures at 1920x1080, 1280x720, 1024x768 and 2560x1080 with both sidebar states; one icon column, green Complete, rendered width/fade motion, six unclipped labels, rapid reversal, mid-animation dismissal, reduced motion, disabled Complete and expanded edge bounds; shared feedback hover/press/release and stable hit area; Shift+F10/Escape (embedded calendar) via temporary input device and keyboard submit; inline tag-row feedback; edge clamping; focus/page dismissal; inline cancellation; inline save failure/retry; difficulty/tags/schedule saved without full editor and reload; repeat-clear/progress preservation; subtask confirmation; exactly-once completion; History restriction/undo; stale refresh; future daily preview edit restrictions; confirmed delete.\n");
    }
}
