using TMPro;
using UnityEngine;

public sealed class RewardAllowanceUI : MonoBehaviour
{
    public TMP_Text summary;
    private SqlitePlayerService service;
    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Service;
        service.Changed += Refresh; Refresh();
    }
    private void OnDisable() { if (service != null) service.Changed -= Refresh; }
    private void Refresh()
    {
        var day = service.TodayRewards;
        summary.text = $"Task rewards: {day.taskExperience}/{service.TaskExperienceLimit} EXP  |  {day.taskFishCoins}/{service.TaskFishLimit} Fish Coins\n" +
            $"Focus rewards: {day.focusMinutes}/{service.FocusDailyMinuteLimit} minutes  |  {day.focusTickets}/{TaskService.FocusTicketCap} Tickets\n" +
            $"Mission Tickets: {day.taskTickets + day.questsTickets}/{TaskService.FirstQuestTickets + TaskService.ThreeQuestTickets}. Resets at midnight.";
    }
}
