using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Read-only presentation of the same allowances used by task and Focus settlement.
public sealed class RewardLimitTooltip : MonoBehaviour
{
    public RectTransform card;
    public TMP_Text title, taskRewards, focusRewards, ticketRewards, note;
    public Slider taskBar, focusBar, ticketBar;
    public TMP_Text taskValue, focusValue, ticketValue;
    public float twoRowHeight = 292, ticketHeight = 198;
    TaskService service;
    RewardLimitHoverTarget source;
    MainMenuNavigation navigation;
    MainMenuPage page;
    Canvas canvas;

    public void Show(RewardLimitHoverTarget target)
    {
        source = target;
        navigation = GetComponentInParent<MainMenuNavigation>();
        if (navigation) page = navigation.CurrentPage;
        gameObject.SetActive(true);
        Refresh();
        Place();
    }
    public void Hide(RewardLimitHoverTarget target = null)
    {
        if (target && source != target) return;
        source = null;
        gameObject.SetActive(false);
    }
    void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += Refresh;
        canvas = GetComponentInParent<Canvas>().rootCanvas;
    }
    void OnDisable()
    {
        if (service != null) service.Changed -= Refresh;
        source = null;
    }
    void OnApplicationFocus(bool focused) { if (!focused) Hide(); }
    void Refresh()
    {
        if (service == null || !source) return;
        var day = service.TodayRewards;
        bool tickets = source.kind == RewardLimitKind.Tickets;
        bool experience = source.kind == RewardLimitKind.Experience;
        title.text = tickets ? "Daily Ticket limit" : experience ? "Daily EXP limits" : "Daily Fish limits";
        card.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, tickets ? ticketHeight : twoRowHeight);
        taskRewards.gameObject.SetActive(!tickets);
        focusRewards.gameObject.SetActive(!tickets);
        taskBar.gameObject.SetActive(!tickets);
        focusBar.gameObject.SetActive(!tickets);
        ticketRewards.gameObject.SetActive(tickets);
        ticketBar.gameObject.SetActive(tickets);
        string currency = experience ? "EXP" : "Fish";
        taskRewards.text = "Tasks";
        int used = experience ? day.taskExperience : day.taskFishCoins;
        int limit = experience ? service.TaskExperienceLimit : service.TaskFishLimit;
        SetBar(taskBar, used, limit);
        taskValue.text = $"{used} / {limit} {currency}";
        int focusLevel = service.FocusRewardLevel;
        int focusLimit = TaskService.ScaleRewardAmount(experience ? service.FocusDailyMinuteLimit : service.FocusDailyMinuteLimit / 5 * 2, focusLevel);
        focusRewards.text = $"Focus · max {focusLimit} {currency}/day";
        // Focus allowance is consumed in minutes, not paid currency. Short sessions and
        // level changes can yield different totals; a full bar must still mean no time remains.
        SetBar(focusBar, day.focusMinutes, service.FocusDailyMinuteLimit);
        focusValue.text = $"{day.focusMinutes} / {service.FocusDailyMinuteLimit} rewarded min";
        ticketRewards.text = "Focus";
        SetBar(ticketBar, day.focusTickets, TaskService.FocusTicketCap);
        ticketValue.text = $"{day.focusTickets} / {TaskService.FocusTicketCap} Tickets";
        int sessionLimit = TaskService.ScaleRewardAmount(experience ? service.FocusSessionMinuteLimit : service.FocusSessionMinuteLimit / 5 * 2, focusLevel);
        note.text = tickets ? "1 ticket / 25 Focus min · Resets at midnight" : $"Session: {sessionLimit} {currency} / {service.FocusSessionMinuteLimit} min\nResets at midnight · Bonuses are extra";
    }
    static void SetBar(Slider bar, int value, int limit)
    {
        bar.maxValue = Mathf.Max(1, limit);
        bar.SetValueWithoutNotify(value);
    }
    void LateUpdate()
    {
        if (!source || !source.isActiveAndEnabled || (navigation && navigation.CurrentPage != page)) { Hide(); return; }
        Place();
    }
    void Place()
    {
        if (!source || !canvas) return;
        var root = (RectTransform)transform;
        var bounds = root.rect;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var target = (RectTransform)source.transform;
        var screen = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(new Vector3(target.rect.xMax, target.rect.yMin)));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, camera, out var point)) return;
        float scale = Mathf.Min(1, (bounds.width - 24) / card.rect.width, (bounds.height - 24) / card.rect.height);
        card.localScale = Vector3.one * Mathf.Max(.1f, scale);
        var size = card.rect.size * card.localScale.x;
        card.anchoredPosition = new Vector2(
            Mathf.Clamp(point.x - size.x, bounds.xMin + 12, bounds.xMax - size.x - 12),
            Mathf.Clamp(point.y - 12, bounds.yMin + size.y + 12, bounds.yMax - 12));
    }
}
