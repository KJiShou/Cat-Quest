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
    public TMP_FontAsset settingsFont;
    public Sprite settingsSprite;
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
        if (focus != null && displayedSecond != (int)Math.Ceiling(focus.RemainingSeconds)) { RefreshTime(); RefreshSummary(); }
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
        if (cancel) cancel.gameObject.SetActive(phase == FocusPhase.Focusing);
        if (endRest) endRest.gameObject.SetActive(phase == FocusPhase.Resting);
        if (retry) retry.gameObject.SetActive(phase == FocusPhase.Saving);
        RefreshTime();
        RefreshSummary();
    }
    private bool Ready => focus != null && (focus.Phase == FocusPhase.Ready || focus.Phase == FocusPhase.Finished || focus.Phase == FocusPhase.Interrupted);
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
        if (Ready && message) message.text = "";
        if (message) message.gameObject.SetActive(focus.Phase == FocusPhase.Saving && !string.IsNullOrEmpty(focus.Message));
        if (questSearch)
        {
            questSearch.interactable = Ready;
            ((TMP_Text)questSearch.placeholder).text = string.IsNullOrEmpty(focus.Options.questTitle) ? "Search or choose quest" : focus.Options.questTitle;
        }
        if (questDropdown) questDropdown.interactable = Ready;
        if (!Ready) CloseQuests();
        focusDuration.SetValueWithoutNotify(focus.Options.focusMinutes);
        restDuration.SetValueWithoutNotify(focus.Options.restMinutes);
        focusDuration.interactable = restDuration.interactable = Ready;
        SetDurationButton(focusDecrease, Ready && focus.Options.focusMinutes > 1);
        SetDurationButton(focusIncrease, Ready && focus.Options.focusMinutes < 60);
        SetDurationButton(restDecrease, Ready && focus.Options.restMinutes > 1);
        SetDurationButton(restIncrease, Ready && focus.Options.restMinutes < 60);
        focusDurationLabel.text = focus.Options.focusMinutes + " min";
        restDurationLabel.text = focus.Options.restMinutes + " min";
        if (questList) foreach (var button in questList.GetComponentsInChildren<Button>()) Style(button, Ready, button.name == (focus.Options.questId ?? "NoQuest"));
        if (questContext) questContext.text = "CURRENT QUEST\n" + (string.IsNullOrEmpty(focus.Options.questTitle) ? "Free focus" : focus.Options.questTitle);
        var day = GameDataManager.EnsureInstance().Tasks.TodayRewards;
        int minutes = Ready ? focus.Options.focusMinutes : (int)Math.Floor(focus.ElapsedSeconds / 60);
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
        if (allowance) allowance.text = $"TODAY'S FOCUS\n{day.focusMinutes} / 120 reward minutes\n{day.focusTickets} / 4 Boss Tickets\n\n1 EXP / minute\n2 Fish / 5 full minutes\n1 Ticket / 25 full minutes";
    }
    private static void Style(Button button, bool enabled, bool selected)
    {
        button.interactable = enabled;
        if (button.targetGraphic) button.targetGraphic.color = selected ? new Color32(113,227,171,255) : new Color32(225,240,250,255);
    }
    private void RefreshTime()
    {
        displayedSecond = (int)Math.Ceiling(focus.RemainingSeconds);
        timer.text = $"{displayedSecond / 60:00}:{displayedSecond % 60:00}";
        if (quickStart)
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (label.text.Contains("min Focus") && label.text.Contains("min Rest")) label.text = $"{focus.Options.focusMinutes} min Focus / {focus.Options.restMinutes} min Rest";
        if (progress)
        {
            progress.maxValue = focus.Phase == FocusPhase.Resting ? focus.RestSeconds : focus.FocusSeconds;
            progress.SetValueWithoutNotify((float)(progress.maxValue - focus.RemainingSeconds));
        }
    }
}
