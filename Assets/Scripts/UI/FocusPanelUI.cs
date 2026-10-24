using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public sealed class FocusPanelUI : MonoBehaviour
{
    public TMP_Text timer, status, message;
    public Slider progress;
    public Button start, endEarly, cancel, endRest, retry, dashboard;
    public MainMenuNavigation navigation;
    public bool quickStart;
    public Slider focusDuration, restDuration;
    public Button focusDecrease, focusIncrease, restDecrease, restIncrease;
    public Sprite durationDisabledSprite;
    public TMP_Text focusDurationLabel, restDurationLabel;
    public RectTransform questList;
    public TMP_InputField questSearch;
    public Button questDropdown;
    public RectTransform questPopup;
    public TMP_Text questContext, rewards, rewardTitle, allowance;
    public TMP_Text fishReward, ticketReward;
    public Slider rewardAllowanceBar, ticketAllowanceBar;
    public TMP_Text rewardAllowanceLabel, ticketAllowanceLabel;
    public TMP_FontAsset settingsFont;
    public Sprite settingsSprite;
    public Toggle pomodoroMode, stopwatchMode, countdownMode;
    public GameObject focusDurationCard, restDurationCard;
    private int pomodoroMinutes = 25, countdownMinutes = 25;
    public FocusController Controller => focus;
    private FocusController focus;
    private int displayedSecond = -1;
    private void OnEnable()
    {
        focus = FocusRunner.EnsureInstance().Controller;
        focus.Changed += Refresh;
        GameDataManager.EnsureInstance().Tasks.Changed += RefreshQuests;
        if (!quickStart)
        {
            if (pomodoroMode) pomodoroMode.onValueChanged.AddListener(ChoosePomodoro);
            if (stopwatchMode) stopwatchMode.onValueChanged.AddListener(ChooseStopwatch);
            if (countdownMode) countdownMode.onValueChanged.AddListener(ChooseCountdown);
            foreach (var button in new[] { focusDecrease, focusIncrease, restDecrease, restIncrease })
            {
                var colors = button.colors;
                colors.disabledColor = Color.white;
                button.colors = colors;
            }
            focusDuration.onValueChanged.AddListener(ChangeFocusDuration);
            restDuration.onValueChanged.AddListener(ChangeRestDuration);
            focusDecrease.onClick.AddListener(DecreaseFocus);focusIncrease.onClick.AddListener(IncreaseFocus);
            restDecrease.onClick.AddListener(DecreaseRest);restIncrease.onClick.AddListener(IncreaseRest);
            RefreshQuests();
            if (questSearch) { questSearch.onSelect.AddListener(OpenQuests); questSearch.onValueChanged.AddListener(SearchQuests); }
            if (questDropdown) questDropdown.onClick.AddListener(ToggleQuests);
            CloseQuests();
        }
        start.onClick.AddListener(StartFocus);
        if (endEarly) endEarly.onClick.AddListener(EndEarly);
        if (cancel) cancel.onClick.AddListener(Cancel);
        if (endRest) endRest.onClick.AddListener(EndRest);
        if (retry) retry.onClick.AddListener(Retry);
        if (dashboard) dashboard.onClick.AddListener(ReturnToDashboard);
        Refresh();
    }
    private void OnDisable()
    {
        if (pomodoroMode) pomodoroMode.onValueChanged.RemoveListener(ChoosePomodoro);
        if (stopwatchMode) stopwatchMode.onValueChanged.RemoveListener(ChooseStopwatch);
        if (countdownMode) countdownMode.onValueChanged.RemoveListener(ChooseCountdown);
        if (focus != null) focus.Changed -= Refresh;
        if (GameDataManager.Instance) GameDataManager.Instance.Tasks.Changed -= RefreshQuests;
        if (focusDuration) focusDuration.onValueChanged.RemoveListener(ChangeFocusDuration);
        if (questSearch) { questSearch.onSelect.RemoveListener(OpenQuests); questSearch.onValueChanged.RemoveListener(SearchQuests); }
        if (questDropdown) questDropdown.onClick.RemoveListener(ToggleQuests);
        CloseQuests();
        if (restDuration) restDuration.onValueChanged.RemoveListener(ChangeRestDuration);
        if(focusDecrease)focusDecrease.onClick.RemoveListener(DecreaseFocus);if(focusIncrease)focusIncrease.onClick.RemoveListener(IncreaseFocus);
        if(restDecrease)restDecrease.onClick.RemoveListener(DecreaseRest);if(restIncrease)restIncrease.onClick.RemoveListener(IncreaseRest);
        start.onClick.RemoveListener(StartFocus);
        if (endEarly) endEarly.onClick.RemoveListener(EndEarly);
        if (cancel) cancel.onClick.RemoveListener(Cancel);
        if (endRest) endRest.onClick.RemoveListener(EndRest);
        if (retry) retry.onClick.RemoveListener(Retry);
        if (dashboard) dashboard.onClick.RemoveListener(ReturnToDashboard);
    }
    private void Update()
    {
        if (focus != null && displayedSecond != FocusController.DisplaySecond(focus.DisplaySeconds, focus.CountsUp)) { RefreshTime(); RefreshSummary(); }
        if (questPopup && questPopup.gameObject.activeSelf)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CloseQuests();
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                var canvas = questPopup.GetComponentInParent<Canvas>();
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var point = Mouse.current.position.ReadValue();
                if (!RectTransformUtility.RectangleContainsScreenPoint(questPopup, point, camera)
                    && !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)questSearch.transform.parent, point, camera)) CloseQuests();
            }
        }
    }
    private void OpenQuests(string _) { if (Ready && questPopup) { questPopup.gameObject.SetActive(true); RefreshQuests(); } }
    private void SearchQuests(string _) { OpenQuests(null); }
    private void ToggleQuests() { if (!Ready || !questPopup) return; if (questPopup.gameObject.activeSelf) CloseQuests(); else { OpenQuests(null); questSearch.ActivateInputField(); } }
    private void CloseQuests()
    {
        if (questPopup) questPopup.gameObject.SetActive(false);
        if (questSearch) questSearch.SetTextWithoutNotify("");
    }
    private void StartFocus()
    {
        if (quickStart)
        {
            // A failed navigation (e.g. unsaved task input) must not start a hidden session.
            if (!navigation.ShowPage(MainMenuPage.Focus)) return;
            if (focus.Phase == FocusPhase.Focusing || focus.Phase == FocusPhase.Resting || focus.Phase == FocusPhase.Saving) return;
        }
        focus.Start();
    }
    private void EndEarly() { focus.EndEarly(); }
    private void Cancel() { focus.Cancel(); }
    private void EndRest() { focus.EndRest(); }
    private void Retry() { focus.RetrySettlement(); }
    private void ReturnToDashboard() { navigation.ShowPage(MainMenuPage.Dashboard); }
    private void Refresh()
    {
        var phase = focus.Phase;
        status.text = phase == FocusPhase.Focusing ? "Focus Training" : phase == FocusPhase.Resting ? "Rest" : "Focus Training";
        if (message) message.text = focus.Message;
        bool ready = phase == FocusPhase.Ready || phase == FocusPhase.Finished || phase == FocusPhase.Interrupted;
        start.gameObject.SetActive(quickStart || ready);
        start.GetComponentInChildren<TMP_Text>(true).text = quickStart && !ready ? "Open Focus" : phase == FocusPhase.Finished ? "Start Another Focus" : "Start Focus";
        if (endEarly) endEarly.gameObject.SetActive(phase == FocusPhase.Focusing);
        if (endEarly) endEarly.GetComponentInChildren<TMP_Text>(true).text = focus.CountsUp ? "End Focus" : "End Focus Early";
        if (cancel) cancel.gameObject.SetActive(phase == FocusPhase.Focusing);
        if (endRest) endRest.gameObject.SetActive(phase == FocusPhase.Resting);
        if (retry) retry.gameObject.SetActive(phase == FocusPhase.Saving);
        RefreshTime();
        RefreshSummary();
    }
    private bool Ready => focus != null && (focus.Phase == FocusPhase.Ready || focus.Phase == FocusPhase.Finished || focus.Phase == FocusPhase.Interrupted);
    private void ChoosePomodoro(bool selected) { if (selected) ChooseMode(FocusTimerMode.Pomodoro); }
    private void ChooseStopwatch(bool selected) { if (selected) ChooseMode(FocusTimerMode.Stopwatch); }
    private void ChooseCountdown(bool selected) { if (selected) ChooseMode(FocusTimerMode.Countdown); }
    private void ChooseMode(FocusTimerMode mode)
    {
        if (!Ready || mode == focus.Options.mode) return;
        var options = focus.Options.Copy();
        if (options.mode == FocusTimerMode.Pomodoro) pomodoroMinutes = options.focusMinutes;
        if (options.mode == FocusTimerMode.Countdown) countdownMinutes = options.focusMinutes;
        options.mode = mode;
        options.focusMinutes = mode == FocusTimerMode.Countdown ? countdownMinutes : pomodoroMinutes;
        focus.Configure(options);
    }
    private void DecreaseFocus(){focusDuration.value-=1;}
    private void IncreaseFocus(){focusDuration.value+=1;}
    private void DecreaseRest(){restDuration.value-=1;}
    private void IncreaseRest(){restDuration.value+=1;}
    private void ChangeFocusDuration(float value) => Configure(Mathf.RoundToInt(value), 0);
    private void ChangeRestDuration(float value) => Configure(0, Mathf.RoundToInt(value));
    private void Configure(int minutes, int rest)
    {
        var options = focus.Options.Copy();
        if (minutes > 0) options.focusMinutes = minutes;
        if (rest > 0) options.restMinutes = rest;
        focus.Configure(options);
        RefreshSummary();
    }
    private void RefreshQuests()
    {
        if (!questList || focus == null) return;
        foreach (Transform child in questList) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        var ui = new TaskUIBuilder(settingsFont, settingsSprite);
        AddQuest(ui, null);
        var service = GameDataManager.EnsureInstance().Tasks;
        string query = questSearch ? questSearch.text.Trim() : "";
        int matches = 0;
        foreach (var view in service.Query(TaskTab.Daily).Concat(service.Query(TaskTab.Todo)).Where(v => !v.Completed && service.Eligible(v.task)
            && (query.Length == 0 || v.task.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))) { AddQuest(ui, view); matches++; }
        if (matches == 0) { var empty = ui.Text(questList, query.Length == 0 ? "No available quests" : "No matching quests", 16); TaskUIBuilder.Height(empty.rectTransform, 40); }
        var scroll = questList.GetComponentInParent<ScrollRect>(); if (scroll) scroll.verticalNormalizedPosition = 1;
        RefreshSummary();
    }
    private void AddQuest(TaskUIBuilder ui, TaskView view)
    {
        var button = ui.Button(questList, view?.task.id ?? "NoQuest", view?.task.title ?? "No quest");
        TaskUIBuilder.Height((RectTransform)button.transform, 48);
        var label = button.GetComponentInChildren<TMP_Text>(); label.fontSize = 17; label.enableAutoSizing = true; label.fontSizeMin = 13; label.fontSizeMax = 17;
        button.onClick.AddListener(() => {
            var options = focus.Options.Copy(); options.questId = view?.task.id; options.questTitle = view?.task.title; options.occurrenceDay = view?.occurrence.day;
            focus.Configure(options);
            CloseQuests();
        });
    }
    private void SetDurationButton(Button button, bool enabled)
    {
        button.interactable = enabled;
        if (durationDisabledSprite && button.targetGraphic is Image background)
        {
            background.overrideSprite = enabled ? null : durationDisabledSprite;
            // Keep a solid, visible button against the pale slider track.
            background.color = enabled ? Color.white : new Color32(166, 166, 166, 255);
        }
        var icon = button.transform.Find("Icon");
        if (icon && icon.TryGetComponent<Image>(out var image))
            image.color = enabled ? Color.white : new Color32(96, 106, 120, 255);
    }
    private void RefreshSummary()
    {
        if (quickStart || focus == null) return;
        if (message) message.gameObject.SetActive(focus.Phase == FocusPhase.Saving && !string.IsNullOrEmpty(focus.Message));
        if (pomodoroMode) { pomodoroMode.SetIsOnWithoutNotify(focus.Options.mode == FocusTimerMode.Pomodoro); pomodoroMode.interactable = Ready; }
        if (stopwatchMode) { stopwatchMode.SetIsOnWithoutNotify(focus.Options.mode == FocusTimerMode.Stopwatch); stopwatchMode.interactable = Ready; }
        if (countdownMode) { countdownMode.SetIsOnWithoutNotify(focus.Options.mode == FocusTimerMode.Countdown); countdownMode.interactable = Ready; }
        if (focusDurationCard) focusDurationCard.SetActive(focus.Options.mode != FocusTimerMode.Stopwatch);
        if (restDurationCard) restDurationCard.SetActive(focus.Options.mode == FocusTimerMode.Pomodoro);
        if (questSearch)
        {
            questSearch.interactable = Ready;
            ((TMP_Text)questSearch.placeholder).text = string.IsNullOrEmpty(focus.Options.questTitle) ? "Search or choose quest" : focus.Options.questTitle;
        }
        if (questDropdown) questDropdown.interactable = Ready;
        if (!Ready) CloseQuests();
        // SetValueWithoutNotify first avoids clamping a previous mode's value through a listener.
        focusDuration.SetValueWithoutNotify(1);
        focusDuration.maxValue = focus.Options.mode == FocusTimerMode.Countdown ? 180 : 60;
        focusDuration.SetValueWithoutNotify(focus.Options.focusMinutes);
        restDuration.SetValueWithoutNotify(focus.Options.restMinutes);
        focusDuration.interactable = restDuration.interactable = Ready;
        SetDurationButton(focusDecrease, Ready && focus.Options.focusMinutes > 1);
        SetDurationButton(focusIncrease, Ready && focus.Options.focusMinutes < focusDuration.maxValue);
        SetDurationButton(restDecrease, Ready && focus.Options.restMinutes > 1);
        SetDurationButton(restIncrease, Ready && focus.Options.restMinutes < 60);
        focusDurationLabel.text = focus.Options.focusMinutes + " min";
        restDurationLabel.text = focus.Options.restMinutes + " min";
        if (questList) foreach (var button in questList.GetComponentsInChildren<Button>()) Style(button, Ready, button.name == (focus.Options.questId ?? "NoQuest"));
        if (questContext) questContext.text = "CURRENT QUEST\n" + (string.IsNullOrEmpty(focus.Options.questTitle) ? "Free focus" : focus.Options.questTitle);
        var day = GameDataManager.EnsureInstance().Tasks.TodayRewards;
        int minutes = Ready ? (focus.CountsUp ? 0 : focus.Options.focusMinutes) : (int)Math.Floor(Math.Min(focus.ElapsedSeconds / 60, TaskService.FocusSessionMinuteCap));
        int eligible = Math.Max(0, Math.Min(minutes, TaskService.FocusMinuteCap - day.focusMinutes));
        int tickets = Math.Max(0, Math.Min(minutes/25, TaskService.FocusTicketCap - day.focusTickets));
        bool settled = focus.LastResult != null && (focus.Phase == FocusPhase.Resting || focus.Phase == FocusPhase.Finished);
        const string heading = "Rewards";
        int experience = settled ? focus.LastResult.experience : eligible;
        int fish = settled ? focus.LastResult.fishCoins : eligible/5*2;
        int grantedTickets = settled ? focus.LastResult.tickets : tickets;
        if (fishReward && ticketReward)
        {
            rewards.text = experience.ToString();
            fishReward.text = fish.ToString();
            ticketReward.text = grantedTickets.ToString();
        }
        else if (rewards) rewards.text = (rewardTitle ? "" : heading + "\n\n") + $"EXP  +{experience}\n\nFish  +{fish}\n\nTickets  +{grantedTickets}";
        if (rewardAllowanceBar) rewardAllowanceBar.SetValueWithoutNotify(day.focusMinutes);
        if (ticketAllowanceBar) ticketAllowanceBar.SetValueWithoutNotify(day.focusTickets);
        if (rewardAllowanceLabel) rewardAllowanceLabel.text = $"Reward time today   {day.focusMinutes} / {TaskService.FocusMinuteCap} min";
        if (ticketAllowanceLabel) ticketAllowanceLabel.text = $"Boss Tickets today   {day.focusTickets} / {TaskService.FocusTicketCap}";
        if (allowance) allowance.text = (day.focusMinutes >= TaskService.FocusMinuteCap ? "Daily EXP / Fish limit reached. Timer still available."
                : focus.ElapsedSeconds >= TaskService.FocusSessionMinuteCap * 60 && focus.CountsUp ? (focus.Phase == FocusPhase.Focusing ? "Session reward limit reached. Timer continues." : "Session rewards limited to the first 3 hours.")
                : "Rewards: up to 3 hours per session / 6 hours per day.");
        // The authored scene intentionally unbinds the old sky message. Keep result feedback
        // in this serialized settings card instead of overriding the scene's sky composition.
        if (allowance && focus.Phase == FocusPhase.Saving)
            allowance.text = "Focus not saved\nYour result is waiting to be saved.\nUse Retry Save below.";
        else if (allowance && focus.Phase == FocusPhase.Finished && focus.LastResult != null)
            allowance.text = "Focus saved\n" + allowance.text;
        else if (allowance && focus.Phase == FocusPhase.Interrupted)
            allowance.text = "Session ended without rewards.\n" + allowance.text;
    }
    private static void Style(Button button, bool enabled, bool selected)
    {
        button.interactable = enabled;
        if (button.targetGraphic) button.targetGraphic.color = selected ? new Color32(113,227,171,255) : new Color32(225,240,250,255);
    }
    private void RefreshTime()
    {
        displayedSecond = FocusController.DisplaySecond(focus.DisplaySeconds, focus.CountsUp);
        timer.text = FocusController.FormatTime(focus.DisplaySeconds, focus.CountsUp);
        if (quickStart)
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if ((label.text.Contains("min Focus") && label.text.Contains("min Rest")) || label.text.StartsWith("Stopwatch •") || label.text.StartsWith("Countdown •"))
                    label.text = focus.CountsUp ? "Stopwatch • Up to 3h rewards" : focus.Options.mode == FocusTimerMode.Countdown ? $"Countdown • {focus.Options.focusMinutes} min" : $"{focus.Options.focusMinutes} min Focus / {focus.Options.restMinutes} min Rest";
        if (progress)
        {
            progress.maxValue = focus.CountsUp ? TaskService.FocusSessionMinuteCap * 60 : focus.Phase == FocusPhase.Resting ? focus.RestSeconds : focus.FocusSeconds;
            progress.SetValueWithoutNotify((float)(focus.CountsUp ? Math.Min(progress.maxValue, focus.ElapsedSeconds) : progress.maxValue - focus.RemainingSeconds));
        }
    }
}
