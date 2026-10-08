using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class TaskSystemPlayVerification
{
    private static int step;
    private static double next;
    private static string taskId;
    private static string dailyId;
    private static string groupedId;
    static TaskSystemPlayVerification() { EditorApplication.playModeStateChanged += StateChanged; }
    [MenuItem("Tools/Cat Quest/Verify MainMenu Play Flow")]
    public static void Run()
    {
        if(EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/TaskSystemPlayVerification",Guid.NewGuid().ToString("N"),"save.json"));
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",path);
        SessionState.SetBool("CatQuest.PlayVerification",true);
        EditorApplication.isPlaying=true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("CatQuest.PlayVerification",false))
        {
            EditorApplication.isPaused=false; step=0; next=EditorApplication.timeSinceStartup+2; EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);
            SessionState.SetBool("CatQuest.PlayVerification",false); EditorApplication.update-=Tick;
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if(EditorApplication.timeSinceStartup<next) return;
        try
        {
            var system=UnityEngine.Object.FindFirstObjectByType<TaskSystemUI>();
            Check(system && system.Service!=null,"MainMenu did not initialize the task system.");
            if(step==0)
            {
                Check(system.Service.Snapshot.tasks.Count==0 && system.fishCoins.text=="0","Mock tasks or balances were imported.");
                Canvas.ForceUpdateCanvases();
                var listPanel=(RectTransform)system.listContent.parent.parent;
                Check(!system.detailsRoot.gameObject.activeSelf && listPanel.rect.width>((RectTransform)system.detailsRoot.parent).rect.width*.95f,"Empty selection did not give the list full width.");
                Check(system.search.customCaretColor && system.search.caretColor==TaskUIBuilder.Blue,"Search caret is not blue.");
                var navigation=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
                Check(navigation && navigation.pages.Length==6,"Navigation not connected.");
                navigation.buttons[0].onClick.Invoke(); Canvas.ForceUpdateCanvases();
                var headerPosition=navigation.tickets.rectTransform.position;
                for(int page=0;page<6;page++) {
                    navigation.buttons[page].onClick.Invoke();
                    Check(navigation.CurrentPage==(MainMenuPage)page && navigation.pages.Count(p=>p.activeSelf)==1 && navigation.pages[page].activeSelf,"Sidebar did not switch exclusively to its page.");
                    Canvas.ForceUpdateCanvases();
                    Check(Vector3.Distance(navigation.tickets.rectTransform.position,headerPosition)<1,"Switching page "+page+" moved shared header: "+navigation.tickets.rectTransform.position+" expected "+headerPosition+
                        "; content="+navigation.taskSystem.transform.parent.GetComponent<RectTransform>().rect+
                        "; page="+navigation.pages[page].GetComponent<RectTransform>().rect+
                        "; controlHeight="+navigation.taskSystem.transform.parent.GetComponent<VerticalLayoutGroup>().childControlHeight+
                        "; forceHeight="+navigation.taskSystem.transform.parent.GetComponent<VerticalLayoutGroup>().childForceExpandHeight+
                        "; alignment="+navigation.taskSystem.transform.parent.GetComponent<VerticalLayoutGroup>().childAlignment);
                }
                navigation.buttons[0].onClick.Invoke(); Capture(system); File.Copy("Temp/task-system-preview.png","Temp/navigation-dashboard-preview.png",true);
                navigation.buttons[2].onClick.Invoke(); Capture(system); File.Copy("Temp/task-system-preview.png","Temp/navigation-focus-preview.png",true);
                navigation.buttons[1].onClick.Invoke();
                Check(system.gameObject.activeInHierarchy && system.tickets.text=="0","Task page or ticket HUD failed.");
                for(int i=0;i<5;i++) {
                    system.SelectTab((TaskTab)i);
                    for(int j=0;j<5;j++) {
                        Color expected=i==j ? TaskUIBuilder.Blue : new Color32(121,143,173,255);
                        Check(system.tabButtons[j].transform.Find("Icon").GetComponent<Image>().color==expected,"Tab icon color failed.");
                        Check(system.tabCounts[j].transform.parent.GetComponent<Image>().color==expected && system.tabCounts[j].color==Color.white,"Tab badge color failed.");
                        Check(system.tabCounts[j].transform.parent.gameObject.activeSelf==(j<3),"Tags/History badge should be hidden.");
                    }
                }
                Check(system.search.textComponent.fontSize==16 && ((TMP_Text)system.search.placeholder).fontSize==16,"Search font failed.");
                navigation.buttons[1].onClick.Invoke();
                system.BeginAdd(); system.QuickAddInput.text="Navigation quick task";
                navigation.buttons[0].onClick.Invoke();
                Check(navigation.CurrentPage==MainMenuPage.Dashboard && system.Service.Snapshot.tasks.Count==1,"Leaving task page did not save quick input.");
                navigation.buttons[1].onClick.Invoke(); system.Service.Delete(system.Service.Snapshot.tasks.Single().id);
                system.SelectTab(TaskTab.Today); system.BeginAdd();
                var firstInput=system.QuickAddInput; system.BeginAdd(); Check(firstInput==system.QuickAddInput,"Duplicate quick-add row.");
                firstInput.text="   "; firstInput.onDeselect.Invoke(firstInput.text);
                Check(system.Service.Snapshot.tasks.Count==0 && !system.QuickAddInput,"Blank blur created task.");
                system.BeginAdd(); system.QuickAddInput.text="Escape task"; system.CancelQuickAdd();
                Check(system.Service.Snapshot.tasks.Count==0,"Cancel quick add saved task.");
                system.BeginAdd(); system.Refresh();
                Check(!TaskUIBuilder.Find(system.listContent,"AddTask"),"Plus is still shown during quick add.");
                var continuing=system.QuickAddInput; continuing.text="First quick task"; continuing.onSubmit.Invoke(continuing.text); continuing.DeactivateInputField();
                Check(system.QuickAddInput==continuing && continuing.text=="" && system.Service.Snapshot.tasks.Count==1,"Enter did not continue quick add.");
                step=11; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==11)
            {
                var continuing=system.QuickAddInput;
                Check(continuing && continuing.isFocused && !TaskUIBuilder.Find(system.listContent,"AddTask"),"Enter did not refocus input/hide plus after refresh.");
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-quick-add-preview.png",true);
                continuing.text="Second quick task"; continuing.onSubmit.Invoke(continuing.text); continuing.onSubmit.Invoke("");
                Check(system.Service.Snapshot.tasks.Count==2 && system.QuickAddInput==continuing,"Consecutive Enter saved duplicate/blank task.");
                step=12; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==12)
            {
                Check(system.QuickAddInput && system.QuickAddInput.isFocused,"Second Enter lost keyboard focus.");
                system.QuickAddInput.onDeselect.Invoke(""); system.Refresh();
                Check(!system.QuickAddInput && TaskUIBuilder.Find(system.listContent,"AddTask"),"Blur did not restore plus.");
                foreach(var created in system.Service.Snapshot.tasks) system.Service.Delete(created.id);
                system.BeginAdd(); system.QuickAddInput.text="UI integration task";
                var submitted=system.QuickAddInput; submitted.onDeselect.Invoke(submitted.text); submitted.onSubmit.Invoke(submitted.text);
                Check(system.Service.Snapshot.tasks.Count==1,"Blur/submit duplicated task.");
                taskId=system.Service.Snapshot.tasks.Single().id; system.SelectTask(taskId);
                Canvas.ForceUpdateCanvases();
                Check(system.detailsRoot.gameObject.activeSelf,"Selecting a task did not open details.");
                Check(system.detailsRoot.Find("ScrollRect/Content/Title/GameObject/Title").GetComponent<TMP_Text>().text=="UI integration task","Original info panel not populated.");
                TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke(); var form=system.editor;
                Check(form.gameObject.activeSelf && form.scheduleToggle.isOn,"Today add defaults failed.");
                form.taskTitle.text="UI integration task";
                form.timePicker.LoadValue(TimeSpan.Zero); Check(form.timePicker.hour==12 && !form.timePicker.isPm && form.SelectedTime==TimeSpan.Zero,"Midnight conversion failed.");
                form.timePicker.LoadValue(TimeSpan.FromHours(12)); Check(form.timePicker.hour==12 && form.timePicker.isPm && form.SelectedTime.Hours==12,"Noon conversion failed.");
                form.timePicker.LoadValue(new TimeSpan(23,59,0)); Check(form.timePicker.hour==11 && form.timePicker.isPm && form.SelectedTime.Hours==23,"PM conversion failed.");
                form.timePicker.LoadValue(new TimeSpan(13,17,0));
                form.addSubtask.onClick.Invoke(); var row=form.subtaskContent.Cast<Transform>().Last(t=>t.gameObject.activeSelf); row.GetComponentInChildren<TMP_InputField>().text="UI subtask";
                step=10; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==10)
            {
                if(!system.editor.tagSelection.gameObject.activeInHierarchy) throw new Exception("Tag selector is inactive.");
                system.editor.tagSelection.Show(); step=1; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==1)
            {
                var form=system.editor; var items=form.tagSelection.transform.Find("Dropdown List").GetComponentsInChildren<Toggle>(false);
                items[0].OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left}); form.tagSelection.Hide();
                Canvas.ForceUpdateCanvases();
                VerifyFormLayout(form);
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-edit-layout-preview.png",true);
                system.SaveDraft(); Check(!form.gameObject.activeSelf && system.Service.Snapshot.tasks.Count==1,"Add Task was not committed.");
                var task=system.Service.Snapshot.tasks.Single(); taskId=task.id;
                Check(task.minuteOfDay==13*60+17 && task.tagIds.Count==1 && task.subtasks.Count==1,"Schedule, tags or subtasks did not persist.");
                system.SelectTask(taskId);
                Canvas.ForceUpdateCanvases();
                var taskRow=TaskUIBuilder.Find(system.listContent,"Task_"+taskId);
                var dateRect=TaskUIBuilder.Find(taskRow,"DateTime").GetComponent<RectTransform>();
                var tagRect=TaskUIBuilder.Find(taskRow,"Tags").GetComponent<RectTransform>();
                var dateCorners=new Vector3[4]; var tagCorners=new Vector3[4]; dateRect.GetWorldCorners(dateCorners); tagRect.GetWorldCorners(tagCorners);
                Check(tagCorners[0].x>dateCorners[3].x,"Tags overlap date.");
                TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke();
                form.taskTitle.text="Cancelled title"; system.CancelDraft(); Check(system.Service.Snapshot.tasks.Single().title=="UI integration task","Cancel changed task data.");
                TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke(); form.description.text="Saved from MainMenu"; system.SaveDraft();
                Check(system.Service.Snapshot.tasks.Single().description=="Saved from MainMenu","Edit save failed.");
                system.CompleteSelected(taskId);
                var canvas=system.GetComponentInParent<Canvas>().rootCanvas;
                Check(canvas.transform.Find("TaskConfirmDialog").gameObject.activeSelf,"GUI Pro confirmation was not shown.");
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-confirm-preview.png",true);
                TaskUIBuilder.Find(canvas.transform.Find("TaskConfirmDialog"),"Cancel").GetComponent<Button>().onClick.Invoke();
                Check(system.Service.Snapshot.fishCoins==0,"Cancelling confirm completed task.");
                system.CompleteSelected(taskId);
                TaskUIBuilder.Find(canvas.transform.Find("TaskConfirmDialog"),"Confirm").GetComponent<Button>().onClick.Invoke();
                Check(system.Service.Snapshot.fishCoins==10 && system.Service.Snapshot.experience==10,"Normal task rewards failed.");
                Check(system.tickets.text=="0" && system.Service.Snapshot.tickets==0,"XP rewards were shown as tickets.");
                Check(system.tabCounts[0].text=="0" && system.tabCounts[4].text=="1","Tab counts did not refresh.");
                Check(!system.detailsRoot.gameObject.activeSelf && system.SelectedKey==null,"Completed selection kept the details panel open.");
                system.SelectTab(TaskTab.History); system.SelectTask(system.Service.Query(TaskTab.History).Single().Key);
                Check(!system.detailsRoot.Find("Button").gameObject.activeSelf,"History is not read-only.");
                var receiptId=system.Service.Query(TaskTab.History).Single().Key;
                var undoBox=TaskUIBuilder.Find(system.listContent,"Task_"+receiptId).Find("Image").GetComponent<Button>();
                Check(undoBox.interactable,"History checkbox did not allow reopening.");
                undoBox.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                Check(system.Service.Query(TaskTab.History).Count==0 && system.SelectedKey==null && !system.detailsRoot.gameObject.activeSelf,"History checkbox did not reopen/clear selection.");
                Check(system.Service.Snapshot.fishCoins==10,"Undo removed rewards.");
                system.SelectTab(TaskTab.Today);
                var completeBox=TaskUIBuilder.Find(system.listContent,"Task_"+taskId).Find("Image").GetComponent<Button>();
                completeBox.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                Check(system.Service.Query(TaskTab.History).Single().Key==receiptId && system.Service.Snapshot.fishCoins==10 && system.SelectedKey==null,"Checkbox completion selected the row, duplicated history or paid rewards again.");
                system.SelectTab(TaskTab.Daily); system.BeginAdd(); system.QuickAddInput.text="UI daily task";
                system.SelectTab(TaskTab.Daily); // Switching tabs submits using the originating defaults.
                dailyId=system.Service.Snapshot.tasks.First(t=>t.daily).id; system.SelectTask(dailyId);
                TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke();
                Check(form.dailyToggle.isOn && form.scheduleToggle.isOn,"Daily add defaults failed.");
                form.taskTitle.text="UI daily task"; form.difficulty.value=(int)TaskDifficulty.Hard; system.SaveDraft(); dailyId=system.Service.Snapshot.tasks.First(t=>t.daily).id;
                system.CompleteSelected(dailyId); system.CompleteSelected(dailyId);
                Check(system.Service.Snapshot.fishCoins==30 && system.Service.Snapshot.history.Count==2,"Daily completion paid twice or wrong reward.");
                var dailyBox=TaskUIBuilder.Find(system.listContent,"Task_"+dailyId).Find("Image").GetComponent<Button>();
                dailyBox.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                Check(!system.Service.Query(TaskTab.Daily).Single().Completed && system.Service.Snapshot.fishCoins==30,"Daily checkbox did not reopen without removing rewards.");
                dailyBox=TaskUIBuilder.Find(system.listContent,"Task_"+dailyId).Find("Image").GetComponent<Button>();
                dailyBox.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                Check(system.Service.Query(TaskTab.Daily).Single().Completed && system.Service.Snapshot.fishCoins==30,"Daily checkbox recompletion paid twice.");
                system.SelectTab(TaskTab.Todo); system.BeginAdd(); system.QuickAddInput.text="Temporary todo"; system.CommitQuickAdd();
                var temporary=system.Service.Snapshot.tasks.First(t=>t.title=="Temporary todo");
                Check(!temporary.scheduled,"Todo should default to no schedule.");
                system.SelectTask(temporary.id); TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke();
                form.taskTitle.text="Scrollable draft"; for(int i=0;i<16;i++) form.addSubtask.onClick.Invoke();
                step=2; next=EditorApplication.timeSinceStartup+1; return;
            }
            if(step==2)
            {
                var form=system.editor; Check(form.subtaskContent.rect.height>form.subtaskScroll.viewport.rect.height,"Subtask content does not scroll.");
                Check(form.subtaskScroll.verticalNormalizedPosition<.01f && form.subtaskScroll.viewport.GetComponent<Mask>(),"Subtask scrolling/masking failed.");
                system.CancelDraft(); Check(system.Service.Snapshot.tasks.First(t=>t.title=="Temporary todo").subtasks.Count==0,"Cancelling subtasks persisted them."); system.Service.Delete(system.Service.Snapshot.tasks.First(t=>t.title=="Temporary todo").id);
                var tagged=system.Service.Snapshot.tasks.First(t=>t.daily);
                var catalog=system.Service.Snapshot.tags;
                tagged.tagIds.Add(catalog[0].id); tagged.tagIds.Add(catalog[1].id);
                system.Service.Upsert(tagged,Array.Empty<string>(),out _);
                // Completed daily tasks are excluded from Tags, so create an unfinished one for grouping.
                var grouped=new QuestTask { title="Grouped task",tagIds=new System.Collections.Generic.List<string>{catalog[0].id,catalog[1].id} };
                system.Service.Upsert(grouped,Array.Empty<string>(),out groupedId);
                system.SelectTab(TaskTab.Tags);
                Check(!TaskUIBuilder.Find(system.listContent,"TagActions") && !TaskUIBuilder.Find(system.listContent,"TagFilters"),"Tags management toolbar still exists.");
                Check(TaskUIBuilder.Find(system.listContent,"TagGroup_"+catalog[0].id) && TaskUIBuilder.Find(system.listContent,"TagGroup_"+catalog[1].id),"Tag groups missing.");
                Check(system.listContent.GetComponentsInChildren<Button>(false).Count(button=>button.name=="Task_"+groupedId)==2,"Multi-tag task not rendered in both groups.");
                system.SelectTask(groupedId); Canvas.ForceUpdateCanvases();
                VerifyDifficultyLayout(system,groupedId);
                var tagContainer=system.detailsRoot.Find("ScrollRect/Content/Tags/Tags");
                foreach(Transform chip in tagContainer) {
                    if(!chip.gameObject.activeSelf) continue;
                    var chipText=chip.GetComponentInChildren<TMP_Text>();
                    Check(chipText.rectTransform.anchorMin==Vector2.zero && chipText.rectTransform.anchorMax==Vector2.one,"Tag text not contained by pill.");
                }
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-tags-preview.png",true);
                var manyTags=system.Service.Snapshot.tasks.First(t=>t.id==groupedId);
                manyTags.tagIds=catalog.Select(tag=>tag.id).ToList(); system.Service.Upsert(manyTags,Array.Empty<string>(),out _);
                system.SelectTask(groupedId); Canvas.ForceUpdateCanvases();
                var lines=tagContainer.Cast<Transform>().Where(chip=>chip.gameObject.activeSelf).Select(chip=>Mathf.RoundToInt(((RectTransform)chip).anchoredPosition.y)).Distinct().Count();
                Check(lines>1,"Many detail tags did not wrap.");
                TaskUIBuilder.Find(system.detailsRoot,"Edit Button").GetComponentInChildren<Button>().onClick.Invoke();
                step=13; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==13)
            {
                var form=system.editor; Canvas.ForceUpdateCanvases();
                Check(((RectTransform)form.tagContent.parent).rect.height>48,"Multiple edit tags did not grow the selector.");
                var active=form.tagContent.Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
                Check(active.Select(t=>Mathf.RoundToInt(((RectTransform)t).anchoredPosition.y)).Distinct().Count()>1,"Multiple edit tags did not wrap.");
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-edit-many-tags-preview.png",true);
                foreach(var chip in active) chip.GetComponent<Button>().onClick.Invoke();
                step=14; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==14)
            {
                var form=system.editor; Canvas.ForceUpdateCanvases();
                var selector=(RectTransform)form.tagContent.parent;
                Check(Mathf.Abs(selector.rect.height-48)<.5f && form.tagSelection.captionText.gameObject.activeSelf,"Empty tag selector did not shrink/show placeholder.");
                var caption=form.tagSelection.captionText.rectTransform;
                Check(Mathf.Abs(caption.TransformPoint(caption.rect.center).y-selector.TransformPoint(selector.rect.center).y)<1,"Add tags placeholder is not vertically centered.");
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-edit-empty-tags-preview.png",true);
                system.CancelDraft();
                var longTask=system.Service.Snapshot.tasks.Single(t=>t.id==groupedId);
                longTask.title=new string('W',100);
                longTask.description="Today is a nice day";
                Check(system.Service.Upsert(longTask,Array.Empty<string>(),out _),system.Service.LastError);
                system.SelectTask(groupedId);
                step=15; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==15)
            {
                Canvas.ForceUpdateCanvases();
                var heading=(RectTransform)system.detailsRoot.Find("ScrollRect/Content/Title");
                var schedule=(RectTransform)system.detailsRoot.Find("ScrollRect/Content/Schedule");
                var title=heading.Find("GameObject/Title").GetComponent<TMP_Text>();
                Check(heading.rect.height>50 && title.rectTransform.rect.height>=title.GetPreferredValues(title.text,title.rectTransform.rect.width,0).y-.5f,"Very long detail title did not grow its heading.");
                var headingCorners=new Vector3[4]; var scheduleCorners=new Vector3[4]; heading.GetWorldCorners(headingCorners); schedule.GetWorldCorners(scheduleCorners);
                Check(scheduleCorners[1].y<=headingCorners[0].y,"Long detail heading overlaps Schedule.");
                var tags=(RectTransform)system.detailsRoot.Find("ScrollRect/Content/Tags/Tags");
                var rewardIcon=(RectTransform)system.detailsRoot.Find("ScrollRect/Content/Rewards/Title/Icon");
                var tagCorners=new Vector3[4]; var iconCorners=new Vector3[4]; tags.GetWorldCorners(tagCorners); rewardIcon.GetWorldCorners(iconCorners);
                Check(iconCorners[1].y<=tagCorners[0].y,"Wrapped detail tags overlap Rewards.");
                VerifyDescriptionLayout(system);
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-max-title-preview.png",true);
                var described=system.Service.Snapshot.tasks.Single(t=>t.id==groupedId);
                described.description=string.Join("\n",Enumerable.Repeat("A longer description that wraps within the task details panel.",6));
                Check(system.Service.Upsert(described,Array.Empty<string>(),out _),system.Service.LastError);
                step=16; next=EditorApplication.timeSinceStartup+.5; return;
            }
            if(step==16)
            {
                Canvas.ForceUpdateCanvases(); VerifyDescriptionLayout(system);
                Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-description-preview.png",true);
                VerifyOutsideDismissal(system);
                system.Service.Delete(groupedId);
                var dialog=system.GetComponentInParent<Canvas>().rootCanvas.transform.Find("TaskDialog");
                system.SelectTab(TaskTab.Daily); system.SelectTask(dailyId); TaskUIBuilder.Find(system.detailsRoot,"DeleteButton").GetComponentInChildren<Button>().onClick.Invoke();
                TaskUIBuilder.Find(system.GetComponentInParent<Canvas>().rootCanvas.transform.Find("TaskConfirmDialog"),"Confirm").GetComponent<Button>().onClick.Invoke(); Check(!system.Service.Snapshot.tasks.Any(t=>t.id==dailyId) && system.Service.Snapshot.history.Count==2,"Daily delete removed history.");
                var disk=new PlayerDataStore(GameDataManager.Instance.SavePath); Check(disk.Data.history.Count==2 && disk.Data.fishCoins==30,"UI changes were not saved on disk.");
                system.SelectTab(TaskTab.History); system.SelectTask(system.Service.Query(TaskTab.History).First().Key);
                var deletedHistory=system.Service.Query(TaskTab.History).Single(v=>v.task.id==dailyId);
                Check(!TaskUIBuilder.Find(system.listContent,"Task_"+deletedHistory.Key).Find("Image").GetComponent<Button>().interactable,"Deleted History checkbox remained enabled.");
                system.search.text="UI integration"; Check(system.listContent.GetComponentsInChildren<Button>(false).Count(b=>b.name.StartsWith("Task_"))==1,"Search UI did not filter history.");
                system.search.text=""; system.filterButton.onClick.Invoke();
                var normal=dialog.GetComponentsInChildren<Toggle>(false).First(t=>t.name=="Normal"); normal.isOn=true;
                TaskUIBuilder.Find(dialog,"Apply").GetComponent<Button>().onClick.Invoke(); Check(system.listContent.GetComponentsInChildren<Button>(false).Count(b=>b.name.StartsWith("Task_"))==1,"Difficulty filter UI failed.");
                system.SelectTab(TaskTab.History); system.SelectTask(system.Service.Query(TaskTab.History).First(v=>v.task.title=="UI integration task").Key);
                Capture(system);
                File.WriteAllText(Path.Combine(Application.dataPath,"../Temp/task-system.play-result"),"PASS: full-width unselected list; independent completion/undo checkboxes; retained rewards and receipts; disabled deleted history; blue carets; description alignment; single/multiple/empty centered tag selectors; three difficulty badges and growing long-title heading; precise overdue color; six sidebar pages; quick-add; edit/cancel; grouped/wrapped tags; date/time/subtasks; completion confirmation; counters/history; scrolling/mask; delete; disk persistence.\nTest save: "+GameDataManager.Instance.SavePath);
            }
        }
        catch(Exception e) { File.WriteAllText(Path.Combine(Application.dataPath,"../Temp/task-system.play-result"),"FAIL: "+e); Debug.LogException(e); }
        EditorApplication.update-=Tick; SessionState.SetBool("CatQuest.PlayVerification",false); Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null); EditorApplication.isPlaying=false;
    }
    private static void VerifyDescriptionLayout(TaskSystemUI system)
    {
        var text=system.detailsRoot.Find("ScrollRect/Content/Description/Title").GetComponent<TMP_Text>();
        Check(text.rectTransform.rect.height>=text.GetPreferredValues(text.text,text.rectTransform.rect.width,0).y-.5f,"Description is clipped by its text rectangle.");
        var schedule=(RectTransform)system.detailsRoot.Find("ScrollRect/Content/Schedule");
        var descriptionCorners=new Vector3[4]; var scheduleCorners=new Vector3[4];
        text.rectTransform.GetWorldCorners(descriptionCorners); schedule.GetWorldCorners(scheduleCorners);
        Check(scheduleCorners[1].y<=descriptionCorners[0].y,"Description overlaps Schedule.");
    }
    private static void VerifyOutsideDismissal(TaskSystemUI system)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var capture=typeof(TaskSystemUI).GetMethod("CaptureOutsideClick",flags);
        string selected=system.SelectedKey;
        capture.Invoke(system,new object[]{system.detailsRoot.Find("Button")}); system.SendMessage("LateUpdate");
        Check(system.SelectedKey==selected,"Click inside details closed the panel.");
        capture.Invoke(system,new object[]{TaskUIBuilder.Find(system.listContent,"Task_"+groupedId)}); system.SendMessage("LateUpdate");
        Check(system.SelectedKey==selected,"Task row click dismissed the selection.");
        system.filterButton.onClick.Invoke();
        capture.Invoke(system,new object[]{system.search.transform}); system.SendMessage("LateUpdate");
        Check(system.SelectedKey==selected,"An open filter popup allowed outside dismissal.");
        typeof(TaskSystemUI).GetMethod("CloseModal",flags).Invoke(system,null);
        var dialog=(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",flags).GetValue(system);
        dialog.Show("Test","Keep the selected details.",()=>{});
        capture.Invoke(system,new object[]{system.search.transform}); system.SendMessage("LateUpdate");
        Check(system.SelectedKey==selected,"An open confirmation allowed outside dismissal."); dialog.Dismiss();
        capture.Invoke(system,new object[]{system.search.transform});
        Check(system.SelectedKey==selected,"Outside dismissal moved layout before the clicked control could handle its event.");
        EventSystem.current.SetSelectedGameObject(system.search.gameObject); system.SendMessage("LateUpdate"); Canvas.ForceUpdateCanvases();
        Check(system.SelectedKey==null && !system.detailsRoot.gameObject.activeSelf && EventSystem.current.currentSelectedGameObject==system.search.gameObject,"Outside click did not dismiss details or stole input focus.");
        var listPanel=(RectTransform)system.listContent.parent.parent;
        Check(listPanel.rect.width>((RectTransform)system.detailsRoot.parent).rect.width*.95f,"Dismissed details did not restore full-width list.");
        system.SelectTask(groupedId);
        var empty=system.Service.Snapshot.tasks.Single(t=>t.id==groupedId); empty.description="";
        Check(system.Service.Upsert(empty,Array.Empty<string>(),out _),system.Service.LastError);
        Check(!system.detailsRoot.Find("ScrollRect/Content/Description").gameObject.activeSelf,"Empty description left a visible section.");
    }
    private static void VerifyFormLayout(EditTaskDetailsUI form)
    {
        foreach(var input in form.GetComponentsInChildren<TMP_InputField>(true))
            Check(input.customCaretColor && input.caretColor==TaskUIBuilder.Blue,"Edit form caret is not blue: "+input.name);
        var placeholder=(TMP_Text)form.description.placeholder;
        Check(placeholder.alignment==form.description.textComponent.alignment && placeholder.rectTransform.rect==form.description.textComponent.rectTransform.rect,"Description placeholder and text do not align.");
        var selector=(RectTransform)form.tagContent.parent;
        Check(Mathf.Abs(selector.rect.height-48)<.5f,"Tag selector differs from Schedule height: "+selector.rect.height);
        var chip=form.tagContent.Cast<Transform>().First(t=>t.gameObject.activeSelf);
        Vector3 chipCenter=((RectTransform)chip).TransformPoint(((RectTransform)chip).rect.center);
        Vector3 frameCenter=selector.TransformPoint(selector.rect.center);
        Check(Mathf.Abs(chipCenter.y-frameCenter.y)<1,"Tag chip is not vertically centered.");
    }
    private static void VerifyDifficultyLayout(TaskSystemUI system,string id)
    {
        var task=system.Service.Snapshot.tasks.Single(t=>t.id==id);
        task.title="A long task title that must wrap beside its difficulty badge without covering the badge";
        task.scheduled=true; task.date=TaskService.DateKey(system.Service.LocalNow.AddDays(-1));
        foreach(TaskDifficulty value in Enum.GetValues(typeof(TaskDifficulty)))
        {
            task.difficulty=value; Check(system.Service.Upsert(task,Array.Empty<string>(),out _),system.Service.LastError);
            system.SelectTask(id); Canvas.ForceUpdateCanvases();
            var row=TaskUIBuilder.Find(system.listContent,"Task_"+id);
            var badge=TaskUIBuilder.Find(row,"Normal_Difficulty");
            var text=badge.GetComponentInChildren<TMP_Text>(); var image=badge.GetComponentInChildren<Image>();
            Check(text.text==value.ToString() && text.color==TaskUIBuilder.DifficultyColor(value) && image.sprite==system.editor.difficultySprites[(int)value] && image.preserveAspect,"List difficulty badge mismatch.");
            var title=system.detailsRoot.Find("ScrollRect/Content/Title/GameObject/Title").GetComponent<RectTransform>();
            var detailBadge=title.parent.GetComponentsInChildren<Transform>(true).First(t=>t.name.Contains("Difficulty"));
            var corners=new Vector3[4]; var badgeCorners=new Vector3[4]; title.GetWorldCorners(corners); ((RectTransform)detailBadge).GetWorldCorners(badgeCorners);
            Check(corners[3].x<=badgeCorners[0].x,"Long title overlaps detail badge.");
            Check(detailBadge.GetComponentInChildren<TMP_Text>().text==value.ToString(),"Detail difficulty caption mismatch.");
            var date=TaskUIBuilder.Find(row,"DateTime").GetComponent<TMP_Text>();
            Check(date.color==TaskUIBuilder.Overdue && !date.text.Contains("Overdue"),"Overdue date is not red or still has a prefix.");
            Check(system.detailsRoot.Find("ScrollRect/Content/Schedule/GameObject/Title (2)").GetComponent<TMP_Text>().color==TaskUIBuilder.Overdue,"Detail overdue date is not red.");
            system.editor.LoadDraft(task,system.Service.Snapshot.tags,Array.Empty<string>());
            Check(system.editor.difficulty.captionText.text==value.ToString() && system.editor.difficulty.captionText.color==TaskUIBuilder.DifficultyColor(value),"Editor difficulty mismatch.");
        }
        Capture(system); File.Copy("Temp/task-system-preview.png","Temp/task-long-title-preview.png",true);
    }
    private static void Capture(TaskSystemUI system)
    {
        var canvases=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Canvas>(true)).Where(c=>c.isRootCanvas && c.gameObject.activeInHierarchy).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var go=new GameObject("Task System Preview Camera"); var camera=go.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.white; camera.orthographic=true; camera.orthographicSize=540; camera.nearClipPlane=.1f; camera.farClipPlane=1000;
        var rt=new RenderTexture(1920,1080,24); var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false); var old=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;
            for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=RenderMode.ScreenSpaceCamera;canvases[i].worldCamera=camera;canvases[i].planeDistance=100;}
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply(); File.WriteAllBytes(Path.Combine(Application.dataPath,"../Temp/task-system-preview.png"),texture.EncodeToPNG());
        }
        finally
        {
            for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i];canvases[i].planeDistance=distances[i];}
            RenderTexture.active=old; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture); Canvas.ForceUpdateCanvases();
        }
    }
}
