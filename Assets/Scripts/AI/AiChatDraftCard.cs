using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public sealed class AiChatDraftCard : MonoBehaviour
{
    public TMP_Text summary, title, schedule, subtasks, changes, state;
    public Transform difficulty, tags;
    public Sprite[] difficultySprites;
    public GameObject tagTemplate;
    public Sprite tagTintSprite;
    public Button review, dismiss;
    public void Bind(AiTaskDraft draft, Action edit, Action cancel)
    {
        var task=draft.task;
        title.richText=summary.richText=schedule.richText=subtasks.richText=changes.richText=false;
        title.text=task.title; summary.text=task.description;
        summary.gameObject.SetActive(!string.IsNullOrEmpty(task.description));
        schedule.text=task.daily?"Daily · "+task.date:task.scheduled?task.date:"No schedule";
        TaskUIBuilder.DifficultyBadge(difficulty,task.difficulty,difficultySprites);
        subtasks.text=string.Join("\n",task.subtasks.Select(s=>"• "+s.title));
        subtasks.transform.parent.gameObject.SetActive(task.subtasks.Count>0);
        state.text=draft.state=="saved"?"Saved":draft.state=="dismissed"?"Dismissed":"";
        state.gameObject.SetActive(state.text.Length>0);
        changes.text="";
        if(!string.IsNullOrEmpty(draft.targetId)&&!string.IsNullOrEmpty(draft.expected)) {
            int end=draft.expected.LastIndexOf("|",StringComparison.Ordinal);
            if(end>0){var old=JsonUtility.FromJson<QuestTask>(draft.expected.Substring(0,end));
                changes.text="Previously: "+old.title+" · "+(old.daily?"Daily · ":"")+(old.scheduled?old.date:"No schedule")+"\n"+old.description+"\n"+string.Join(" · ",old.subtasks.Select(s=>s.title));}
        }
        changes.gameObject.SetActive(changes.text.Length>0);
        foreach(Transform child in tags)Destroy(child.gameObject);
        var catalog=GameDataManager.EnsureInstance().Tasks.Snapshot.tags;
        foreach(var id in task.tagIds){var tag=catalog.FirstOrDefault(t=>t.id==id);if(tag==null)continue;
            var chip=Instantiate(tagTemplate,tags);chip.SetActive(true);TaskUIBuilder.LayoutTag(chip,tag.name);TagColors.Apply(chip,tag.colorHex,tagTintSprite);
            foreach(var button in chip.GetComponents<Button>())button.interactable=false;
        }
        tags.parent.gameObject.SetActive(task.tagIds.Count>0);
        review.interactable=dismiss.interactable=draft.state=="pending";
        review.onClick.AddListener(()=>edit());dismiss.onClick.AddListener(()=>cancel());
    }
}
