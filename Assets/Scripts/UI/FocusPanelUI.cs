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
    public FocusCancelConfirmation cancelConfirmation;
    string confirmingSession;
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
    public GameObject allowanceNotice;
    public TMP_Text fishReward, ticketReward;
    public Slider rewardAllowanceBar, ticketAllowanceBar;
    public TMP_Text rewardAllowanceLabel, ticketAllowanceLabel;
    public TMP_Text ticketRule;
    public GameObject ticketProgressCard;
    public Slider ticketProgressBar;
    public TMP_Text ticketStepValue;
    public TMP_FontAsset settingsFont;
    public Sprite settingsSprite;
    public Toggle pomodoroMode, stopwatchMode, countdownMode;
    public GameObject focusDurationCard, restDurationCard;
    private int pomodoroMinutes = 25, countdownMinutes = 25;
    public FocusController Controller => focus;
    private FocusController focus;
    private int displayedSecond = -1;
    private string noticeSession, lastNotice;
    private double noticeUntil;
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
        if(cancelConfirmation)cancelConfirmation.Dismiss();
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
        if (noticeUntil > 0 && Time.realtimeSinceStartupAsDouble >= noticeUntil)
        {
            noticeUntil = 0;
            RefreshSummary();
        }
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
    private void EndEarly() => ConfirmSessionAction(false);
    private void Cancel() => ConfirmSessionAction(true);
    private void ConfirmSessionAction(bool cancelSession)
    {
        if (focus.Phase != FocusPhase.Focusing || !cancelConfirmation || cancelConfirmation.IsOpen) return;
        if (cancelSession != (focus.Options.mode == FocusTimerMode.Pomodoro)) return;
        string session = confirmingSession = focus.SessionId;
        cancelConfirmation.Show(cancelSession, () => {
            if (focus.Phase != FocusPhase.Focusing || focus.SessionId != session) return;
            if (cancelSession) focus.Cancel();
            else focus.EndEarly();
        }, cancelSession ? null : (Func<FocusCancelConfirmation.RewardPreview>)EndFocusEstimate);
    }
    private FocusCancelConfirmation.RewardPreview EndFocusEstimate()
    {
        var service = GameDataManager.EnsureInstance().Tasks;
        var estimate = service.PreviewFocusRewards(focus.ElapsedSeconds);
        int minutes = (int)(Math.Min(focus.ElapsedSeconds, service.FocusSessionMinuteLimit * 60d) / 60);
        int remaining = TaskService.DailyGoalTarget(DailyRewardGoal.Focus25Minutes) - service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes);
        return new FocusCancelConfirmation.RewardPreview(estimate, Math.Max(0, Math.Min(minutes, remaining)));
    }
    private void EndRest() { focus.EndRest(); }
    private void Retry() { focus.RetrySettlement(); }
    private void ReturnToDashboard() { navigation.ShowPage(MainMenuPage.Dashboard); }
    private void Refresh()
    {
        var phase = focus.Phase;
        if(cancelConfirmation&&cancelConfirmation.IsOpen&&(phase!=FocusPhase.Focusing||focus.SessionId!=confirmingSession))cancelConfirmation.Dismiss();
        status.text = phase == FocusPhase.Focusing ? "Focus Training" : phase == FocusPhase.Resting ? "Rest" : "Focus Training";
        if (message) message.text = focus.Message;
        bool ready = phase == FocusPhase.Ready || phase == FocusPhase.Finished || phase == FocusPhase.Interrupted;
        start.gameObject.SetActive(quickStart || ready);
        start.GetComponentInChildren<TMP_Text>(true).text = quickStart && !ready ? "Open Focus" : "Start Focus";
        if (endEarly) endEarly.gameObject.SetActive(phase == FocusPhase.Focusing && focus.Options.mode != FocusTimerMode.Pomodoro);
        if (cancel) cancel.gameObject.SetActive(phase == FocusPhase.Focusing && focus.Options.mode == FocusTimerMode.Pomodoro);
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
        AddTag(ui, null);
        var service = GameDataManager.EnsureInstance().Tasks;
        var catalog = service.TagCatalog;
        if (Ready)
        {
            focus.Options.tagIds.RemoveAll(id => !catalog.Any(t => t.id == id));
            focus.Options.tagNames = catalog.Where(t => focus.Options.tagIds.Contains(t.id)).Select(t => t.name).ToList();
        }
        string query = questSearch ? questSearch.text.Trim() : "";
        int matches = 0;
        foreach (var tag in catalog.Where(t => query.Length == 0 || t.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
        { AddTag(ui, tag); matches++; }
        if (matches == 0) { var empty = ui.Text(questList, query.Length == 0 ? "Create tags in Tasks" : "No matching tags", 16); TaskUIBuilder.Height(empty.rectTransform, 40); }
        var scroll = questList.GetComponentInParent<ScrollRect>(); if (scroll) scroll.verticalNormalizedPosition = 1;
        RefreshSummary();
    }
    private void AddTag(TaskUIBuilder ui, QuestTag tag)
    {
        var button = ui.Button(questList, tag?.id ?? "NoTags", tag?.name ?? "No tags");
        TaskUIBuilder.Height((RectTransform)button.transform, 48);
        var label = button.GetComponentInChildren<TMP_Text>(); label.fontSize = 17; label.enableAutoSizing = true; label.fontSizeMin = 13; label.fontSizeMax = 17;
        button.onClick.AddListener(() => {
            if (!Ready) return;
            var options = focus.Options.Copy();
            options.questId = options.questTitle = options.occurrenceDay = null;
            if (tag == null) options.tagIds.Clear();
            else { options.tagIds.Clear(); options.tagIds.Add(tag.id); }
            var catalog = GameDataManager.EnsureInstance().Tasks.TagCatalog;
            options.tagIds.RemoveAll(id => !catalog.Any(t => t.id == id));
            options.tagNames = catalog.Where(t => options.tagIds.Contains(t.id)).Select(t => t.name).ToList();
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
            var placeholder = (TMP_Text)questSearch.placeholder;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
            ((TMP_Text)questSearch.placeholder).text = focus.Options.tagNames.Count == 0 ? "Search or choose a tag" : string.Join(", ", focus.Options.tagNames);
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
        var catalog = GameDataManager.EnsureInstance().Tasks.TagCatalog;
        if (questList) foreach (var button in questList.GetComponentsInChildren<Button>())
        {
            bool selected = focus.Options.tagIds.Contains(button.name) || (button.name == "NoTags" && focus.Options.tagIds.Count == 0);
            Style(button, Ready, selected);
            var tag = catalog.FirstOrDefault(t => t.id == button.name);
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = (selected ? "✓ " : "") + (tag?.name ?? "No tags");
            if (tag != null)
            {
                button.targetGraphic.color = TagColors.Background(tag.colorHex);
                label.color = TagColors.Foreground(tag.colorHex);
            }
        }
        var selectedTag = catalog.FirstOrDefault(t => focus.Options.tagIds.Contains(t.id));
        if (questSearch && questSearch.placeholder is TMP_Text tagPlaceholder)
        {
            tagPlaceholder.color = selectedTag != null ? TagColors.Foreground(selectedTag.colorHex) : TaskUIBuilder.Ink;
            if (questSearch.targetGraphic) questSearch.targetGraphic.color = selectedTag != null ? TagColors.Background(selectedTag.colorHex) : Color.white;
            questSearch.textComponent.color = tagPlaceholder.color;
        }
        if (questContext) questContext.text = "TAGS\n" + (focus.Options.tagNames.Count == 0 ? "Free focus" : string.Join(", ", focus.Options.tagNames));
        var service = GameDataManager.EnsureInstance().Tasks;
        var day = service.TodayRewards;
        int dailyLimit = service.FocusDailyMinuteLimit;
        int sessionLimit = service.FocusSessionMinuteLimit;
        bool settled = focus.LastResult != null && (focus.Phase == FocusPhase.Resting || focus.Phase == FocusPhase.Finished);
        var previewReward = service.PreviewFocusRewards(Ready ? (focus.CountsUp ? 0 : focus.FocusSeconds) : focus.ElapsedSeconds);
        const string heading = "Rewards";
        int experience = settled ? focus.LastResult.experience : previewReward.experience;
        int fish = settled ? focus.LastResult.fishCoins : previewReward.fishCoins;
        int grantedTickets = settled ? focus.LastResult.tickets : previewReward.tickets;
        if (fishReward && ticketReward)
        {
            rewards.text = experience.ToString();
            fishReward.text = fish.ToString();
            ticketReward.text = grantedTickets.ToString();
        }
        else if (rewards) rewards.text = (rewardTitle ? "" : heading + "\n\n") + $"EXP  +{experience}\n\nFish  +{fish}\n\nTickets  +{grantedTickets}";
        if (rewardAllowanceBar) { rewardAllowanceBar.maxValue = dailyLimit; rewardAllowanceBar.SetValueWithoutNotify(day.focusMinutes); }
        if (ticketAllowanceBar) { ticketAllowanceBar.maxValue = TaskService.FocusTicketCap; ticketAllowanceBar.SetValueWithoutNotify(day.focusTickets); }
        if (rewardAllowanceLabel) rewardAllowanceLabel.text = $"Reward time today   {day.focusMinutes} / {dailyLimit} min";
        if (ticketAllowanceLabel) ticketAllowanceLabel.text = $"Boss Tickets today   {day.focusTickets} / {TaskService.FocusTicketCap}";
        RefreshTicketProgress(day, previewReward.tickets);
        string notice = (day.focusMinutes >= dailyLimit ? "Daily EXP / Fish limit reached. Timer still available."
                : focus.ElapsedSeconds >= sessionLimit * 60d && focus.CountsUp ? (focus.Phase == FocusPhase.Focusing ? "Session reward limit reached. Timer continues." : $"Session rewards limited to {sessionLimit} min.")
                : "");
        // The authored scene intentionally unbinds the old sky message. Keep result feedback
        // in this serialized settings card instead of overriding the scene's sky composition.
        bool persistentNotice = !string.IsNullOrEmpty(focus.ErrorMessage);
        if (persistentNotice)
            notice = (focus.Phase == FocusPhase.Saving ? "Not saved. " : "Start failed. ")
                + FocusErrorSummary(focus.ErrorMessage)
                + (focus.Phase == FocusPhase.Saving ? " Retry Save." : " Try Start Focus again.");
        else if (focus.Phase == FocusPhase.Saving)
            notice = "Focus not saved. Retry to save your result.";
        else if (focus.Phase == FocusPhase.Finished && focus.LastResult != null)
            notice = "Focus saved." + (string.IsNullOrEmpty(notice) ? "" : "\n" + notice);
        else if (focus.Phase == FocusPhase.Interrupted)
            notice = "Session ended without rewards.";

        if (notice != lastNotice || noticeSession != focus.SessionId)
        {
            lastNotice = notice;
            noticeSession = focus.SessionId;
            noticeUntil = persistentNotice || string.IsNullOrEmpty(notice) ? 0 : Time.realtimeSinceStartupAsDouble + 4;
        }
        if (allowance) allowance.text = persistentNotice || Time.realtimeSinceStartupAsDouble < noticeUntil ? notice : "";
        if(allowanceNotice)allowanceNotice.SetActive(allowance&&!string.IsNullOrEmpty(allowance.text));
    }
    private void RefreshTicketProgress(RewardDay day, int estimatedTickets)
    {
        const double ticketSeconds = 25 * 60;
        bool focusing = focus.Phase == FocusPhase.Focusing;
        int remaining = TaskService.FocusTicketCap - day.focusTickets;
        double nextBoundary = (Math.Floor(focus.ElapsedSeconds / ticketSeconds) + 1) * ticketSeconds;
        bool endsBeforeNext = focusing && !focus.CountsUp && focus.FocusSeconds < nextBoundary;
        bool visible = (focus.Phase == FocusPhase.Ready || focusing) && remaining > 0 && estimatedTickets < remaining
            && (!endsBeforeNext || estimatedTickets == 0);
        if (ticketProgressCard) ticketProgressCard.SetActive(visible);
        bool shortPlan = !focusing && !focus.CountsUp && focus.FocusSeconds < ticketSeconds;
        double elapsed = focusing ? focus.ElapsedSeconds % ticketSeconds : 0;
        double seconds = shortPlan ? focus.FocusSeconds : endsBeforeNext ? focus.RemainingSeconds : ticketSeconds - elapsed;
        if (ticketRule) ticketRule.text = visible ? FocusController.FormatTime(seconds, false) : "";
        if (ticketStepValue) ticketStepValue.text = visible ? (shortPlan || endsBeforeNext ? "0" : "+1") : "";
        if (ticketProgressBar) {
            ticketProgressBar.maxValue = 1;
            ticketProgressBar.SetValueWithoutNotify(visible ? (float)(elapsed / ticketSeconds) : 0);
        }
    }
    static string FocusErrorSummary(string error)
    {
        // Keep the actual reason, but omit the long local path and duplicate save
        // heading in this compact card. The controller retains the original error.
        const string savePrefix = "Could not save your changes. ";
        if (error.StartsWith(savePrefix, StringComparison.Ordinal)) error = error.Substring(savePrefix.Length);
        var path = GameDataManager.Instance ? GameDataManager.Instance.SavePath : null;
        if (!string.IsNullOrEmpty(path)) error = error.Replace(path + ".tmp", "save file").Replace(path, "save file");
        return error;
    }
    private static void Style(Button button, bool enabled, bool selected)
    {
        button.interactable = enabled;
        if (button.targetGraphic) button.targetGraphic.color = selected ? new Color32(189,224,254,255) : new Color32(231,239,250,255);
    }
    private void RefreshTime()
    {
        displayedSecond = FocusController.DisplaySecond(focus.DisplaySeconds, focus.CountsUp);
        timer.text = FocusController.FormatTime(focus.DisplaySeconds, focus.CountsUp);
        if (quickStart)
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if ((label.text.Contains("min Focus") && label.text.Contains("min Rest")) || label.text.StartsWith("Stopwatch •") || label.text.StartsWith("Countdown •"))
                    label.text = focus.CountsUp ? $"Stopwatch • Up to {GameDataManager.EnsureInstance().Tasks.FocusSessionMinuteLimit} min rewards" : focus.Options.mode == FocusTimerMode.Countdown ? $"Countdown • {focus.Options.focusMinutes} min" : $"{focus.Options.focusMinutes} min Focus / {focus.Options.restMinutes} min Rest";
        if (progress)
        {
            progress.maxValue = focus.CountsUp ? GameDataManager.EnsureInstance().Tasks.FocusSessionMinuteLimit * 60f : focus.Phase == FocusPhase.Resting ? focus.RestSeconds : focus.FocusSeconds;
            progress.SetValueWithoutNotify((float)(focus.CountsUp ? Math.Min(progress.maxValue, focus.ElapsedSeconds) : progress.maxValue - focus.RemainingSeconds));
        }
    }
}
