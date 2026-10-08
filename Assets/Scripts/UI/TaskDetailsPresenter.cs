using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Populates the original information panel without replacing its designed hierarchy.
public sealed class TaskDetailsPresenter
{
    private readonly RectTransform root;
    private readonly Transform body, tags, subtasks;
    private readonly GameObject tagTemplate, subtaskTemplate;
    private readonly TMP_Text title, description, schedule, fish, xp;
    private readonly Button complete, edit, delete;
    private TaskView currentView;
    public TaskDetailsPresenter(RectTransform root, TaskUIBuilder ui)
    {
        this.root=root;
        body=root.Find("ScrollRect/Content");
        title=body.Find("Title/GameObject/Title").GetComponent<TMP_Text>();
        var titleLayout=title.transform.parent.GetComponent<HorizontalLayoutGroup>();
        titleLayout.childControlWidth=titleLayout.childControlHeight=true;
        titleLayout.childForceExpandWidth=titleLayout.childForceExpandHeight=false;
        titleLayout.childAlignment=TextAnchor.MiddleLeft; titleLayout.spacing=12;
        TaskUIBuilder.Flex(title.transform);
        title.textWrappingMode=TextWrappingModes.Normal;
        description=body.Find("Description/Title").GetComponent<TMP_Text>();
        description.alignment=TextAlignmentOptions.TopLeft;
        description.textWrappingMode=TextWrappingModes.Normal;
        description.overflowMode=TextOverflowModes.Overflow;
        TaskUIBuilder.Flex(description.transform);
        schedule=body.Find("Schedule/GameObject/Title (2)").GetComponent<TMP_Text>();
        tags=body.Find("Tags/Tags"); subtasks=body.Find("SubTasks/TaskList");
        var templates=TaskUIBuilder.Rect(root,"DetailTemplates"); templates.gameObject.SetActive(false);
        var ignore=templates.gameObject.AddComponent<LayoutElement>(); ignore.ignoreLayout=true;
        tagTemplate=UnityEngine.Object.Instantiate(tags.GetChild(0).gameObject,templates);
        subtaskTemplate=UnityEngine.Object.Instantiate(subtasks.GetChild(0).gameObject,templates);
        var oldLayout=tags.GetComponent<LayoutGroup>();
        var tagPadding=oldLayout ? oldLayout.padding : new RectOffset(70,20,0,0);
        if(oldLayout) UnityEngine.Object.DestroyImmediate(oldLayout);
        var wrap=tags.gameObject.AddComponent<WrapLayoutGroup>(); wrap.spacing=new Vector2(10,8); wrap.padding=tagPadding;
        var tagSection=tags.parent.GetComponent<VerticalLayoutGroup>();
        if(tagSection) { tagSection.childControlWidth=true; tagSection.childForceExpandWidth=true; tagSection.childControlHeight=true; tagSection.childForceExpandHeight=false; tagSection.padding.bottom=12; }
        TaskUIBuilder.Flex(tags);
        fish=body.Find("Rewards/RewardsItem/FishCoins/Text (TMP)").GetComponent<TMP_Text>();
        xp=body.Find("Rewards/RewardsItem/EXP/Text (TMP)").GetComponent<TMP_Text>();
        complete=root.Find("Button/CompleteQuest").GetComponentInChildren<Button>(true);
        edit=root.Find("Button/Edit Button").GetComponentInChildren<Button>(true);
        delete=root.Find("Button/DeleteButton").GetComponentInChildren<Button>(true);
        // Content scrolls inside the original viewport; clipping content itself would hide long lists.
        var contentMask=body.GetComponent<RectMask2D>(); if(contentMask) UnityEngine.Object.Destroy(contentMask);
        var scroll=root.Find("ScrollRect").GetComponent<ScrollRect>();
        scroll.content=(RectTransform)body; scroll.viewport=(RectTransform)scroll.transform; scroll.horizontal=false;
        if(!scroll.GetComponent<RectMask2D>()) scroll.gameObject.AddComponent<RectMask2D>();
        var fitter=body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var contentSize=body.GetComponent<LayoutElement>(); if(contentSize) contentSize.ignoreLayout=true;
        var rect=(RectTransform)body; rect.anchorMin=new Vector2(0,1); rect.anchorMax=Vector2.one;
        rect.pivot=new Vector2(.5f,1); rect.anchoredPosition=Vector2.zero; rect.sizeDelta=Vector2.zero;
        var glow=root.Find("ScrollRect/BottomGlow"); if(glow) glow.gameObject.SetActive(false);
    }
    public void Render(TaskView view, Func<TaskView,string> scheduleText, TaskService service,
        Action<TaskView> onEdit, Action<string> onComplete, Action<string> onDelete, Action<string> error)
    {
        currentView=view;
        TaskUIBuilder.Clear(tags); TaskUIBuilder.Clear(subtasks);
        title.text=view==null ? "Task Details" : view.task.title;
        description.text=view==null ? "Select a task to view its details." : view.task.description;
        var descriptionLayout=description.transform.parent.GetComponent<HorizontalLayoutGroup>();
        descriptionLayout.childControlWidth=true;
        descriptionLayout.childControlHeight=true;
        descriptionLayout.childForceExpandHeight=false;
        foreach(Transform section in body)
            section.gameObject.SetActive(view!=null || section.name=="Title" || section.name=="Description");
        description.transform.parent.gameObject.SetActive(!string.IsNullOrWhiteSpace(description.text));
        var difficulty=body.Find("Title/GameObject").GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Contains("Difficulty"));
        if(difficulty) {
            difficulty.gameObject.SetActive(view!=null);
            if(view!=null) {
                var system=root.GetComponentInParent<TaskSystemUI>();
                TaskUIBuilder.DifficultyBadge(difficulty,view.task.difficulty,system.editor.difficultySprites);
                var badgeSize=TaskUIBuilder.Height((RectTransform)difficulty,34);
                badgeSize.minWidth=badgeSize.preferredWidth=80; badgeSize.flexibleWidth=0;
            }
        }
        root.Find("Button").gameObject.SetActive(view!=null && !view.IsHistory);
        complete.onClick.RemoveAllListeners(); edit.onClick.RemoveAllListeners(); delete.onClick.RemoveAllListeners();
        if(view==null) return;
        schedule.text=scheduleText(view);
        RefreshScheduleColor(service);
        foreach(var name in view.tagNames) {
            var chip=UnityEngine.Object.Instantiate(tagTemplate,tags); chip.SetActive(true);
            TaskUIBuilder.LayoutTag(chip,name);
        }
        body.Find("Tags").gameObject.SetActive(view.tagNames.Count>0);
        foreach(var sub in view.task.subtasks) {
            var row=UnityEngine.Object.Instantiate(subtaskTemplate,subtasks); row.SetActive(true); row.name="Subtask_"+sub.id;
            row.GetComponentInChildren<TMP_Text>(true).text=sub.title;
            var toggle=row.GetComponentInChildren<Toggle>(true); toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(view.Completed || view.occurrence.completedSubtaskIds.Contains(sub.id));
            toggle.interactable=!view.IsHistory && !view.Completed && service.Eligible(view.task);
            toggle.onValueChanged.AddListener(value=> { if(!service.SetSubtask(view.task.id,sub.id,value)) { error(service.LastError); toggle.SetIsOnWithoutNotify(!value); } });
        }
        body.Find("SubTasks").gameObject.SetActive(view.task.subtasks.Count>0);
        fish.text=(view.IsHistory ? view.history.fishCoins : TaskService.Reward(view.task.difficulty)).ToString();
        xp.text=(view.IsHistory ? view.history.experience : TaskService.Reward(view.task.difficulty)).ToString();
        complete.interactable=!view.Completed && service.Eligible(view.task);
        complete.onClick.AddListener(()=>onComplete(view.task.id)); edit.onClick.AddListener(()=>onEdit(view)); delete.onClick.AddListener(()=>onDelete(view.task.id));
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)body);
    }
    public void RefreshScheduleColor(TaskService service)
    {
        if(currentView!=null) schedule.color=service.IsOverdue(currentView) ? TaskUIBuilder.Overdue : TaskUIBuilder.ScheduleInk;
    }
    public void RefreshLayout()
    {
        if (!root.gameObject.activeInHierarchy || title.rectTransform.rect.width <= 0) return;
        float height = Mathf.Max(50,title.GetPreferredValues(title.text,title.rectTransform.rect.width,0).y);
        var row = (RectTransform)title.transform.parent;
        var section = (RectTransform)row.parent;
        if (Mathf.Abs(section.rect.height-height)>.1f)
        {
            TaskUIBuilder.Height(title.rectTransform,height);
            TaskUIBuilder.Height(row,height); TaskUIBuilder.Height(section,height);
            row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            section.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)body);
        }
        var descriptionSection=(RectTransform)description.transform.parent;
        float descriptionWidth=((RectTransform)body).rect.width-body.GetComponent<VerticalLayoutGroup>().padding.horizontal;
        if(descriptionSection.gameObject.activeSelf && descriptionWidth>0 && Mathf.Abs(descriptionSection.rect.width-descriptionWidth)>.1f)
        {
            descriptionSection.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,descriptionWidth);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)body);
        }
        if(descriptionSection.gameObject.activeSelf && description.rectTransform.rect.width>0)
        {
            float textHeight=description.GetPreferredValues(description.text,description.rectTransform.rect.width,0).y;
            float descriptionHeight=textHeight+descriptionSection.GetComponent<HorizontalLayoutGroup>().padding.vertical+16;
            TaskUIBuilder.Height(description.rectTransform,textHeight);
            TaskUIBuilder.Height(descriptionSection,descriptionHeight);
            if(Mathf.Abs(descriptionSection.rect.height-descriptionHeight)>.1f)
            {
                descriptionSection.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,descriptionHeight);
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)body);
            }
        }
        // The original content layout reads section RectTransforms rather than controlling their height.
        var tagSection = (RectTransform)tags.parent;
        float tagHeight = Mathf.Max(100,tagSection.GetComponent<VerticalLayoutGroup>().preferredHeight);
        if (tagSection.gameObject.activeSelf && Mathf.Abs(tagSection.rect.height-tagHeight)>.1f)
        {
            tagSection.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,tagHeight);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)body);
        }
    }
}
