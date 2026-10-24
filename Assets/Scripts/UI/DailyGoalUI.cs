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
    public Image rowBackground, progressFill;
    public Sprite inProgressSprite, completedSprite;
    public Sprite inProgressFillSprite, completedFillSprite;
    private TaskService service;
    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += Refresh;
        claim.onClick.AddListener(Claim);
        Refresh();
    }
    private void OnDisable()
    {
        if (service != null) service.Changed -= Refresh;
        claim.onClick.RemoveListener(Claim);
    }
    private void Claim()
    {
        bool saved = service.ClaimDailyGoal(goal);
        feedback.text = saved ? "Reward claimed!" : "Save failed. Retry.";
        if (!saved) Debug.LogWarning(service.LastError);
        Refresh();
    }
    private void Refresh()
    {
        int target = goal == DailyRewardGoal.ThreeQuests ? 3 : 25;
        int value = service.DailyGoalProgress(goal);
        bool claimed = service.DailyGoalClaimed(goal);
        progress.text = $"{value} / {target}";
        progressBar.maxValue = target;
        progressBar.SetValueWithoutNotify(claimed ? target : value);
        claimLabel.text = claimed ? "Claimed" : value >= target ? "Claim" : "In Progress";
        claim.interactable = !claimed && value >= target;
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
