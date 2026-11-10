using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Uses the main runner's isolated save, capture and restored Play lifecycle.
public static class RemainingGuidanceVerificationChecks
{
    static Type Runner => typeof(TaskTabsVerification).Assembly.GetType("RemainingFixesVerification")
        ?? throw new InvalidOperationException("RemainingFixesVerification runner is required.");
    static void Resolution(int width)
        => Runner.GetMethod("Resolution", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { width, width == 1920 ? 1080 : 720 });
    static IEnumerable<object> Capture(string name)
    {
        var run = (IEnumerator)Runner.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { name });
        while (run.MoveNext()) yield return run.Current;
    }

    public static IEnumerator Run(MainMenuNavigation nav, GameDataManager manager, bool before,
        string output, Action<bool, string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(manager.SavePath),
            "Guidance checks use the runner's isolated save");
        if (!before) {
            foreach (var wait in After(nav, manager, output, check)) yield return wait;
            yield break;
        }
        var service = manager.Tasks;
        var tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
        var originalTutorial = service.Tutorial;
        var originalPage = nav.CurrentPage;
        tutorial.Skip();
        var boss = nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        var focusPanel = nav.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>();
        FocusController focus = null;
        FocusSessionOptions options = null;
        try
        {
            check(nav.ShowPage(MainMenuPage.Boss), "Boss available for before result previews");
            yield return 1;
            for (int index = 0; index < 2; index++)
            {
                boss.resultPanels[index].SetActive(true);
                boss.resultPanels[index].transform.SetAsLastSibling();
                foreach (int width in new[] { 1920, 1280 })
                {
                    Resolution(width); yield return .4;
                    foreach (var wait in Capture("guidance-boss-" + (index == 0 ? "victory" : "defeat") + "-preview-" + width)) yield return wait;
                }
                boss.resultPanels[index].SetActive(false);
            }
            check(true, "Before Boss images are authored result previews only; no victory/defeat was fabricated or committed");

            check(nav.ShowPage(MainMenuPage.Focus), "Focus available for before guidance"); yield return .3;
            focus = focusPanel.Controller;
            options = focus.Options.Copy();
            check(focus.Configure(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 30 }),
                "Configure isolated 30-minute Countdown");
            check(focus.Start(), "Start isolated Focus for before End Early explanation");
            for (int tick = 0; tick < 90; tick++) focus.Advance(10);
            foreach (int width in new[] { 1920, 1280 })
            {
                Resolution(width); yield return .4;
                foreach (var wait in Capture("guidance-focus-15min-" + width)) yield return wait;
                focusPanel.endEarly.onClick.Invoke(); yield return .15;
                check(focusPanel.cancelConfirmation.IsOpen, "Before End Focus prompt is visible");
                foreach (var wait in Capture("guidance-focus-end-early-" + width)) yield return wait;
                focusPanel.cancelConfirmation.Dismiss();
            }
            check(focus.Cancel(), "Before Focus fixture cancels without rewards");
            check(focus.Configure(options), "Restore before Focus options");

            check(nav.ShowPage(MainMenuPage.Tasks), "Tasks available for before first teaching step");
            nav.taskSystem.SelectTab(TaskTab.Today);
            var progress = service.Tutorial; progress.dismissed = false; progress.taskStep = 2; progress.taskPageStep = 0;
            check(service.SaveTutorial(progress), "Prepare isolated unfinished Tasks guide");
            tutorial.OpenTopic(0); yield return .5; tutorial.CompleteTyping();
            check(tutorial.CurrentTopic == "task-page-0", "Before Tasks guide starts with Today explanation");
            foreach (int width in new[] { 1920, 1280 })
            {
                Resolution(width); yield return .3;
                foreach (var wait in Capture("guidance-tasks-first-step-" + width)) yield return wait;
            }
        }
        finally
        {
            foreach (var panel in boss.resultPanels) if (panel) panel.SetActive(false);
            if (focusPanel.cancelConfirmation) focusPanel.cancelConfirmation.Dismiss();
            if (focus != null && focus.Phase == FocusPhase.Focusing) focus.Cancel();
            if (focus != null && options != null) focus.Configure(options);
            tutorial.Skip(); service.SaveTutorial(originalTutorial);
            nav.ShowPage(originalPage);
        }
    }

    static IEnumerable<object> ReadyArena(BossPageUI boss, Action<bool,string> check)
    {
        for (int frame = 0; frame < 200 && boss.arena.IsLoading; frame++) yield return .05;
        check(boss.arena.IsReady, "Guidance battle waits for the actual async arena: " + boss.arena.LoadError);
    }
    static IEnumerable<object> Return(BossPageUI boss, Action<bool,string> check)
    {
        for (int frame = 0; frame < 120 && boss.IsReturningFromResult; frame++) yield return .05;
        check(!boss.IsReturningFromResult, "Result action finishes its existing transition before navigating");
    }
    static void Advance(FocusController focus, int seconds)
    {
        for (int elapsed = 0; elapsed < seconds; elapsed += 10) focus.Advance(Math.Min(10, seconds - elapsed));
    }
    static void TextFits(TMP_Text text, Action<bool,string> check, string label)
    {
        text.ForceMeshUpdate(); check(!text.isTextOverflowing, label + " fits its authored bounds");
    }
    static Rect Bounds(RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        var min = (Vector2)corners[0]; var max = min;
        foreach (var corner in corners) { min = Vector2.Min(min, corner); max = Vector2.Max(max, corner); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static void SaveFixture(GameDataManager manager, Action<bool,string> check)
        => check(manager.TrySaveData(out var error), "Save isolated guidance fixture: " + error);
    static void ResultOnlyContinue(BossPageUI boss, int index, Action<bool,string> check)
    {
        var panel = boss.resultPanels[index];
        var buttons = panel.GetComponentsInChildren<Button>(true).Where(button => button.gameObject.activeInHierarchy).ToArray();
        check(panel.activeInHierarchy && buttons.Length == 1 && buttons[0] == boss.resultContinue[index]
            && buttons[0].IsInteractable() && buttons[0].GetComponentInChildren<TMP_Text>().text == "Continue",
            "The committed result exposes only its original Continue action");
        check(!panel.GetComponentsInChildren<Slider>(true).Any(slider => slider.gameObject.activeInHierarchy),
            "The result has no added mastery progress row");
        check(EventSystem.current.currentSelectedGameObject == buttons[0].gameObject,
            "Continue is the safe default keyboard selection");
        check(index == 0
            ? !boss.resultDetails[0].gameObject.activeSelf && string.IsNullOrEmpty(boss.resultDetails[0].text)
            : boss.resultValues[1].text == "Train and try again" && boss.resultDetails[1].text == "No rewards · Progress kept",
            "Victory and defeat retain their original result copy without added guidance");
        TextFits(buttons[0].GetComponentInChildren<TMP_Text>(), check, "Result Continue");
        if (boss.resultValues[index].gameObject.activeInHierarchy) TextFits(boss.resultValues[index], check, "Committed result value");
        if (boss.resultDetails[index].gameObject.activeInHierarchy) TextFits(boss.resultDetails[index], check, "Original result detail");
    }
    static IEnumerable<object> ContinueResult(MainMenuNavigation nav, BossPageUI boss, TaskService service,
        int index, string input, Action<bool,string> check)
    {
        ResultOnlyContinue(boss, index, check);
        string committed = JsonUtility.ToJson(service.Snapshot);
        boss.SkipBattle(); boss.Settle(index == 0 ? BattleOutcome.Victory : BattleOutcome.Defeat);
        boss.Confirm(); boss.Action(); boss.ShowStages();
        check(JsonUtility.ToJson(service.Snapshot) == committed && !boss.confirmation.activeSelf && !boss.stagePicker.activeSelf,
            "Repeated result input cannot settle, enter or spend again");
        var previousKeyboard = Keyboard.current;
        var resultKeyboard = InputSystem.AddDevice<Keyboard>(); resultKeyboard.MakeCurrent();
        try {
            InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState(Key.Tab)); yield return .05;
            check(EventSystem.current.currentSelectedGameObject == boss.resultContinue[index].gameObject,
                "Actual Tab remains on the only result action");
            InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState()); yield return .05;
            InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState(Key.LeftShift, Key.Tab)); yield return .05;
            check(EventSystem.current.currentSelectedGameObject == boss.resultContinue[index].gameObject,
                "Actual Shift-Tab does not reach the background");
            InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState()); yield return .05;
            if (input == "escape") {
                InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState(Key.Escape)); yield return .05;
                InputSystem.QueueStateEvent(resultKeyboard, new KeyboardState()); yield return .05;
            } else if (input == "submit") {
                ExecuteEvents.Execute(boss.resultContinue[index].gameObject,
                    new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            } else FocusLayoutVerification.Click(boss.resultContinue[index]);
            check(!boss.resultPanels.Any(panel => panel.activeSelf), input + " immediately closes the result");
            boss.resultContinue[index].onClick.Invoke(); boss.CloseResult(); boss.Action(); boss.ShowStages();
            foreach (var wait in Return(boss, check)) yield return wait;
            check(nav.CurrentPage == MainMenuPage.Boss && !boss.stagePicker.activeSelf && !boss.confirmation.activeSelf
                && !boss.IsBattling && !service.HasActiveBattle && JsonUtility.ToJson(service.Snapshot) == committed
                && !boss.GetComponent<EquipmentPageUI>().claimView.gameObject.activeInHierarchy,
                "Continue returns to the Boss preview without navigation, entry, claim or duplicate reward");
            check(EventSystem.current.currentSelectedGameObject == boss.action.gameObject,
                "The existing return transition restores keyboard focus to Challenge");
        } finally {
            InputSystem.RemoveDevice(resultKeyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        }
    }
    static void GlyphsFit(TMP_Text text, RectTransform card, Action<bool,string> check, string label)
    {
        TextFits(text, check, label);
        var bounds = Bounds(card);
        bool contained = true;
        int visible = 0;
        for (int i = 0; i < text.textInfo.characterCount; i++) {
            var character = text.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            visible++;
            foreach (var point in new[] { character.bottomLeft, character.topLeft, character.topRight, character.bottomRight }) {
                var world = text.transform.TransformPoint(point);
                contained &= world.x >= bounds.xMin && world.x <= bounds.xMax && world.y >= bounds.yMin && world.y <= bounds.yMax;
            }
        }
        check(visible > 0 && contained, label + " has visible glyphs contained by its actual card");
    }
    static void FocusPreview(FocusCancelConfirmation confirmation, RewardAmounts expected, int goalMinutes, Action<bool,string> check)
    {
        var estimateLabel = confirmation.rewardPreview.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "EstimateLabel");
        check(confirmation.rewardPreview && confirmation.rewardPreview.activeInHierarchy && !confirmation.info.gameObject.activeSelf && estimateLabel.text == "Estimated on save"
            && confirmation.previewExperience.text == expected.experience.ToString() && confirmation.previewFish.text == expected.fishCoins.ToString()
            && confirmation.previewTickets.text == expected.tickets.ToString() && confirmation.previewGoal.text == $"+{goalMinutes} min today",
            "Icon preview shows the actual service estimate and eligible daily-goal minutes without claiming they are saved");
        foreach (var text in new[] { estimateLabel, confirmation.previewExperience, confirmation.previewFish, confirmation.previewTickets, confirmation.previewGoal })
            GlyphsFit(text, confirmation.card, check, "End Early " + text.name);
    }
    static IEnumerable<object> FocusTicketHelp(FocusPanelUI panel, TaskService service, int width, Action<bool,string> check)
    {
        var target = panel.ticketAllowanceLabel.GetComponent<RewardLimitHoverTarget>();
        var sessionTarget = panel.ticketProgressCard.GetComponent<RewardLimitHoverTarget>();
        check(target && sessionTarget && target.tooltip && sessionTarget.tooltip == target.tooltip
            && target.kind == RewardLimitKind.FocusSessionTickets && sessionTarget.kind == target.kind
            && target.GetComponent<Selectable>() && target.GetComponent<Selectable>().IsInteractable()
            && sessionTarget.GetComponent<Selectable>() && (!panel.ticketProgressCard.activeInHierarchy || sessionTarget.GetComponent<Selectable>().IsInteractable()),
            "Persistent allowance and session progress share the existing keyboard-accessible Focus Ticket tooltip");
        var tooltip = target.tooltip;
        var pointer = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return .1;
        check(tooltip.gameObject.activeInHierarchy && tooltip.note.text.Contains("25 min in one session")
            && tooltip.note.text.Contains("Short sessions do not combine") && tooltip.ticketBar.maxValue == 3
            && tooltip.ticketBar.value == service.TodayRewards.focusTickets,
            "Focus Ticket rules remain reachable and use the actual three-ticket daily allowance");
        GlyphsFit(tooltip.note, tooltip.card, check, "Focus Ticket rules " + width);
        GlyphsFit(tooltip.ticketValue, tooltip.card, check, "Focus Ticket allowance " + width);
        foreach (var wait in Capture("guidance-focus-ticket-rules-" + (service.TodayRewards.focusTickets >= 3 ? "capped-" : "") + width)) yield return wait;
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerExitHandler); yield return .05;
        check(!tooltip.gameObject.activeSelf, "Focus Ticket rule tooltip closes on pointer exit");
        EventSystem.current.SetSelectedGameObject(target.gameObject); yield return .05;
        check(tooltip.gameObject.activeSelf, "Persistent Focus Ticket rules open from keyboard focus");
        EventSystem.current.SetSelectedGameObject(null); yield return .05;
        check(!tooltip.gameObject.activeSelf, "Focus Ticket rules close on keyboard deselection");
        if (panel.ticketProgressCard.activeInHierarchy) {
            ExecuteEvents.Execute(sessionTarget.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return .05;
            check(tooltip.gameObject.activeSelf, "Session progress hover reaches the same rule tooltip");
            ExecuteEvents.Execute(sessionTarget.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            EventSystem.current.SetSelectedGameObject(sessionTarget.gameObject); yield return .05;
            check(tooltip.gameObject.activeSelf, "Session progress keyboard focus reaches the same rule tooltip");
            EventSystem.current.SetSelectedGameObject(null); yield return .05;
            check(!tooltip.gameObject.activeSelf, "Session rule tooltip leaves no overlay after focus moves away");
        }
    }
    static IEnumerable<object> DismissCelebrations(Action<bool,string> check)
    {
        var view = UnityEngine.Object.FindFirstObjectByType<LevelUpCelebrationUI>();
        if (!view) yield break;
        for (int frame = 0; frame < 160 && (view.IsShowing || view.PendingCount > 0); frame++) {
            if (view.IsShowing && view.continueButton.interactable) view.Dismiss();
            yield return .05;
        }
        check(!view.IsShowing && view.PendingCount == 0, "Earned level-up feedback is dismissed before the next guidance interaction");
    }
    static string TutorialState(TutorialController tutorial, TaskService service, FocusController focus)
    {
        var type = typeof(TutorialController);
        object Read(string field) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tutorial);
        var progress = service.Tutorial;
        return $"frame={Time.frameCount}; unscaled={Time.unscaledTime:F3}; topic={tutorial.CurrentTopic ?? "<null>"}"
            + $"; active={tutorial.isActiveAndEnabled}; panel={tutorial.panel.activeSelf}; page={tutorial.navigation.CurrentPage}"
            + $"; focused={Read("focused")}; applicationFocused={Application.isFocused}; dirty={Read("dirty")}; pollAt={Read("pollAt")}"
            + $"; taskStep={progress.taskStep}; contentStep={progress.taskPageStep}; dismissed={progress.dismissed}; snoozed={Read("snoozed")}"
            + $"; focus={focus.Phase}; rewards={tutorial.rewards.IsAnimating}; flights={tutorial.rewards.ActiveCount}"
            + $"; celebration={tutorial.celebration.IsShowing}/{tutorial.celebration.PendingCount}; taskBlocked={tutorial.tasks.TutorialBlocked}"
            + $"; settings={tutorial.settings.gameObject.activeInHierarchy}; blockers={string.Join(",", tutorial.blockers.Where(b => b && b.activeInHierarchy).Select(b => b.name))}"
            + $"; battle={service.HasActiveBattle}; next={tutorial.next.gameObject.activeSelf}; back={tutorial.back.gameObject.activeSelf}; error={service.LastError}";
    }
    // Same Boss sequence as the aggregate, with its own fixture restoration for a narrow run.
    public static IEnumerator RunBoss(MainMenuNavigation nav, GameDataManager manager, bool before,
        string output, Action<bool,string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!before && !string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(manager.SavePath),
            "Boss-result after checks use the runner's isolated save");
        var original = PlayerDataStore.Copy(manager.PlayerData);
        var originalPage = nav.CurrentPage;
        var tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
        tutorial.Skip();
        try {
            foreach (var wait in BossResults(nav, manager, check)) yield return wait;
        } finally {
            tutorial.Skip(); nav.ShowPage(MainMenuPage.Dashboard);
            var store = (IPlayerDataStore)typeof(GameDataManager).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            check(store.TrySave(original, out var error), "Restore isolated Boss-result fixture: " + error);
            nav.ShowPage(originalPage);
        }
    }
    static IEnumerable<object> BossResults(MainMenuNavigation nav, GameDataManager manager, Action<bool,string> check)
    {
        var service = manager.Tasks;
        var boss = nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        // Fixture supplies prior progress; every outcome is settled by the actual service.
        manager.PlayerData.rewardDays.Clear(); manager.PlayerData.focusSessions.Clear();
        manager.PlayerData.experience = 10000; manager.PlayerData.tickets = 20;
        manager.PlayerData.equipmentUnlocked = true; manager.PlayerData.equipment.Clear();
        manager.PlayerData.equippedWeaponId = manager.PlayerData.equippedBodyOutfitId = manager.PlayerData.equippedHeadAccessoryId = null;
        foreach (var progress in manager.PlayerData.bossProgress) progress.wins = progress.pendingClaims = progress.claimedComponents = 0;
        manager.PlayerData.bossProgress.Single(p => p.bossId == 1).wins = 4;
        SaveFixture(manager, check);
        check(boss.resultPanels.Length == 2 && boss.resultContinue.Length == 2
            && boss.resultContinue.All(button => button), "Both original result Continue bindings remain authored");
        check(nav.ShowPage(MainMenuPage.Boss), "Open Boss for real committed results"); boss.SelectStage(1);
        foreach (var wait in ReadyArena(boss, check)) yield return wait;
        int entryTickets = service.Tickets;
        boss.Action(); boss.Confirm(); check(boss.IsBattling && service.Tickets == entryTickets - 1, "Confirmed entry consumes one ticket");
        var beforeVictory = service.Snapshot;
        using (var locked = File.Open(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            boss.SkipBattle();
            check(boss.actionLabel.text == "Retry Save" && !boss.resultPanels.Any(panel => panel.activeSelf)
                && JsonUtility.ToJson(service.Snapshot) == JsonUtility.ToJson(beforeVictory),
                "Failed victory save exposes no success and changes no reward or mastery");
        }
        boss.Action(); yield return .2;
        var receipt = service.Snapshot.battles.Last();
        var mastery = service.EquipmentSnapshot.bossProgress.Single(progress => progress.bossId == 1);
        check(receipt.outcome == BattleOutcome.Victory && mastery.wins == 5 && mastery.pendingClaims == 1
            && mastery.claimedComponents == 0 && service.Tickets == entryTickets - 1
            && service.Experience == beforeVictory.experience + receipt.experience
            && service.Snapshot.fishCoins == beforeVictory.fishCoins + receipt.fishCoins,
            "Retry commits the real fifth victory, reward and pending claim once");
        foreach (int width in new[] { 1920, 1280 }) {
            Resolution(width); yield return .3; ResultOnlyContinue(boss, 0, check);
            foreach (var wait in Capture("guidance-boss-victory-" + width)) yield return wait;
        }
        foreach (var wait in ContinueResult(nav, boss, service, 0, "mouse", check)) yield return wait;
        check(new PlayerDataStore(manager.SavePath).Data.bossProgress.Single(p => p.bossId == 1).wins == 5,
            "Committed mastery survives reload");
        check(service.ClaimEquipment("boss1-weapon"), "The existing claim rule still grants the earned fifth-win Weapon");
        foreach (int priorWins in new[] { 5, 9, 14, 15 }) {
            manager.PlayerData.bossProgress.Single(p => p.bossId == 1).wins = priorWins;
            SaveFixture(manager, check); boss.SelectStage(1);
            foreach (var wait in ReadyArena(boss, check)) yield return wait;
            var beforeEntry = service.Snapshot;
            boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin real mastery boundary after " + priorWins + " wins");
            boss.SkipBattle(); yield return .2;
            int wins = priorWins + 1; bool earned = wins == 10 || wins == 15;
            var progress = service.EquipmentSnapshot.bossProgress.Single(p => p.bossId == 1);
            var prior = beforeEntry.bossProgress.Single(p => p.bossId == 1);
            receipt = service.Snapshot.battles.Last();
            check(progress.wins == wins && progress.pendingClaims == prior.pendingClaims + (earned ? 1 : 0)
                && progress.claimedComponents == prior.claimedComponents && receipt.outcome == BattleOutcome.Victory
                && service.Tickets == beforeEntry.tickets - 1 && service.Experience == beforeEntry.experience + receipt.experience
                && service.Snapshot.fishCoins == beforeEntry.fishCoins + receipt.fishCoins
                && service.Snapshot.equipment.Count == beforeEntry.equipment.Count,
                "Actual victory preserves milestone, currency and no-auto-claim rules at " + wins + " wins");
            ResultOnlyContinue(boss, 0, check);
            if (earned) foreach (var wait in Capture("guidance-boss-mastery-" + wins + "-1280")) yield return wait;
            foreach (var wait in ContinueResult(nav, boss, service, 0, wins == 6 ? "submit" : wins == 10 ? "escape" : "mouse", check)) yield return wait;
            if (earned) check(service.ClaimEquipment(wins == 10 ? "boss1-body" : "boss1-head"),
                "The fixed milestone claim remains available outside the result");
        }
        manager.PlayerData.experience = 2025; manager.PlayerData.equipment.Clear();
        var firstProgress = manager.PlayerData.bossProgress.Single(p => p.bossId == 1);
        firstProgress.pendingClaims = firstProgress.claimedComponents = 0; firstProgress.wins = 1;
        SaveFixture(manager, check); boss.SelectStage(2);
        foreach (var wait in ReadyArena(boss, check)) yield return wait;
        var beforeDefeat = service.Snapshot;
        boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin valid low-level Orc battle");
        boss.SkipBattle(); yield return .2;
        receipt = service.Snapshot.battles.Last();
        check(receipt.outcome == BattleOutcome.Defeat && receipt.experience == 0 && receipt.fishCoins == 0
            && service.Experience == beforeDefeat.experience && service.Snapshot.fishCoins == beforeDefeat.fishCoins
            && service.Tickets == beforeDefeat.tickets - 1 && service.MasterStage == beforeDefeat.masterStage
            && service.Snapshot.bossProgress.Select(p => p.wins).SequenceEqual(beforeDefeat.bossProgress.Select(p => p.wins)),
            "Valid defeat keeps its spent entry and grants no currency, mastery or stage progress");
        foreach (int width in new[] { 1920, 1280 }) {
            Resolution(width); yield return .3; ResultOnlyContinue(boss, 1, check);
            foreach (var wait in Capture("guidance-boss-defeat-" + width)) yield return wait;
        }
        foreach (var wait in ContinueResult(nav, boss, service, 1, "submit", check)) yield return wait;
        check(boss.SelectedBoss == 2, "Defeat Continue returns to the same Boss preview");

        manager.PlayerData.experience = 10000; manager.PlayerData.bossProgress.Single(p => p.bossId == 1).wins = 0;
        SaveFixture(manager, check); boss.SelectStage(1);
        foreach (var wait in ReadyArena(boss, check)) yield return wait;
        check(service.BossUnlockError(2) != null, "Orc remains gated before the first valid victory");
        boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin first-win unlock regression");
        boss.SkipBattle(); yield return .2;
        check(service.Snapshot.battles.Last().outcome == BattleOutcome.Victory && service.BossUnlockError(2) == null
            && service.EquipmentSnapshot.bossProgress.Single(p => p.bossId == 1).wins == 1,
            "The first valid victory still unlocks Orc without a result-page shortcut");
        ResultOnlyContinue(boss, 0, check);
        foreach (var wait in Capture("guidance-boss-first-unlock-1280")) yield return wait;
        foreach (var wait in ContinueResult(nav, boss, service, 0, "escape", check)) yield return wait;

        manager.PlayerData.bossProgress.Single(p => p.bossId == 1).wins = 4;
        manager.PlayerData.equipmentUnlocked = false; SaveFixture(manager, check);
        boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin earned-mastery/locked-equipment boundary");
        boss.SkipBattle(); yield return .2;
        mastery = service.EquipmentSnapshot.bossProgress.Single(p => p.bossId == 1);
        check(mastery.wins == 5 && mastery.pendingClaims == 1 && !service.EquipmentUnlocked && !service.Snapshot.equipment.Any(),
            "Locked equipment retains its earned pending claim without granting an item");
        ResultOnlyContinue(boss, 0, check);
        foreach (var wait in Capture("guidance-boss-mastery-equipment-locked-1280")) yield return wait;
        foreach (var wait in ContinueResult(nav, boss, service, 0, "mouse", check)) yield return wait;

        var beforeCombinedResult = PlayerDataStore.Copy(manager.PlayerData);
        try {
            int levelTenExperience = 0;
            for (int level = 1; level < 10; level++) {
                TaskService.CatLevel(levelTenExperience, out _, out _, out int required); levelTenExperience += required;
            }
            int repeatVictoryExperience = service.Snapshot.battles.Last(b => b.bossId == 1
                && b.masterStage == 0 && b.outcome == BattleOutcome.Victory).experience;
            check(repeatVictoryExperience > 0, "Combined result uses the preceding real repeat-victory reward");
            manager.PlayerData.experience = levelTenExperience - repeatVictoryExperience;
            var progress = manager.PlayerData.bossProgress.Single(p => p.bossId == 1);
            progress.wins = 4; progress.pendingClaims = progress.claimedComponents = 0;
            manager.PlayerData.equipmentUnlocked = false; SaveFixture(manager, check); boss.SelectStage(1);
            foreach (var wait in ReadyArena(boss, check)) yield return wait;
            check(service.RewardLevel == 9 && service.BossUnlockError(2) != null, "Fifth-win fixture starts below the real Orc level gate");
            boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin real simultaneous mastery and Orc-unlock battle");
            boss.SkipBattle(); yield return .2;
            receipt = service.Snapshot.battles.Last();
            mastery = service.EquipmentSnapshot.bossProgress.Single(p => p.bossId == 1);
            check(receipt.outcome == BattleOutcome.Victory && receipt.experience == repeatVictoryExperience
                && service.Experience == levelTenExperience && service.RewardLevel == 10 && service.BossUnlockError(2) == null
                && mastery.wins == 5 && mastery.pendingClaims == 1 && !manager.PlayerData.equipmentUnlocked,
                "One committed victory still earns mastery and unlocks Orc at Lv.10 while equipment remains locked");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            foreach (int width in new[] { 1920, 1280 }) {
                Resolution(width); yield return .3; ResultOnlyContinue(boss, 0, check);
                foreach (var wait in Capture("guidance-boss-mastery-and-unlock-" + width)) yield return wait;
            }
            foreach (var wait in ContinueResult(nav, boss, service, 0, "mouse", check)) yield return wait;
        } finally {
            var store = (IPlayerDataStore)typeof(GameDataManager).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            check(store.TrySave(beforeCombinedResult, out var error), "Restore combined-result fixture: " + error);
        }
        manager.PlayerData.experience = 1000000; manager.PlayerData.masterStage = 1;
        manager.PlayerData.bossProgress.Single(p => p.bossId == 3).wins = 1;
        SaveFixture(manager, check); boss.SelectStage(1, 1);
        foreach (var wait in ReadyArena(boss, check)) yield return wait;
        var beforeMaster = service.Snapshot;
        boss.Action(); boss.Confirm(); check(boss.IsBattling, "Begin actual Master-stage battle");
        boss.SkipBattle(); yield return .2; receipt = service.Snapshot.battles.Last();
        check(receipt.outcome == BattleOutcome.Victory && service.MasterStage == 2
            && beforeMaster.bossProgress.Select(p => p.wins).SequenceEqual(service.Snapshot.bossProgress.Select(p => p.wins))
            && beforeMaster.bossProgress.Select(p => p.pendingClaims).SequenceEqual(service.Snapshot.bossProgress.Select(p => p.pendingClaims))
            && service.Snapshot.equipment.Count == beforeMaster.equipment.Count && service.Tickets == beforeMaster.tickets - 1
            && service.Experience == beforeMaster.experience + receipt.experience
            && service.Snapshot.fishCoins == beforeMaster.fishCoins + receipt.fishCoins,
            "Actual Master victory advances only its stage and committed currency without normal mastery rewards");
        ResultOnlyContinue(boss, 0, check);
        foreach (var wait in Capture("guidance-boss-master-result-1280")) yield return wait;
        foreach (var wait in ContinueResult(nav, boss, service, 0, "submit", check)) yield return wait;
        check(boss.SelectedBoss == 2 && boss.bossTitle.text.StartsWith("Master 2 "),
            "Continue selects the persisted next Master preview without opening the Stage picker");
        int tickets = service.Tickets; boss.Action();
        check(boss.confirmation.activeSelf && !boss.IsBattling && service.Tickets == tickets,
            "The next Master still requires a separate one-ticket confirmation");
        boss.cancel.onClick.Invoke();
        check(!boss.confirmation.activeSelf && !boss.IsBattling && service.Tickets == tickets, "Cancelling the next Master preview spends nothing");
        manager.PlayerData.experience = 10000; SaveFixture(manager, check);
    }
    static IEnumerable<object> After(MainMenuNavigation nav, GameDataManager manager, string output, Action<bool,string> check)
    {
        var service = manager.Tasks;
        var original = PlayerDataStore.Copy(manager.PlayerData);
        var originalPage = nav.CurrentPage;
        var tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
        var boss = nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        var panel = nav.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>();
        var focus = FocusRunner.EnsureInstance().Controller;
        var oldOptions = focus.Options.Copy();
        Keyboard keyboard = null;
        tutorial.Skip();
        try {
            foreach (var wait in BossResults(nav, manager, check)) yield return wait;

            check(nav.ShowPage(MainMenuPage.Focus), "Open Focus ticket explanation"); yield return .2;
            check(panel.ticketRule && panel.ticketProgressCard && panel.ticketProgressBar && panel.ticketStepValue,
                "Focus session timing uses editable icon/count/progress bindings");
            var beforeShortSession = PlayerDataStore.Copy(manager.PlayerData);
            check(focus.Configure(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 15 }), "Configure a real short Countdown");
            yield return .15;
            check(panel.ticketProgressCard.activeSelf && panel.ticketStepValue.text == "0", "Ready short plan does not promise a Ticket");
            check(focus.Start(), "Start the short Countdown"); yield return .1;
            check(panel.ticketProgressCard.activeSelf && panel.ticketStepValue.text == "0", "Starting a short plan keeps zero Ticket expectation");
            Advance(focus, 880); yield return .15;
            check(panel.ticketProgressCard.activeSelf && panel.ticketStepValue.text == "0" && panel.ticketReward.text == "0",
                "A running short plan still shows zero Tickets immediately before completion");
            Advance(focus, 20); yield return .15;
            check(focus.Phase == FocusPhase.Finished && focus.LastResult.tickets == 0 && !panel.ticketProgressCard.activeSelf,
                "Actual short Countdown completion saves zero Tickets and hides the next-ticket row");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            for (int frame = 0; frame < 100 && tutorial.rewards.IsAnimating; frame++) yield return .05;
            check(!tutorial.rewards.IsAnimating, "Short-session feedback finishes before restoring the isolated display fixture");
            var shortStore = (IPlayerDataStore)typeof(GameDataManager).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            check(shortStore.TrySave(beforeShortSession, out var shortError), "Restore isolated allowance after short-plan display check: " + shortError);
            check(focus.Configure(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 30 }), "Prepare the original End Early scenario");
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 30 }), "Start real Countdown guidance check");
            Advance(focus, 900);
            var estimated = service.PreviewFocusRewards(focus.ElapsedSeconds);
            foreach (int width in new[] { 1920, 1280 }) {
                Resolution(width); yield return .3;
                check(panel.ticketProgressCard.activeSelf && panel.ticketStepValue.text == "+1"
                    && panel.ticketProgressBar.value > .59f && panel.ticketProgressBar.value < .62f,
                    "Fifteen minutes in a longer session display actual progress toward its first Ticket");
                GlyphsFit(panel.ticketRule, (RectTransform)panel.ticketProgressCard.transform, check, "Focus next-ticket time " + width);
                GlyphsFit(panel.ticketStepValue, (RectTransform)panel.ticketProgressCard.transform, check, "Focus Ticket step " + width);
                foreach (var wait in Capture("guidance-focus-15min-" + width)) yield return wait;
                foreach (var wait in FocusTicketHelp(panel, service, width, check)) yield return wait;
                panel.endEarly.onClick.Invoke(); yield return .15;
                FocusPreview(panel.cancelConfirmation, estimated, 15, check);
                check(EventSystem.current.currentSelectedGameObject == panel.cancelConfirmation.keepFocusing.gameObject,
                    "End Early keeps the safe keyboard default");
                foreach (var wait in Capture("guidance-focus-end-early-" + width)) yield return wait;
                panel.cancelConfirmation.Dismiss();
            }
            panel.endEarly.onClick.Invoke();
            int experience = service.Experience, coins = service.FishCoins, tickets = service.Tickets;
            using (var locked = File.Open(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                panel.cancelConfirmation.Confirm();
                check(focus.Phase == FocusPhase.Saving && focus.LastResult == null && service.Experience == experience
                    && service.FishCoins == coins && service.Tickets == tickets && service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 0,
                    "An estimate never grants rewards or daily-goal minutes before persistence");
                yield return .15;
                check(!panel.ticketProgressCard.activeSelf && panel.allowanceNotice.activeSelf && !panel.cancelConfirmation.IsOpen,
                    "Pending save shows the existing failure notice without a next-ticket promise or success popup");
            }
            check(focus.RetrySettlement() && focus.LastResult.tickets == 0, "End Early retry commits earned time with zero short-session Tickets");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            int savedCount = service.Snapshot.focusSessions.Count;
            panel.cancelConfirmation.Confirm();
            check(service.Snapshot.focusSessions.Count == savedCount && !focus.RetrySettlement(), "Stale confirmation and retry cannot settle twice");
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 30 }), "Start second short Focus");
            Advance(focus, 900); check(focus.EndEarly(), "Save second short Focus");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            check(service.TodayRewards.focusTickets == 0 && service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 25,
                "Two 15-minute sessions count toward the Focus goal but do not combine for Tickets");
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Stopwatch }), "Start live Ticket boundary preview");
            Advance(focus, 1480); panel.endEarly.onClick.Invoke(); yield return .15;
            check(panel.cancelConfirmation.previewTickets.text == "0", "Estimate below 25 minutes has no Ticket");
            Advance(focus, 20); yield return 1.1;
            check(panel.cancelConfirmation.previewTickets.text == "1", "Open numeric estimate refreshes across the 25-minute boundary");
            panel.cancelConfirmation.Confirm(); check(focus.LastResult.tickets == 1, "Live estimate agrees with committed Ticket");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Countdown, focusMinutes = 30 }), "Start a 30-minute plan that cannot reach a second Ticket");
            Advance(focus, 1500); yield return .15;
            check(!panel.ticketProgressCard.activeSelf && panel.ticketReward.text == "1" && service.PreviewFocusRewards(focus.ElapsedSeconds).tickets == 1,
                "After 25 minutes a 30-minute plan keeps its earned Ticket preview without promising an unreachable next Ticket");
            check(focus.Cancel(), "Cancel the display-only 30-minute boundary fixture without rewards");
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Stopwatch }), "Start remaining daily allowance check");
            Advance(focus, 3000); check(focus.EndEarly() && focus.LastResult.tickets == 2, "Existing three-Ticket daily allowance is preserved");
            foreach (var wait in DismissCelebrations(check)) yield return wait;
            check(focus.Start(new FocusSessionOptions { mode = FocusTimerMode.Stopwatch }), "Start capped Ticket preview");
            Advance(focus, 1500); yield return 1.1; panel.endEarly.onClick.Invoke(); yield return .15;
            check(!panel.ticketProgressCard.activeSelf && panel.ticketAllowanceBar.maxValue == 3 && panel.ticketAllowanceBar.value == 3
                && panel.cancelConfirmation.previewTickets.text == "0",
                "The full daily bar and numeric End Early preview agree while the unavailable next-ticket row stays hidden");
            panel.cancelConfirmation.Dismiss();
            foreach (var wait in FocusTicketHelp(panel, service, 1280, check)) yield return wait;
            check(focus.Cancel(), "Cancel cap fixture without rewards");
            check(new PlayerDataStore(manager.SavePath).Data.rewardDays.Single(d => d.day == TaskService.DateKey(service.LocalNow)).focusTickets == 3,
                "Focus Ticket allowance and earned receipts survive reload");

            // The guide intentionally yields to earned reward feedback. Wait for
            // that shared presentation instead of treating its priority as a failure.
            for (int frame = 0; frame < 100 && tutorial.rewards.IsAnimating; frame++) yield return .05;
            check(!tutorial.rewards.IsAnimating, "Earned reward flights and pulses settle before the Tasks guide");
            check(nav.ShowPage(MainMenuPage.Tasks), "Open create-first Tasks guide"); nav.taskSystem.SelectTab(TaskTab.Today);
            var progressState = service.Tutorial; progressState.dismissed = false; progressState.taskStep = 2; progressState.taskPageStep = 0;
            check(service.SaveTutorial(progressState), "Prepare unfinished legacy guide content index");
            tutorial.OpenTopic(0);
            string readinessTrace = Path.Combine(output, "tutorial-readiness.txt");
            File.AppendAllText(readinessTrace, "Opened: " + TutorialState(tutorial, service, focus) + "\n");
            yield return .3;
            File.AppendAllText(readinessTrace, "Original 0.3s checkpoint: " + TutorialState(tutorial, service, focus) + "\n");
            // OpenTopic queues Tasks teaching for Update; elapsed Editor time alone
            // does not establish that a PlayerLoop ran. Keep real Busy/focus guards.
            for (int frame = 0; frame < 60 && (tutorial.CurrentTopic != "task-page-5" || !tutorial.panel.activeInHierarchy); frame++) {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                yield return .05;
                File.AppendAllText(readinessTrace, "Await Update: " + TutorialState(tutorial, service, focus) + "\n");
            }
            tutorial.CompleteTyping();
            bool createFirst = tutorial.CurrentTopic == "task-page-5" && tutorial.panel.activeInHierarchy
                && !tutorial.next.gameObject.activeSelf && !tutorial.back.gameObject.activeSelf;
            if (!createFirst) foreach (var wait in Capture("guidance-task-first-step-diagnostic-1280")) yield return wait;
            check(createFirst, "New and unfinished legacy guides offer actual creation before tab explanations; "
                + TutorialState(tutorial, service, focus));
            foreach (int width in new[] { 1920, 1280 }) {
                Resolution(width); yield return .3; TextFits(tutorial.dialogue.body, check, "Create-first guide " + width);
                check(!Bounds(tutorial.pointer).Overlaps(Bounds(tutorial.card)), "Create-first pointer does not cover its dialogue card or nameplate at " + width);
                foreach (var wait in Capture("guidance-tasks-first-step-" + width)) yield return wait;
            }
            FocusLayoutVerification.Click(tutorial.CurrentTarget.GetComponent<Selectable>()); yield return .3; tutorial.CompleteTyping();
            var input = nav.taskSystem.QuickAddInput; check(input && tutorial.CurrentTopic == "task-page-6", "Highlighted Add opens the real task field");
            string title = "Read one page - guidance regression";
            input.text = title; input.Select(); input.ActivateInputField(); keyboard = InputSystem.AddDevice<Keyboard>();
            int history = service.Snapshot.history.Count; experience = service.Experience;
            var taskDialog = (TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(nav.taskSystem);
            using (var locked = File.Open(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return .15;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return .15;
                int failedCount = service.Snapshot.tasks.Count(t => t.title == title);
                // The existing modal error has priority over the guide. Its
                // temporary Hide is expected; persisted teaching progress must stay put.
                bool preserved = taskDialog.Root.activeSelf && !tutorial.panel.activeSelf
                    && service.Tutorial.taskStep == 2 && service.Tutorial.taskPageStep == 6
                    && failedCount == 0 && input && input.text == title;
                if (!preserved) foreach (var wait in Capture("guidance-task-save-failure-diagnostic-1280")) yield return wait;
                check(preserved, "Failed real task save shows the existing error modal, preserves the draft and does not advance the saved guide step"
                    + $"; topic={tutorial.CurrentTopic ?? "<null>"}; taskStep={service.Tutorial.taskStep}; contentStep={service.Tutorial.taskPageStep}"
                    + $"; saved={failedCount}; input={(input ? input.text : "<missing>")}; currentInput={(nav.taskSystem.QuickAddInput ? nav.taskSystem.QuickAddInput.text : "<missing>")}"
                    + $"; focused={(input && input.isFocused)}; selection={EventSystem.current.currentSelectedGameObject?.name}; error={service.LastError}");
            }
            FocusLayoutVerification.Click(taskDialog.Root.transform.Find("Popup/Confirm").GetComponent<Button>());
            for (int frame = 0; frame < 40 && tutorial.CurrentTopic != "task-page-6"; frame++) yield return .05;
            tutorial.CompleteTyping();
            check(!taskDialog.Root.activeSelf && tutorial.CurrentTopic == "task-page-6" && input.text == title
                && service.Tutorial.taskStep == 2 && !service.Snapshot.tasks.Any(t => t.title == title),
                "Acknowledging the save error resumes the same guide and unsaved draft before explicit retry");
            input.Select(); input.ActivateInputField(); yield return .1;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return .15;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int frame = 0; frame < 50 && tutorial.CurrentTopic != "task-page-6-saved"; frame++) yield return .1;
            tutorial.CompleteTyping();
            check(service.Snapshot.tasks.Count(t => t.title == title) == 1 && tutorial.CurrentTopic == "task-page-6-saved"
                && service.Snapshot.history.Count == history && service.Experience == experience,
                "Real Enter saves exactly one task before advancing and fabricates no completed work");
            foreach (int width in new[] { 1920, 1280 }) {
                Resolution(width); yield return .3;
                TextFits(tutorial.dialogue.body, check, "Saved-task guide " + width);
                foreach (var wait in Capture("guidance-task-created-" + width)) yield return wait;
            }
            tutorial.GoBack(); yield return .2; tutorial.CompleteTyping(); tutorial.Continue(); yield return .2; tutorial.CompleteTyping();
            check(tutorial.CurrentTopic == "task-page-6-saved" && service.Snapshot.tasks.Count(t => t.title == title) == 1,
                "Back and resume preserve the saved task without asking for another");
            tutorial.Continue(); yield return .2; tutorial.CompleteTyping();
            check(tutorial.CurrentTopic == "task-page-7", "Saved task is followed by completion explanation");
            tutorial.Continue(); yield return .2; tutorial.CompleteTyping();
            check(tutorial.CurrentTopic == "task-page-0" && service.Snapshot.history.Count == history,
                "Tab tour begins after creation and remains explanation-only");
            tutorial.Skip(); check(!nav.taskSystem.TutorialOwnsInput, "Skip retains its existing release of task input");
        } finally {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            tutorial.Skip(); panel.cancelConfirmation.Dismiss();
            if (focus.Phase == FocusPhase.Focusing) focus.Cancel();
            if (focus.Phase == FocusPhase.Saving) focus.RetrySettlement();
            if (focus.Phase == FocusPhase.Resting) focus.EndRest();
            focus.Configure(oldOptions);
            nav.ShowPage(MainMenuPage.Dashboard);
            var store = (IPlayerDataStore)typeof(GameDataManager).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            check(store.TrySave(original, out var error), "Restore isolated guidance fixture: " + error);
            nav.ShowPage(originalPage);
        }
    }
}
