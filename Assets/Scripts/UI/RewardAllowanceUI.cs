using TMPro;
using UnityEngine;

public sealed class RewardAllowanceUI : MonoBehaviour
{
    public TMP_Text summary;
    private TaskService service;
    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += Refresh; Refresh();
    }
    private void OnDisable() { if (service != null) service.Changed -= Refresh; }
    private void Refresh()
    {
        var day = service.TodayRewards;
        summary.text = $"Task rewards: {day.taskExperience}/120 EXP  |  {day.taskFishCoins}/60 Fish Coins\n" +
            $"Focus rewards: {day.focusMinutes}/120 minutes  |  {day.focusTickets}/4 Tickets\n" +
            "Daily goal rewards are extra. Goals reset at midnight.";
    }
}
