using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DailyGoalUI : MonoBehaviour
{
    public DailyRewardGoal goal;
    public TMP_Text progress, claimLabel, feedback;
    public Slider progressBar;
    public Button claim;
    public GameObject inProgress;
    public TMP_Text inProgressLabel;
    public TMP_Text experienceReward, fishReward;
    [SerializeField] private TMP_Text ticketReward;
    public Image rowBackground, progressFill;
    public Sprite inProgressSprite, completedSprite;
    public Sprite inProgressFillSprite, completedFillSprite;
    private SqlitePlayerService service;
    private TMP_Text taskHeading;
    private bool saving;
    private DailyRewardGoal DisplayGoal => goal == DailyRewardGoal.ThreeQuests ? service.CurrentTaskGoal : goal;
    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Service;
        if (goal == DailyRewardGoal.ThreeQuests) taskHeading = transform.Find("Text_Info")?.GetComponent<TMP_Text>();
        service.Changed += Refresh;
        claim.onClick.AddListener(Claim);
        Refresh();
    }
    private void OnDisable()
    {
        if (service != null) service.Changed -= Refresh;
        claim.onClick.RemoveListener(Claim);
    }
    private async void Claim()
    {
        if(saving || !service.IsReady)return;
        saving=true;claim.interactable=false;
        bool saved;
        try {saved=(await service.ClaimDailyGoalAsync(DisplayGoal)).Succeeded;}finally{saving=false;}
        if (!this || !isActiveAndEnabled) return;
        feedback.text = saved ? "Reward claimed!" : "Save failed. Retry.";
        if (!saved) Debug.LogWarning(service.LastError);
        Refresh();
    }
    private void Refresh()
    {
        var displayed = DisplayGoal;
        int target = TaskService.DailyGoalTarget(displayed);
        int value = service.DailyGoalProgress(displayed);
        bool claimed = service.DailyGoalClaimed(displayed);
        var reward=service.PreviewDailyGoal(displayed);
        if (taskHeading)
            taskHeading.text = displayed == DailyRewardGoal.FirstQuest ? "Complete 1 task" : "Complete 3 tasks";
        if(experienceReward) experienceReward.text=reward.experience.ToString();
        if(fishReward) fishReward.text=reward.fishCoins.ToString();
        if (ticketReward)
        {
            bool hasTickets = reward.tickets > 0;
            if (hasTickets) ticketReward.text = reward.tickets.ToString();
            ticketReward.transform.parent.gameObject.SetActive(hasTickets);
        }
        progress.text = $"{value} / {target}" + (goal == DailyRewardGoal.Focus25Minutes ? " min" : "");
        progressBar.maxValue = target;
        progressBar.SetValueWithoutNotify(claimed ? target : value);
        claimLabel.text = claimed ? "Claimed" : value >= target ? "Claim" : "In Progress";
        claim.interactable = !saving && service.IsReady && !claimed && value >= target;
        bool ready = value >= target;
        if (inProgress)
        {
            claim.gameObject.SetActive(ready && !claimed);
            inProgress.SetActive(!ready || claimed);
            inProgressLabel.text = claimed ? "Claimed" : "In Progress";
        }
        if (rowBackground)
        {
            rowBackground.sprite = ready || claimed ? completedSprite : inProgressSprite;
            rowBackground.color = ready || claimed ? new Color32(222, 240, 255, 255) : Color.white;
        }
        if (progressFill)
        {
            progressFill.sprite = ready || claimed ? completedFillSprite : inProgressFillSprite;
            progressFill.color = Color.white;
        }
        progress.color = new Color32(65, 154, 239, 255);
        if (claimed) feedback.text = "";
        else if (feedback.text == "Reward claimed!") feedback.text = "";
    }
}
