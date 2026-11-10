using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Observes committed domain state. Never completes tasks, grants rewards or confirms battle entry.
[DefaultExecutionOrder(-300)]
public sealed class TutorialController : MonoBehaviour
{
    public MainMenuNavigation navigation;
    public TaskSystemUI tasks;
    public FocusPanelUI focusPage;
    public BossPageUI bossPage;
    public SettingsPanelUI settings;
    public LevelUpCelebrationUI celebration;
    public RewardFlightUI rewards;
    public GameObject panel, helpPanel, helpBackdrop;
    public TaskDialogKeyboard helpKeyboard;
    public Button helpBackdropButton;
    public RectTransform card, highlight;
    public StoryDialogueView dialogue;
    public CharacterHomePreview mentor;
    public Button next, later, skip, helpClose; // later is retained only for serialized compatibility; hidden.
    public Button back;
    public TutorialSpotlight spotlight;
    public Button typingShield;
    public RectTransform pointer;
    LeaderboardPanelUI leaderboard;
    RectTransform privacyTarget;
    EquipmentPageUI equipmentPage;
    Button observedButton;
    string queuedAction;
    bool advancingFromTarget, ownsAccount, ownsSettings;
    public Button[] topics;
    public TMP_Text nextLabel;
    public GameObject[] blockers;
    TaskService service;
    FocusController focus;
    Coroutine typing;
    TutorialProgress progress;
    PlayerData snapshot;
    bool dirty = true;
    string shown, manualTopic;
    int manualIndex, settingsStep;
    bool snoozed, focused = true, changing, ownsStagePicker;
    float pollAt, noticeUntil;
    RectTransform target;
    TaskContextMenu taskMenuPreview;
    readonly Vector3[] corners = new Vector3[4];
    // Keep persisted step identities; only their teaching order changes.
    static readonly int[] TaskPageOrder = { 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 0, 1, 2, 3, 4, 8 };
    int TaskPagePosition => Array.IndexOf(TaskPageOrder, Mathf.Clamp(progress.taskPageStep, 0, 26));
    public bool IsTyping => typing != null;
    public string CurrentTopic => shown;
    public RectTransform CurrentTarget => target;

    void Start()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        focus = FocusRunner.EnsureInstance().Controller;
        if (!service.InitializeTutorial()) { enabled = false; return; }
        progress = service.Tutorial; snapshot = service.Snapshot; service.Changed += DataChanged;
        leaderboard = navigation.GetComponentInParent<Canvas>().GetComponentInChildren<LeaderboardPanelUI>(true);
        equipmentPage = navigation.pages[(int)MainMenuPage.CatHome].GetComponentInChildren<EquipmentPageUI>(true);
        privacyTarget = settings.accountPage.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Privacy");
        typingShield.onClick.AddListener(CompleteTyping);
        next.onClick.AddListener(Continue); skip.onClick.AddListener(Skip);
        if(back) back.onClick.AddListener(GoBack);
        if(later) later.gameObject.SetActive(false);
        helpClose.onClick.AddListener(CloseHelp);
        if(helpBackdropButton) helpBackdropButton.onClick.AddListener(CloseHelp);
        for(int i = 0; i < topics.Length; i++) { int index = i; topics[i].onClick.AddListener(() => OpenTopic(index)); }
        navigation.PageChanged += PageChanged; navigation.AccessDenied += LockedNotice;
        panel.SetActive(false); helpPanel.SetActive(false); highlight.gameObject.SetActive(false);
    }
    void DataChanged() { dirty = true; }
    void OnDestroy()
    {
        if(typingShield) typingShield.onClick.RemoveListener(CompleteTyping);
        Observe(null);
        if(service != null) service.Changed -= DataChanged;
        if(navigation) { navigation.PageChanged -= PageChanged; navigation.AccessDenied -= LockedNotice; }
        next.onClick.RemoveListener(Continue); skip.onClick.RemoveListener(Skip);
        if(back) back.onClick.RemoveListener(GoBack);
        helpClose.onClick.RemoveListener(CloseHelp);
        if(helpBackdropButton) helpBackdropButton.onClick.RemoveListener(CloseHelp);
    }
    void OnDisable() { Hide(); }
    void OnApplicationFocus(bool value) { focused = value; if(!value) Hide(); }
    void PageChanged(MainMenuPage page) {
        Hide(); pollAt = 0;
        // A temporary snooze of the Focus/help explanation must not suppress a newly visited guide.
        // Explicit Skip remains persisted in progress.dismissed.
        if(focus != null && focus.Phase == FocusPhase.Focusing
            && (page == MainMenuPage.Boss || page == MainMenuPage.Leaderboard)) snoozed = false;
    }
    bool Save(Action<TutorialProgress> edit)
    {
        var candidate = service.Tutorial; edit(candidate);
        if(!service.SaveTutorial(candidate)) { Show("error", service.LastError + " Your tasks are safe. Try again.", null, "Retry"); return false; }
        progress = service.Tutorial; return true;
    }
    void Read(string topic) { Save(p => { if(!p.read.Contains(topic)) p.read.Add(topic); }); }
    bool ReadAlready(string topic) => progress.read.Contains(topic);
    bool Busy()
    {
        return !focused || (settings && settings.gameObject.activeInHierarchy && !ownsAccount && !ownsSettings)
            || (celebration && (celebration.IsShowing || celebration.PendingCount > 0))
            || (rewards && rewards.IsAnimating) || (tasks && tasks.gameObject.activeInHierarchy && tasks.TutorialBlocked)
            || blockers.Any(b => b && b.activeInHierarchy && !(ownsStagePicker && b == bossPage.stagePicker))
            || snapshot.battles.Any(b => b.outcome == BattleOutcome.Running);
    }
    void Update()
    {
        if(queuedAction != null) {
            string key = queuedAction; queuedAction = null;
            if(key == shown && !IsTyping) {
                if(key == "boss-step-2") ownsStagePicker = bossPage.stagePicker.activeSelf;
                advancingFromTarget = true; Continue(); advancingFromTarget = false;
            }
        }
        // Task quick-add replaces its button with an input. Refresh the allowed target before the input shield handles keyboard focus.
        if(panel && panel.activeSelf && shown == "task-page-6") { target = progress != null && progress.taskStep == 3 ? tasks.TutorialTaskTarget(progress.taskId) : tasks.TutorialAddTarget; Position(false); }
        if(service == null || Time.unscaledTime < pollAt) return;
        pollAt = Time.unscaledTime + .25f;
        if(dirty) { snapshot = service.Snapshot; progress = snapshot.tutorial; dirty = false; }
        if(focus.Phase == FocusPhase.Focusing && !ReadAlready("focus") && !progress.dismissed && navigation.CurrentPage == MainMenuPage.Focus)
        { Read("focus"); Hide(); Show("focus-running", "Your training has started. Switching pages or apps will keep the timer running. Time to focus!", null, "Got it"); noticeUntil = Time.unscaledTime + 6; return; }
        if(Busy()) { Hide(); return; }
        if(helpPanel.activeSelf) return;
        if(manualTopic != null) { if(!panel.activeSelf) { if(manualTopic == "settings") ShowSettings(); else if(manualTopic == "equipment") ShowEquipment(); else if(manualIndex == 1) ShowFocus(); else if(manualIndex == 3) ShowBoss(); else if(manualIndex == 4) ShowLeaderboard(); else OpenTopic(manualIndex); } return; }
        if(shown == "focus-running") return;
        if(shown is "locked" or "error") return;
        if(focus.Phase is FocusPhase.Focusing or FocusPhase.Saving or FocusPhase.Resting
            && navigation.CurrentPage != MainMenuPage.Boss && navigation.CurrentPage != MainMenuPage.Leaderboard) { Hide(); return; }
        if(snoozed || progress.dismissed) { Hide(); return; }
        if(shown != null && !shown.StartsWith("task-") && !shown.StartsWith("focus-step-") && !shown.StartsWith("boss-step-") && !shown.StartsWith("leaderboard-step-")) return;
        var data = snapshot;
        if(progress.taskStep < 4)
        {
            if(progress.taskStep == 0) { Show("task-0", "Welcome, {playerName}. Let us grow stronger, one real task at a time. I will help you write your first task.", null, "Let's begin", true); return; }
            if(navigation.CurrentPage != MainMenuPage.Tasks) { Show("task-open", "Open Tasks to write down something you want to do.", navigation.buttons[(int)MainMenuPage.Tasks].transform as RectTransform, "Open Tasks"); return; }
            if(progress.taskStep == 1 && !Save(p => p.taskStep = 2)) return;
            ShowTasks(); return;
        }
        if(!ReadAlready("reward") && data.history.Any(h => !h.revoked))
        {
            var receipt = data.history.Last(h => !h.revoked);
            Show("reward", receipt.experience == 0 && receipt.fishCoins == 0 ? "Task complete and recorded. Today's task reward allowance is used up. Your work still counts." : $"Well done. This task earned {receipt.experience} EXP and {receipt.fishCoins} Fish Coins. EXP grows your cat. Fish Coins can upgrade equipment.", null, "Got it"); return;
        }
        int level = service.RewardLevel;
        if((progress.announcedLevel < 2 && level >= 2) || (progress.announcedLevel < 5 && level >= 5))
        {
            Show("unlock", progress.announcedLevel < 2 && level >= 5 ? "Your progress has opened Focus, Boss battles and the Leaderboard! Let us begin with Focus." : level >= 5 ? "You have reached Lv.5! Boss battles and the Leaderboard are now open. Explore either when you are ready." : "You have reached Lv.2! Focus is now open. I can show you how to start a training session.", null, "Take a look"); return;
        }
        if(navigation.CurrentPage == MainMenuPage.Focus && !ReadAlready("focus")) { ShowFocus(); return; }
        if(navigation.CurrentPage == MainMenuPage.Boss && !ReadAlready("boss")) { ShowBoss(); return; }
        if(navigation.CurrentPage == MainMenuPage.Leaderboard && !ReadAlready("leaderboard")) { ShowLeaderboard(); return; }
        bool claimed = data.rewardDays.Any(d => d.questsClaimed || d.focusClaimed);
        if(!ReadAlready("equipment") && claimed) { ShowEquipment(); return; }
        if(!ReadAlready("daily") && navigation.CurrentPage == MainMenuPage.Dashboard && ((service.DailyGoalProgress(service.CurrentTaskGoal) >= TaskService.DailyGoalTarget(service.CurrentTaskGoal) && !service.DailyGoalClaimed(service.CurrentTaskGoal)) || (service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) >= 25 && !service.DailyGoalClaimed(DailyRewardGoal.Focus25Minutes))))
        { Show("daily", "A Daily Reward is ready. Use Claim on the completed goal to collect it. Your first claim also opens equipment management.", null, "Got it"); return; }
        Hide();
    }
    void ShowTasks()
    {
        int step = Mathf.Clamp(progress.taskPageStep, 0, 26);
        // An unfinished older guide also gets to useful work before the tab tour.
        // This changes explanation progress only; drafts and saved tasks stay intact.
        if(progress.taskStep < 3 && step != 5 && step != 6) {
            if(!Save(p => p.taskPageStep = 5)) return;
            step = 5;
        }
        if(step == 5 && !tasks.TutorialAddTarget) {
            if(tasks.calendar) tasks.calendar.SetMode(TaskDisplayMode.List);
            tasks.SelectTab(TaskTab.Today);
        }

        if(step >= 9) { ShowTaskMenuGuide(step); return; }
        RectTransform anchor; string text;
        switch(step)
        {
            case 0: anchor = tasks.tabButtons[0].transform as RectTransform;
                text = "Today brings together the tasks scheduled for today. Click Today to see the tasks scheduled for today."; break;
            case 1: anchor = tasks.tabButtons[1].transform as RectTransform;
                text = "Daily Tasks repeat. Complete each day's task only after doing the work. An unfinished daily occurrence becomes Missed at the daily reset. It does not earn completion rewards. Click Daily Tasks to open the list."; break;
            case 2: anchor = tasks.tabButtons[2].transform as RectTransform;
                text = "Todo is for one-off work. An overdue Todo stays active until you complete or delete it, so unfinished work does not disappear at midnight. Click Todo to open it."; break;
            case 3: anchor = tasks.tabButtons[3].transform as RectTransform;
                text = "Tags help organize related tasks, such as study or exercise. You can also use these tags to label your Focus sessions once Focus opens at Lv.2. Click Tags to open the list."; break;
            case 4: anchor = tasks.search.transform as RectTransform;
                text = "Use Search to find a task by name. The filter button beside it can narrow the list further. If a task is missing from your view, check the tab, search and filters."; break;
            case 5: anchor = tasks.TutorialAddTarget;
                text = progress.taskStep == 3
                    ? "The + button creates a real task. Your task is already saved, so you can continue reviewing the guide without creating another one."
                    : tasks.QuickAddInput ? "The task name field is already open. Continue to finish writing and save your task. Saving does not mark the work complete."
                    : "The + button creates a real task. Choose something small you actually want to do. Click the highlighted + button to open the task name field. This does not save or complete any work."; break;
            case 6:
                anchor = progress.taskStep == 3 ? tasks.TutorialTaskTarget(progress.taskId) : tasks.TutorialAddTarget;
                text = progress.taskStep == 3 ? "Your task has been saved. No completion rewards have been granted. Press Continue when you are ready to learn how to mark work complete." : "Write your real task in this field, then press Enter to save it. Continue becomes available after the save succeeds. You can always Skip the guide."; break;
            case 7:
                anchor = tasks.TutorialCompletionTarget(progress.taskId);
                text = anchor ? "This checkbox marks the task complete. Use it only after you have actually done the work. The tutorial will not tick it for you. Real completion earns rewards within the daily allowance." : "Your saved task is outside this view or was removed. Find it using its tab, Search or filters after the guide. Only mark a task complete after doing the work."; break;
            default: anchor = tasks.tabButtons[4].transform as RectTransform;
                text = "History keeps your completed work. Even when the reward allowance is used up, valid completion is still recorded. Click History to see the record. Focus opens at Lv.2."; break;
        }
        Show("task-page-" + step + (step == 6 && progress.taskStep == 3 ? "-saved" : ""), text, anchor, "Continue");
        next.interactable = !RequiresTargetClick() && (step != 6 || progress.taskStep == 3);
    }
    void ShowTaskMenuGuide(int step)
    {
        if(!taskMenuPreview) {
            var template = Resources.Load<TaskContextMenu>("TaskContextMenu");
            if(template) {
                taskMenuPreview = Instantiate(template, transform);
                var task = snapshot.tasks.FirstOrDefault(t => t.id == progress.taskId)
                    ?? new QuestTask { title = "Example task" };
                taskMenuPreview.Show(task, service.TagCatalog, service.LocalNow, true,
                    new Vector2(Screen.width * .08f, Screen.height * .88f), null, null);
                taskMenuPreview.transform.SetAsFirstSibling();
            }
        }
        var menu = taskMenuPreview;
        var section = (step >= 12 && step <= 15) || step == 26 ? TaskContextAction.Difficulty
            : step == 17 ? TaskContextAction.Tags
            : step >= 19 && step <= 23 ? TaskContextAction.ChooseDate : TaskContextAction.Edit;
        if(menu && shown != "task-page-" + step) menu.ShowTutorialPanel(section);
        RectTransform anchor = menu ? menu.panel : null;
        string text;
        switch(step) {
            case 9:
                text = "Right-click an active task to open this menu. With a task focused, Shift plus F10 or the Menu key also works. This is a safe preview: Continue explains each button without changing your task."; break;
            case 10: anchor = menu ? menu.edit.transform as RectTransform : null;
                text = "Edit task opens the full editor for the name, description, subtasks and repeat settings. Save keeps your changes; Cancel leaves the task unchanged. Editing does not complete the work."; break;
            case 11: anchor = menu ? menu.difficulty.transform as RectTransform : null;
                text = "Change difficulty opens three choices. Choose the effort that fits your real task. The change is only saved when you press Save."; break;
            case 12: anchor = menu ? menu.difficultyOptions[0].transform as RectTransform : null;
                text = "Easy is for a small, straightforward task. Selecting it changes the draft difficulty. It does not save or grant a reward yet."; break;
            case 13: anchor = menu ? menu.difficultyOptions[1].transform as RectTransform : null;
                text = "Medium is for a task that needs a moderate amount of effort. Difficulty is your choice; select the level that fits the work."; break;
            case 14: anchor = menu ? menu.difficultyOptions[2].transform as RectTransform : null;
                text = "Hard is for more demanding work. Difficulty affects completion rewards within your daily allowance. Selecting Hard does not complete the task."; break;
            case 15: anchor = menu ? menu.save.transform as RectTransform : null;
                text = "Save applies the selected difficulty to this task. If saving fails, your choices stay open so you can retry. Editing does not grant completion rewards."; break;
            case 16: anchor = menu ? menu.tags.transform as RectTransform : null;
                text = "Add or remove tags opens your tag collection. Tags organize your tasks and help you find related work."; break;
            case 17: anchor = menu ? menu.tagsPanel.transform as RectTransform : null;
                text = "Select a tag to add it, and select it again to remove it. You can choose several tags. Save applies your choices. If the list is empty, create tags in Settings first."; break;
            case 18: anchor = menu ? menu.schedule.transform as RectTransform : null;
                text = "Schedule opens the date controls. Choose a shortcut or a calendar day, then Save schedule to apply it. For repeating tasks this changes the repeat start date."; break;
            case 19: anchor = menu ? menu.today.transform as RectTransform : null;
                text = "Today selects today's date. It is a shortcut for scheduling the task today. You still need Save schedule to keep the change."; break;
            case 20: anchor = menu ? menu.tomorrow.transform as RectTransform : null;
                text = "Tomorrow selects the next day's date. It does not mark the task complete or move any earned rewards."; break;
            case 21: anchor = menu ? menu.datePicker.calendar.transform as RectTransform : null;
                text = "The left and right arrows move between months. Press a numbered day to choose that date. Your selected date is a draft until you press Save schedule."; break;
            case 22: anchor = menu ? menu.clearDate.transform as RectTransform : null;
                text = "Clear date removes the schedule when you save. For a repeating task, saving also stops repetition. Read that label before saving. Existing completed history stays intact."; break;
            case 23: anchor = menu ? menu.save.transform as RectTransform : null;
                text = "Save schedule applies the chosen date, or clears it if you selected No date. Other task details and current subtask progress are preserved."; break;
            case 24: anchor = menu ? menu.complete.transform as RectTransform : null;
                text = "Complete task records finished work, just like the checkbox. Use it only after doing the task. It is disabled when completion is unavailable, including future repeating-task previews. This guide will not press it."; break;
            case 25: anchor = menu ? menu.delete.transform as RectTransform : null;
                text = "Delete task asks you to confirm before removing it. Deleting a repeating task also stops future repeats. Completed history and earned rewards remain. You can cancel the confirmation."; break;
            default: anchor = menu ? menu.back.transform as RectTransform : null;
                text = "Cancel returns from a submenu without saving its choices. Escape, clicking outside, or leaving Tasks closes the menu and discards unsaved choices. Continue to explore the task tabs."; break;
        }
        Show("task-page-" + step, text, anchor, "Continue");
    }
    void ShowFocus()
    {
        if(!service.CanAccess(ProgressionFeature.Focus)) { Show("focus-locked", "Focus unlocks at Lv.2. Complete real tasks to grow your cat. Once open, I will walk you through the timer, one step at a time.", null, "Continue"); return; }
        int step = Mathf.Clamp(progress.focusStep, 0, 6);
        RectTransform anchor;
        string text;
        switch(step)
        {
            case 0:
                anchor = focusPage.pomodoroMode.transform as RectTransform;
                text = "Pomodoro helps you work on one thing at a time, with a planned break afterward. The default is 25 minutes of focus followed by 5 minutes of Rest. Press Continue when you are ready."; break;
            case 1:
                anchor = focusPage.pomodoroMode.transform.parent as RectTransform;
                text = "Choose the timer that suits your work. Pomodoro pairs focus with a break. Countdown works toward a set duration. Stopwatch counts up. You can choose a mode here, then press Continue."; break;
            case 2:
                anchor = focusPage.focusDurationCard.activeInHierarchy ? focusPage.focusDurationCard.transform as RectTransform : focusPage.timer.transform as RectTransform;
                text = focusPage.focusDurationCard.activeInHierarchy ? "Set how long you want to focus. Use the time controls to choose a realistic work period, then press Continue." : "Stopwatch counts up, so it does not need a target duration. Press Continue to see what comes next."; break;
            case 3:
                anchor = focusPage.restDurationCard.activeInHierarchy ? focusPage.restDurationCard.transform as RectTransform : focusPage.timer.transform as RectTransform;
                text = focusPage.restDurationCard.activeInHierarchy ? "Set your Rest time here. In Pomodoro, Rest starts after your completed Focus session is saved. Rest gives no EXP, Fish Coins or Tickets." : "This mode does not use the Pomodoro break settings. Rest itself gives no EXP, Fish Coins or Tickets."; break;
            case 4:
                anchor = focusPage.questDropdown.transform.parent as RectTransform;
                text = "Use tags to label what you are working on and review your Focus time later. Tags are optional. Focus never marks a task complete for you. Press Continue when you are ready."; break;
            case 5:
                anchor = focusPage.start.transform as RectTransform;
                text = "Each session earns one Ticket per full 25 minutes, within the daily limit. Short sessions do not combine for Tickets. End Early saves earned rewards and adds to your daily Focus goal. Pomodoro Cancel and interruptions give no rewards."; break;
            default:
                anchor = focusPage.start.transform as RectTransform;
                text = "You are ready. Click Start Focus in the highlighted area to begin. Switching pages or apps will keep it running. Skip this guide if you are not ready to start."; break;
        }
        Show("focus-step-" + step, text, anchor, "Continue");
    }
    void ShowEquipment()
    {
        if(!service.EquipmentUnlocked) { Show("equipment-locked", "Claim a completed Daily Reward goal on the Dashboard to unlock equipment management. Equipment comes from Boss mastery, not from opening this guide. Keep completing real tasks and Focus sessions to grow your cat.", null, "Continue"); return; }
        if(navigation.CurrentPage != MainMenuPage.CatHome) {
            Show("equipment-open", "Equipment management is open! Cat Home is where you review, equip and upgrade your gear. Press Continue to visit it. I will explain each part without spending your Fish Coins or changing your equipment.", navigation.buttons[(int)MainMenuPage.CatHome].transform as RectTransform, "Continue"); return;
        }
        int step = Mathf.Clamp(progress.equipmentStep, 0, 6);
        RectTransform anchor; string text;
        switch(step) {
            case 0: anchor = equipmentPage.TutorialBackpack;
                text = "This Backpack holds your collected equipment. An empty Backpack is normal at first. Each normal Boss has fixed mastery rewards: 5 wins unlock the Weapon, 10 wins the Body Armour, and 15 wins the Head Accessory. Claim each earned piece in the stage selector."; break;
            case 1: anchor = equipmentPage.TutorialSlots;
                text = "Your cat has three equipment slots. A Weapon improves ATK, Body Armour improves DEF, and a Head Accessory improves attack speed. You can wear one item in each slot. Empty slots mean you have not equipped an item there yet."; break;
            case 2: anchor = equipmentPage.TutorialFilters;
                text = "Use the Backpack categories to view all equipment or only Head, Weapon or Body items. A category can be empty even when you own other gear. Return to All to see everything you have collected."; break;
            case 3: anchor = equipmentPage.TutorialBackpack;
                text = "After this guide, select an owned item to inspect its stats and set information. Equip puts it in its matching slot, replacing the item worn there. Unequip returns it to the Backpack; it does not delete the item."; break;
            case 4: anchor = equipmentPage.TutorialBackpack;
                text = "An item's details show its current effect, next-level effect and upgrade cost in Fish Coins. Upgrading improves that item, but its level cannot exceed your Cat Level. Earn EXP from real work to raise that limit, and check the cost before confirming."; break;
            case 5: anchor = equipmentPage.TutorialAutoSelect;
                text = "Auto Select compares combinations of your owned gear and equips the one with the highest calculated Power. It does not buy or upgrade anything. Use it after the guide for a quick loadout, or choose your own items individually."; break;
            default: anchor = equipmentPage.TutorialSlots;
                text = "Equipped pieces from the same Boss set contribute to its set bonuses. Review the set information when choosing gear. You are ready to explore! If your Backpack is empty, keep growing your cat and claim earned Boss mastery rewards when available."; break;
        }
        Show("equipment-step-" + step, text, anchor, "Continue");
    }
    void ShowBoss()
    {
        if(!service.CanAccess(ProgressionFeature.Boss)) { Show("boss-locked", "Boss battles unlock at Lv.5. Focus can earn tickets. Confirming entry spends one ticket. Later bosses also require previous victories and higher levels.", null, "Continue"); return; }
        int step = Mathf.Clamp(progress.bossStep, 0, 6);
        if(step >= 3 && step <= 5 && !bossPage.stagePicker.activeSelf) { bossPage.ShowStages(); ownsStagePicker = true; }
        RectTransform anchor; string text;
        switch(step)
        {
            case 0: anchor = bossPage.bossTitle.transform as RectTransform;
                text = "This is your selected Boss. Battles put your cat's earned progress to use. You choose when to challenge. This guide will not start a battle for you."; break;
            case 1: anchor = bossPage.catHealth;
                text = "Your cat's level and equipped gear affect its battle stats. Watch its health during combat. Weapon, Body and Head equipment improve ATK, DEF and attack speed respectively."; break;
            case 2: anchor = bossPage.stageButton.transform as RectTransform;
                text = "Use the stage selector to review the bosses and your progress. Click the highlighted stage selector to look inside. Opening the selector does not spend a Ticket."; break;
            case 3: anchor = bossPage.stageChoices[1].transform as RectTransform;
                text = "Later bosses have extra requirements. Boss 2 needs a Boss 1 victory and Lv.10. Boss 3 needs a Boss 2 victory and Lv.20. Skipping this guide does not bypass these conditions."; break;
            case 4: anchor = bossPage.masteryBars[0].transform.parent as RectTransform;
                text = "Each normal Boss has its own fixed mastery rewards: 5 wins unlock the Weapon, 10 wins the Body Armour, and 15 wins the Head Accessory. Open the reward chests here to claim earned pieces."; break;
            case 5: anchor = bossPage.masterChoice.transform as RectTransform;
                text = "Your first Boss 3 victory opens Master Stage. It cycles through stronger bosses, and only victory advances the stage. Master Stage does not grant normal-boss mastery equipment."; break;
            default: anchor = bossPage.action.transform as RectTransform;
                text = "Click Challenge to review entry. Only your confirmation spends one Boss Ticket. A valid defeat still uses it. Focus can earn Tickets under its limits. The guide never confirms for you. Skip if you are not ready."; break;
        }
        Show("boss-step-" + step, text, anchor, "Continue");
    }
    void ShowLeaderboard()
    {
        if(!service.CanAccess(ProgressionFeature.Leaderboard)) { Show("leaderboard-locked", "The Leaderboard opens at Lv.5. Display names and sharing are optional. The guide never turns on uploads.", null, "Continue"); return; }
        if(!settings.gameObject.activeSelf) { settings.Open(gameObject); settings.OpenAccount(); ownsAccount = true; }
        settings.TutorialOwnsInput = true;
        if(leaderboard) leaderboard.TutorialOwnsInput = true;
        transform.SetAsLastSibling();
        int step = Mathf.Clamp(progress.leaderboardStep, 0, 2);
        RectTransform anchor = step == 0 ? (RectTransform)settings.playerName.transform : step == 1 ? (RectTransform)settings.upload.transform : privacyTarget;
        string text = step == 0 ? "This is your leaderboard display name. Choose the public name you want other players to see, then use the existing Save button when you decide to change it. This guide will not edit or save your name."
            : step == 1 ? "This switch controls optional statistics uploads. You decide whether to share. The guide will leave its current setting unchanged and will not sign you in. Press Continue to learn what can be shared."
            : "Only your chosen display name and approved game totals are shared. Private tasks, schedules, tags and detailed history stay on this computer. Continue closes this explanation without changing your privacy choices.";
        Show("leaderboard-step-" + step, text, anchor, "Continue");
    }
    public void OpenHelp()
    {
        if(service == null) return;
        if(settings && settings.gameObject.activeInHierarchy) settings.Close(); manualTopic = null; Hide(); helpPanel.SetActive(true);
        if(helpBackdrop) helpBackdrop.SetActive(true);
        helpPanel.transform.SetAsLastSibling();
        if(helpKeyboard) helpKeyboard.Open(topics.Concat(new[]{helpClose}).ToArray(),CloseHelp);
    }
    public void CloseHelp() { manualTopic = null; helpPanel.SetActive(false); if(helpBackdrop) helpBackdrop.SetActive(false); snoozed = true; }
    public void OpenTopic(int index)
    {
        helpPanel.SetActive(false); if(helpBackdrop) helpBackdrop.SetActive(false); Hide(); manualTopic = "help"; manualIndex = index;
        if(index == 0)
        {
            manualTopic = null;
            if(Save(p => { p.dismissed = false; if(p.taskStep >= 4) { p.taskStep = 0; p.taskPageStep = 0; p.taskId = null; } })) { snoozed = false; pollAt = 0; }
            return;
        }
        if(index == 1) { if(!Save(p => p.focusStep = 0)) return; if(service.CanAccess(ProgressionFeature.Focus) && !navigation.ShowPage(MainMenuPage.Focus)) { manualTopic = null; return; } ShowFocus(); }
        else if(index == 2) Show("help-rewards", "Complete real tasks to earn EXP and Fish Coins within daily allowances. Completed work is recorded even at the cap. Claim completed Daily Reward goals separately. The first claim opens equipment management.", null, "Close");
        else if(index == 3) { if(!Save(p => p.bossStep = 0)) return; if(service.CanAccess(ProgressionFeature.Boss) && !navigation.ShowPage(MainMenuPage.Boss)) { manualTopic = null; return; } ShowBoss(); }
        else if(index == 5) { manualTopic = "settings"; settingsStep = 0; ShowSettings(); }
        else { if(!Save(p => p.leaderboardStep = 0)) return; if(service.CanAccess(ProgressionFeature.Leaderboard) && !navigation.ShowPage(MainMenuPage.Leaderboard)) { manualTopic = null; return; } ShowLeaderboard(); }
    }
    void ShowSettings()
    {
        if(!settings.gameObject.activeSelf) settings.Open(gameObject);
        ownsSettings = true;
        settings.TutorialOwnsInput = true;
        transform.SetAsLastSibling();
        RectTransform anchor; string text;
        switch(settingsStep)
        {
            case 0: anchor = settings.notifications.transform.parent as RectTransform;
                text = "Focus notifications let you know when Focus or Rest finishes. This switch controls those reminders. The guide leaves your current choice unchanged."; break;
            case 1: anchor = settings.sound.transform.parent as RectTransform;
                text = "Sound controls effects and feedback volume. Move this slider when you want louder or quieter sounds. Zero mutes sound effects."; break;
            case 2: anchor = settings.music.transform.parent as RectTransform;
                text = "Music has its own volume control. You can keep background music quiet or mute it while leaving sound effects on."; break;
            case 3: anchor = settings.dashboardRankings[0].transform.parent as RectTransform;
                text = "Choose the ranking shown on your Dashboard: Boss Stage, Tasks Completed, Focus Minutes or Power. This choice does not enable statistics uploads."; break;
            case 4: anchor = settings.defaultTaskTabs[0].transform.parent as RectTransform;
                text = "Choose the starting Tasks tab for each launch: Today, Daily Tasks or Todo. Later visits keep your current tab, search, filters and scroll position."; break;
            case 5: anchor = settings.showDashboardTasks.transform.parent.parent as RectTransform;
                text = "These switches show or hide individual Dashboard cards. Hiding a card does not delete its data or stop an active Focus session. Reduce motion limits decorative effects without changing your progress."; break;
            case 6: anchor = settings.tagsButton.transform as RectTransform;
                text = "Manage tags opens your tag collection. Add tags, rename them and choose their colours here. Tasks and Focus use those same tag colours."; break;
            case 7: anchor = settings.accountButton.transform as RectTransform;
                text = "Account manages your display name, sign-in and optional leaderboard uploads. Sharing is your choice. Your private tasks and detailed history stay local."; break;
            default: anchor = settings.aiButton.transform as RectTransform;
                text = "AI opens your chat connection settings. You can create and complete tasks without an AI connection. Continue to finish and return to Settings."; break;
        }
        Show("help-settings-" + settingsStep, text, anchor, "Continue");
    }
    void FinishSettingsGuide()
    {
        manualTopic = null; Hide(); snoozed = true;
        settings.Open(gameObject);
    }
    bool CanGoBack => progress != null && shown != null && (
        (manualTopic == "settings" && settingsStep > 0)
        || (shown.StartsWith("task-page-") && TaskPagePosition > 0)
        || (shown.StartsWith("focus-step-") && progress.focusStep > 0)
        || (shown.StartsWith("equipment-step-") && progress.equipmentStep > 0)
        || (shown.StartsWith("boss-step-") && progress.bossStep > 0)
        || (shown.StartsWith("leaderboard-step-") && progress.leaderboardStep > 0));
    public void GoBack()
    {
        if(!panel.activeSelf || !CanGoBack) return;
        // Rewind explanation progress only; keep real tasks, sessions and rewards committed.
        if(manualTopic == "settings") { settingsStep--; Hide(); ShowSettings(); return; }
        Action show;
        if(shown.StartsWith("task-page-")) {
            int previous = TaskPageOrder[TaskPagePosition - 1];
            if(!Save(p => p.taskPageStep = previous)) return; show = ShowTasks;
        } else if(shown.StartsWith("focus-step-")) {
            if(!Save(p => p.focusStep--)) return; show = ShowFocus;
        } else if(shown.StartsWith("equipment-step-")) {
            if(!Save(p => p.equipmentStep--)) return; show = ShowEquipment;
        } else if(shown.StartsWith("boss-step-")) {
            if(!Save(p => p.bossStep--)) return; show = ShowBoss;
        } else {
            if(!Save(p => p.leaderboardStep--)) return; show = ShowLeaderboard;
        }
        Hide(); show();
    }
    public void Continue()
    {
        if(typing != null) { CompleteTyping(); return; }
        if(RequiresTargetClick() && !advancingFromTarget) return;
        if(manualTopic == "settings") {
            if(settingsStep < 8) { settingsStep++; ShowSettings(); }
            else FinishSettingsGuide();
            return;
        }
        if(shown == "help-rewards") {
            if(!Save(p => p.equipmentStep = 0)) return;
            manualTopic = "equipment"; Hide(); ShowEquipment(); return;
        }
        if(shown == "equipment-open") {
            if(navigation.ShowPage(MainMenuPage.CatHome)) ShowEquipment();
            return;
        }
        if(shown != null && shown.StartsWith("equipment-step-")) {
            if(progress.equipmentStep < 6) { if(!Save(p => p.equipmentStep++)) return; Hide(); ShowEquipment(); }
            else { if(!Save(p => { if(!p.read.Contains("equipment")) p.read.Add("equipment"); })) return; manualTopic = null; Hide(); }
            return;
        }
        if(shown != null && shown.StartsWith("focus-step-"))
        {
            if(progress.focusStep < 6) { if(!Save(p => p.focusStep++)) return; Hide(); ShowFocus(); }
            else { if(!Save(p => { if(!p.read.Contains("focus")) p.read.Add("focus"); })) return; manualTopic = null; Hide(); }
            return;
        }
        if(shown != null && shown.StartsWith("task-page-"))
        {
            if(progress.taskPageStep == 6 && progress.taskStep != 3) return;
            if(TaskPagePosition < TaskPageOrder.Length - 1) {
                int following = TaskPageOrder[TaskPagePosition + 1];
                if(!Save(p => p.taskPageStep = following)) return;
                if(!advancingFromTarget) Hide();
                ShowTasks();
            } else { if(!Save(p => { p.taskStep = 4; if(!p.read.Contains("tasks"))p.read.Add("tasks"); }))return; Hide(); }
            return;
        }
        if(shown != null && shown.StartsWith("boss-step-"))
        {
            if(progress.bossStep < 6) { if(!Save(p => p.bossStep++)) return; Hide(); ShowBoss(); }
            else { if(!Save(p => { if(!p.read.Contains("boss"))p.read.Add("boss"); }))return; manualTopic=null;Hide(); }
            return;
        }
        if(shown != null && shown.StartsWith("leaderboard-step-")) {
            if(progress.leaderboardStep < 2) { if(!Save(p => p.leaderboardStep++)) return; Hide(); ShowLeaderboard(); }
            else { if(!Save(p => { if(!p.read.Contains("leaderboard"))p.read.Add("leaderboard"); }))return; manualTopic=null;Hide(); }
            return;
        }
        if(manualTopic != null) { manualTopic = null; Hide(); snoozed = true; return; }
        string key = shown;
        if(key == "task-0") { if(!Save(p => { p.taskStep = 1; p.taskPageStep = 5; })) return; }
        else if(key == "task-open") { navigation.ShowPage(MainMenuPage.Tasks); return; }
        else if(key == "task-missing") { if(!Save(p => { p.taskStep = 2; p.taskId = null; })) return; }
        else if(key == "task-saved") { if(!Save(p => { p.taskStep = 4; if(!p.read.Contains("tasks")) p.read.Add("tasks"); })) return; }
        else if(key == "unlock")
        {
            bool focusFirst = progress.announcedLevel < 2;
            if(!Save(p => p.announcedLevel = service.RewardLevel)) return;
            Hide(); navigation.ShowPage(focusFirst ? MainMenuPage.Focus : MainMenuPage.Boss); return;
        }
        else if(key == "focus-start") { Hide(); return; }
        else if(key is "reward" or "daily" or "equipment" or "boss" or "leaderboard") { if(!Save(p => { if(!p.read.Contains(key)) p.read.Add(key); })) return; }
        Hide(); pollAt = Time.unscaledTime + .5f;
    }
    public void Skip() { if(Save(p => p.dismissed = true)) { if(ownsSettings) { FinishSettingsGuide(); return; } snoozed = true; manualTopic = null; Hide(); } }
    void LockedNotice(string message) { if(!Busy()) Show("locked", message + ". Complete tasks to grow your cat. You can read the guide in Settings > Help.", null, "Got it"); }
    void Show(string key, string text, RectTransform anchor, string button, bool welcome = false)
    {
        if(tasks && key.StartsWith("task-page-")) tasks.TutorialOwnsInput = true;
        target = anchor;
        if(shown == key && panel.activeSelf) { Position(welcome); return; }
        StopTalking(); shown = key; panel.SetActive(true); panel.transform.SetAsLastSibling();
        next.interactable = true; nextLabel.text = "Continue";
        dialogue.Show("Master Loke", text.Replace("{playerName}", (string.IsNullOrWhiteSpace(GameDataManager.Instance.GetUsername()) ? "apprentice" : GameDataManager.Instance.GetUsername())));
        Position(welcome);
        var animator = mentor.CharacterAnimator;
        if(animator) { animator.updateMode = AnimatorUpdateMode.UnscaledTime; animator.Play("sensei-talk", 0, 0); }
        float voiceDuration = SettingsAudio.EnsureInstance()?.PlayDialogue(this, text, false) ?? 0;
        typing = StartCoroutine(dialogue.Reveal(.025f, () => { typing = null; SettingsAudio.Instance?.StopDialogue(this); dialogue.Complete(); Idle(); RefreshInput(); }, voiceDuration));
        RefreshInput();
    }
    void Position(bool welcome)
    {
        var root = (RectTransform)transform;
        float width = Mathf.Min(welcome ? 940 : 660, root.rect.width - 32);
        bool equipmentGuide = shown != null && shown.StartsWith("equipment-");
        float textHeight = dialogue.body.GetPreferredValues(dialogue.body.text, width - 205, Mathf.Infinity).y;
        float height = Mathf.Min(root.rect.height - 32, Mathf.Max(equipmentGuide ? 280 : 260, textHeight + 150));
        card.sizeDelta = new Vector2(width, height);
        bool top = false;
        bool targetInteractive = RequiresTargetClick() || shown == "task-page-4" || (shown == "task-page-6" && progress.taskStep != 3) || (shown != null && (shown == "focus-step-1" || shown == "focus-step-2" || shown == "focus-step-3" || shown == "focus-step-6"));
        if(spotlight) spotlight.Show(target, targetInteractive && (!IsTyping || shown == "task-page-6"), next, skip, back);
        highlight.gameObject.SetActive(target && target.gameObject.activeInHierarchy);
        if(target && target.gameObject.activeInHierarchy)
        {
            target.GetWorldCorners(corners);
            Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
            highlight.anchorMin = highlight.anchorMax = root.pivot;
            float padding = manualTopic == "settings" ? 20 : 5;
            highlight.pivot = Vector2.zero; highlight.anchoredPosition = min - Vector2.one * padding;
            highlight.sizeDelta = max - min + Vector2.one * (padding * 2);
            top = min.y < -root.rect.height * .12f;
        }
        if(pointer) {
            bool point = target && target.gameObject.activeInHierarchy && (targetInteractive || RequiresTargetClick());
            pointer.gameObject.SetActive(point);
            if(point) {
                Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
                bool below = min.y > root.rect.yMin + 90;
                pointer.anchorMin = pointer.anchorMax = root.pivot;
                pointer.anchoredPosition = new Vector2(Mathf.Clamp((min.x+max.x)*.5f, root.rect.xMin+40, root.rect.xMax-40), below ? min.y-40 : max.y+40);
                pointer.localRotation = Quaternion.Euler(0,0,below?0:180);
            }
        }
        RefreshInput();
        if(tasks && tasks.gameObject.activeInHierarchy && tasks.TutorialNoticeVisible) top = true;
        card.anchorMin = card.anchorMax = welcome ? new Vector2(.5f, 0) : new Vector2(1, top ? 1 : 0);
        card.pivot = welcome ? new Vector2(.5f, 0) : new Vector2(1, top ? 1 : 0);
        card.anchoredPosition = new Vector2(welcome ? 0 : -16, top ? -95 : 18);
        if(target && target.gameObject.activeInHierarchy) PlaceBesideTarget(root, width, height);
    }
    void PlaceBesideTarget(RectTransform root, float width, float height)
    {
        Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
        var bounds = root.rect;
        var targetRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        Vector2 best = default;
        float bestScore = float.PositiveInfinity;
        for(int side = 0; side < 4; side++) {
            float gap = 24;
            // Wide task rows put the card below their pointing arrow. Reserve
            // that arrow's existing extent so it cannot cover the nameplate.
            if(pointer && pointer.gameObject.activeSelf &&
                ((side == 2 && pointer.anchoredPosition.y < min.y) || (side == 3 && pointer.anchoredPosition.y > max.y)))
                gap = Mathf.Max(gap, Mathf.Abs(pointer.anchoredPosition.y - (side == 2 ? min.y : max.y))
                    + pointer.rect.height * Mathf.Abs(pointer.localScale.y) * .5f + 12);
            Vector2 candidate = side == 0 ? new Vector2(max.x + gap, (min.y + max.y - height) * .5f)
                : side == 1 ? new Vector2(min.x - gap - width, (min.y + max.y - height) * .5f)
                : side == 2 ? new Vector2((min.x + max.x - width) * .5f, min.y - gap - height)
                : new Vector2((min.x + max.x - width) * .5f, max.y + gap);
            var position = new Vector2(Mathf.Clamp(candidate.x, bounds.xMin + 16, bounds.xMax - width - 16),
                Mathf.Clamp(candidate.y, bounds.yMin + 16, bounds.yMax - height - 16));
            var placed = new Rect(position, new Vector2(width, height));
            float overlap = Mathf.Max(0, Mathf.Min(placed.xMax, targetRect.xMax) - Mathf.Max(placed.xMin, targetRect.xMin))
                * Mathf.Max(0, Mathf.Min(placed.yMax, targetRect.yMax) - Mathf.Max(placed.yMin, targetRect.yMin));
            float score = overlap * 1000 + (position - candidate).sqrMagnitude;
            if(score < bestScore) { bestScore = score; best = position; }
        }
        card.anchorMin = card.anchorMax = root.pivot;
        card.pivot = Vector2.zero;
        card.anchoredPosition = best;
    }
    void LateUpdate()
    {
        if(!panel || !panel.activeSelf) return;
        if(shown == "task-page-6") target = progress != null && progress.taskStep == 3 ? tasks.TutorialTaskTarget(progress.taskId) : tasks.TutorialAddTarget;
        Position(shown == "task-0");
    }
    bool RequiresTargetClick() => shown == "task-open" || shown == "task-page-0" || shown == "task-page-1" || shown == "task-page-2" || shown == "task-page-3" || (shown == "task-page-5" && progress.taskStep != 3 && !tasks.QuickAddInput) || shown == "task-page-8" || shown == "boss-step-2" || shown == "boss-step-6" || shown == "focus-step-6";
    void Observe(Button button)
    {
        if(observedButton == button) return;
        if(observedButton) observedButton.onClick.RemoveListener(TargetPressed);
        observedButton = button;
        if(observedButton) observedButton.onClick.AddListener(TargetPressed);
    }
    void TargetPressed()
    {
        if(IsTyping) return;
        if(shown == "boss-step-6" && !bossPage.confirmation.activeSelf) return;
        if(shown == "task-page-5" && !tasks.QuickAddInput) return;
        queuedAction = shown;
    }
    void RefreshInput()
    {
        if(typingShield) { typingShield.gameObject.SetActive(IsTyping && panel.activeSelf); if(typingShield.gameObject.activeSelf)typingShield.transform.SetAsLastSibling(); }
        bool waitingForAction = RequiresTargetClick() || (shown == "task-page-6" && progress.taskStep != 3);
        next.gameObject.SetActive(!waitingForAction);
        next.interactable = !waitingForAction;
        if(back) back.gameObject.SetActive(CanGoBack);
        float x = 28;
        PositionAction(back,ref x); PositionAction(next,ref x); PositionAction(skip,ref x);
        Observe(RequiresTargetClick() && target ? target.GetComponent<Button>() : null);
    }
    static void PositionAction(Button button, ref float x)
    {
        if(!button || !button.gameObject.activeSelf) return;
        var rect=(RectTransform)button.transform;
        rect.anchoredPosition=new Vector2(x,rect.anchoredPosition.y);
        x+=rect.rect.width+14;
    }
    public void CompleteTyping() { if(!IsTyping)return; StopTalking();dialogue.Complete();Position(shown == "task-0"); }
    void Idle() { var animator = mentor ? mentor.CharacterAnimator : null; if(animator && animator.isActiveAndEnabled && animator.gameObject.activeInHierarchy && !animator.GetCurrentAnimatorStateInfo(0).IsName("sensei-idle")) animator.Play("sensei-idle", 0, 0); }
    void StopTalking() { SettingsAudio.Instance?.StopDialogue(this); if(typing != null) StopCoroutine(typing); typing = null; Idle(); }
    void Hide() { if(taskMenuPreview) { taskMenuPreview.Dismiss(); Destroy(taskMenuPreview.gameObject); taskMenuPreview = null; } Observe(null); queuedAction=null; if(typingShield)typingShield.gameObject.SetActive(false); if(pointer)pointer.gameObject.SetActive(false); if(ownsSettings && settings) { settings.TutorialOwnsInput=false; settings.Close(); ownsSettings=false; } if(ownsAccount && settings) { settings.TutorialOwnsInput=false; settings.Close(); ownsAccount=false; } if(leaderboard)leaderboard.TutorialOwnsInput=false; if(ownsStagePicker && bossPage) { bossPage.stagePicker.SetActive(false); ownsStagePicker=false; } if(spotlight) spotlight.Dismiss(); if(typing != null || (panel && panel.activeSelf)) StopTalking(); shown = null; target = null; if(panel) panel.SetActive(false); if(highlight) highlight.gameObject.SetActive(false); if(tasks) tasks.TutorialOwnsInput=false; }
}
