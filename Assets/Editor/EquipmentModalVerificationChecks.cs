using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Called by the existing isolated Play runner; owns no Editor lifecycle or assets.
public static class EquipmentModalVerificationChecks
{
    static IEnumerable<double> Press(Keyboard keyboard, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); yield return .12;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return .12;
    }
    static void Submit(Button button)
    {
        EventSystem.current.SetSelectedGameObject(button.gameObject);
        ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
    }
    static IEnumerable<object> Capture(string name)
    {
        var run = UsabilityRoundTwoVerification.Capture(name);
        while (run.MoveNext()) yield return run.Current;
    }
    public static IEnumerator Run(MainMenuNavigation navigation, GameDataManager manager, Action<bool, string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(manager.SavePath),
            "Equipment modal checks use the runner's isolated save");
        var service = manager.Tasks;
        check(service.OwnsEquipment("boss1-weapon") && !service.OwnsEquipment("boss1-body")
            && !service.OwnsEquipment("boss1-head"), "Equipment modal fixture has owned weapon and unowned body/head");
        check(navigation.ShowPage(MainMenuPage.CatHome), "Open Cat Home for equipment keyboard checks");
        yield return .2;
        var page = navigation.pages[(int)MainMenuPage.CatHome].GetComponent<EquipmentPageUI>();
        var detail = page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
        var confirmation = page.transform.Find("ConfirmEquipment");
        var confirm = confirmation.Find("Card/Confirm").GetComponent<Button>();
        var cancel = confirmation.Find("Card/Cancel").GetComponent<Button>();
        var launcher = page.GetComponentsInChildren<EquipmentInventoryItemUI>().Single(item => item.name == "Item_boss1-weapon").button;
        var events = EventSystem.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            foreach (int width in new[] { 1920, 1280 })
            {
                UsabilityRoundTwoVerification.Resolution(width, width == 1920 ? 1080 : 720); yield return .3;
                Submit(launcher); yield return .15;
                check(detail.gameObject.activeInHierarchy && events.currentSelectedGameObject == detail.equip.gameObject,
                    "Equipment detail opens with an available action focused at " + width);
                check(detail.upgrade.IsInteractable(), "Fixture can upgrade before disabled-navigation check");
                foreach (var expected in new[] { detail.upgrade, detail.close, detail.equip })
                {
                    foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
                    check(events.currentSelectedGameObject == expected.gameObject, "Detail Tab cycles to " + expected.name);
                }
                foreach (double delay in Press(keyboard, Key.LeftShift, Key.Tab)) yield return delay;
                check(events.currentSelectedGameObject == detail.close.gameObject, "Detail Shift+Tab wraps backwards");
                detail.upgrade.interactable = false; yield return .15;
                events.SetSelectedGameObject(detail.equip.gameObject);
                foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
                check(events.currentSelectedGameObject == detail.close.gameObject, "Detail Tab skips disabled upgrade");
                events.SetSelectedGameObject(launcher.gameObject); yield return .15;
                check(events.currentSelectedGameObject && events.currentSelectedGameObject.transform.IsChildOf(detail.transform),
                    "Detail recovers keyboard focus from background");
                foreach (object delay in Capture("equipment-keyboard-detail-" + width)) yield return delay;
                foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
                check(!detail.gameObject.activeSelf && events.currentSelectedGameObject == launcher.gameObject,
                    "Detail Escape restores its inventory opener at " + width);
            }
            for (int i = 0; i < 3; i++)
            {
                Submit(launcher);
                foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
            }
            check(page.GetComponents<TaskDialogKeyboard>().Length == 1 && !page.GetComponent<TaskDialogKeyboard>().enabled,
                "Repeated equipment open/close retains one inactive keyboard owner");
            Submit(launcher); yield return .1;
            detail.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = Vector2.zero, pressPosition = Vector2.zero });
            check(!detail.gameObject.activeSelf && events.currentSelectedGameObject == launcher.gameObject,
                "Detail backdrop dismissal immediately restores keyboard focus");

            events.SetSelectedGameObject(launcher.gameObject); page.OpenItem("boss1-body"); yield return .15;
            check(detail.equip.IsInteractable(), "Unowned detail can request a mastery claim");
            Submit(detail.equip); yield return .15;
            check(confirmation.gameObject.activeInHierarchy && events.currentSelectedGameObject == cancel.gameObject,
                "Nested claim confirmation focuses safe Cancel action");
            foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
            check(events.currentSelectedGameObject == confirm.gameObject, "Confirmation Tab selects Confirm");
            foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
            check(events.currentSelectedGameObject == cancel.gameObject, "Confirmation Tab remains in top modal");
            events.SetSelectedGameObject(detail.equip.gameObject); yield return .15;
            check(events.currentSelectedGameObject.transform.IsChildOf(confirmation), "Nested confirmation keeps detail from taking focus");
            foreach (object delay in Capture("equipment-keyboard-confirmation-1280")) yield return delay;
            foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
            check(!confirmation.gameObject.activeSelf && detail.gameObject.activeInHierarchy
                && events.currentSelectedGameObject == detail.equip.gameObject, "One Escape closes only confirmation and restores Claim");
            int ownedBefore = service.Snapshot.equipment.Count;
            Submit(detail.equip); detail.equip.onClick.Invoke(); yield return .1;
            using (File.Open(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Submit(confirm); yield return .15;
                check(confirmation.gameObject.activeInHierarchy && !string.IsNullOrEmpty(service.LastError)
                    && service.Snapshot.equipment.Count == ownedBefore, "Failed claim retains confirmation without granting gear");
                foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
                check(events.currentSelectedGameObject == cancel.gameObject, "Failed claim retains top-modal Tab navigation");
                foreach (object delay in Capture("equipment-keyboard-claim-failed-1280")) yield return delay;
                foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
                check(!confirmation.gameObject.activeSelf && detail.gameObject.activeSelf
                    && events.currentSelectedGameObject == detail.equip.gameObject, "Failed claim Escape restores underlying Claim action");
            }
            confirm.onClick.Invoke();
            check(service.Snapshot.equipment.Count == ownedBefore && !confirmation.gameObject.activeSelf,
                "A stale confirmation callback cannot claim after dismissal");
            Submit(detail.equip); Submit(confirm); yield return .2;
            check(service.OwnsEquipment("boss1-body") && !confirmation.gameObject.activeSelf
                && detail.gameObject.activeInHierarchy, "Successful claim closes confirmation and retains actionable detail");
            confirm.onClick.Invoke();
            check(service.Snapshot.equipment.Count == ownedBefore + 1, "Repeated confirmation does not duplicate gear");
            foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
            check(events.currentSelectedGameObject == launcher.gameObject, "Claim success then detail close restores original opener");

            Submit(launcher); yield return .1;
            navigation.ShowPage(MainMenuPage.Dashboard); yield return .1;
            check(!detail.gameObject.activeSelf && !confirmation.gameObject.activeSelf && !page.GetComponent<TaskDialogKeyboard>().enabled,
                "Page closure dismisses equipment layers and releases keyboard ownership");
            navigation.ShowPage(MainMenuPage.CatHome); yield return .1;
            Submit(launcher); foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
            check(page.GetComponents<TaskDialogKeyboard>().Length == 1 && events.currentSelectedGameObject == launcher.gameObject,
                "Re-enabled equipment page restores modal keyboard behavior without duplication");

            check(navigation.ShowPage(MainMenuPage.Boss), "Open Boss for mastery keyboard checks"); yield return .2;
            var boss = navigation.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
            var claimPage = boss.GetComponent<EquipmentPageUI>();
            boss.ShowStages(); yield return .15;
            var claimLauncher = boss.masteryBars[0].transform.parent.GetComponent<Button>();
            var claimModal = claimPage.transform.Find("ItemModal");
            var claimClose = claimModal.Find("Card/Close").GetComponent<Button>();
            foreach (int width in new[] { 1920, 1280 })
            {
                UsabilityRoundTwoVerification.Resolution(width, width == 1920 ? 1080 : 720); yield return .3;
                Submit(claimLauncher); yield return .15;
                check(claimModal.gameObject.activeInHierarchy && events.currentSelectedGameObject == claimClose.gameObject,
                    "Mastery modal opens with Close focused at " + width);
                foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
                check(events.currentSelectedGameObject == claimPage.claimView.buttons[(int)EquipmentSlot.Head].gameObject,
                    "Mastery Tab skips collected/disabled choices");
                foreach (double delay in Press(keyboard, Key.Tab)) yield return delay;
                check(events.currentSelectedGameObject == claimClose.gameObject, "Mastery Tab wraps within available actions");
                foreach (object delay in Capture("equipment-keyboard-mastery-" + width)) yield return delay;
                foreach (double delay in Press(keyboard, Key.Escape)) yield return delay;
                check(!claimModal.gameObject.activeSelf && boss.stagePicker.activeSelf
                    && events.currentSelectedGameObject == claimLauncher.gameObject, "Mastery Escape returns focus to its stage opener");
            }
            boss.closeStages.onClick.Invoke();
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
    }
}
