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
        summary.text = $"Task rewards: {day.taskExperience}/{service.TaskExperienceLimit} EXP  |  {day.taskFishCoins}/{service.TaskFishLimit} Fish Coins\n" +
            $"Focus rewards: {day.focusMinutes}/{service.FocusDailyMinuteLimit} minutes  |  {day.focusTickets}/{TaskService.FocusTicketCap} Tickets\n" +
            $"First Task Ticket: {day.taskTickets}/{TaskService.TaskTicketCap}. Daily goals are extra. Resets at midnight.";
    }
}
