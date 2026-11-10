using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Called by an existing isolated Play flow. No runner, asset writes or live save access.
public static class TaskContextRetentionVerificationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static TaskFilterDialog Filters(TaskSystemUI owner) =>
        (TaskFilterDialog)typeof(TaskSystemUI).GetField("filters", Hidden).GetValue(owner);
    static void Filter(TaskSystemUI owner, TaskDifficulty? difficulty, params string[] tags)
    {
        owner.filterButton.onClick.Invoke();
        var dialog = Filters(owner);
        dialog.difficultyOptions[difficulty.HasValue ? (int)difficulty.Value + 1 : 0].isOn = true;
        foreach (var toggle in dialog.tagContent.GetComponentsInChildren<Toggle>(true))
            if (toggle != dialog.tagTemplate && toggle.gameObject.activeSelf) toggle.isOn = tags.Contains(toggle.name.Substring(4));
        dialog.apply.onClick.Invoke();
    }
    static void CheckFilter(TaskSystemUI owner, TaskDifficulty? difficulty, string[] tags, Action<bool,string> check)
    {
        owner.filterButton.onClick.Invoke();
        var dialog = Filters(owner);
        check(dialog.gameObject.activeSelf && dialog.difficultyOptions[difficulty.HasValue ? (int)difficulty.Value + 1 : 0].isOn,
            "Task browsing retains the selected difficulty");
        var selected = dialog.tagContent.GetComponentsInChildren<Toggle>(true)
            .Where(toggle => toggle != dialog.tagTemplate && toggle.gameObject.activeSelf && toggle.isOn).Select(toggle => toggle.name.Substring(4));
        check(new HashSet<string>(selected).SetEquals(tags), "Task browsing retains exactly the selected tags");
        dialog.Dismiss();
    }
    static void ClickTab(TaskSystemUI owner, TaskTab tab, Action<bool,string> check)
        => ClickBrowseControl(owner, owner.tabButtons[(int)tab], tab + " tab", check);
    static void ClickBrowseControl(TaskSystemUI owner, Button button, string label, Action<bool,string> check)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        var handler = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        check(handler && handler.transform.IsChildOf(button.transform), "Pointer raycast reaches the " + label);
        // Exercise both possible Update orders around the real raycast/dispatch.
        var outsideClick = typeof(TaskSystemUI).GetMethod("CaptureOutsideClick", Hidden);
        outsideClick.Invoke(owner, new object[] { hits[0].gameObject.transform });
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        outsideClick.Invoke(owner, new object[] { hits[0].gameObject.transform });
    }
    public static void FirstEntry(TaskSystemUI owner, Action<bool,string> check) =>
        check(owner.CurrentTab == owner.Service.Settings.DefaultTaskTab, "First Tasks entry uses the saved default tab");

    public static IEnumerable<double> Run(TaskSystemUI owner, MainMenuNavigation nav, Action<bool,string> check)
    {
        var manager = GameDataManager.EnsureInstance();
        var isolatedRoot = Path.GetFullPath("Library") + Path.DirectorySeparatorChar;
        check(Path.GetFullPath(manager.SavePath).StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase),
            "Context-retention fixtures use an isolated Library save");
        check(!owner.IsEditing && !owner.HasTaskModal, "Context-retention checks start without a pending editor or modal");
        check(nav.ShowPage(MainMenuPage.Tasks), "Tasks opens for context-retention checks");
        if (owner.calendar && owner.calendar.Active) owner.calendar.SetMode(TaskDisplayMode.List);
        yield return .2;
        var service = owner.Service;
        string token = Guid.NewGuid().ToString("N"), prefix = "Browse " + token.Substring(0, 6);
        string tagA = token + "-a", tagB = token + "-b", special = token + "-special";
        manager.PlayerData.tags.Add(new QuestTag { id = tagA, name = prefix + " A" });
        manager.PlayerData.tags.Add(new QuestTag { id = tagB, name = prefix + " B" });
        string today = TaskService.DateKey(service.LocalNow);
        // Fixture construction matches the existing long-list verifier; no per-row UI generation.
        for (int index = 0; index < 48; index++) {
            manager.PlayerData.tasks.Add(new QuestTask { id = token + "-todo-" + index,
                title = prefix + " Todo " + index.ToString("D2"), scheduled = true, date = today,
                difficulty = TaskDifficulty.Normal, tagIds = new List<string> { tagA } });
            manager.PlayerData.tasks.Add(new QuestTask { id = token + "-daily-" + index,
                title = prefix + " Daily " + index.ToString("D2"), daily = true, scheduled = true, date = today,
                repeatInterval = 1, difficulty = TaskDifficulty.Easy, tagIds = new List<string> { tagB } });
        }
        manager.PlayerData.tasks.Add(new QuestTask { id = special, title = prefix + " Todo special",
            scheduled = true, date = today, difficulty = TaskDifficulty.Easy, tagIds = new List<string> { tagA } });
        manager.SaveData();
        owner.Refresh();
        var scroll = owner.listContent.GetComponentInParent<ScrollRect>();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);

        foreach (TaskTab tab in Enum.GetValues(typeof(TaskTab))) {
            owner.SelectTab(tab); owner.search.text = prefix + " " + tab;
        }
        foreach (TaskTab tab in Enum.GetValues(typeof(TaskTab))) {
            owner.SelectTab(tab);
            check(owner.search.text == prefix + " " + tab, "Each tab keeps its own search: " + tab);
        }
        owner.SelectTab(TaskTab.Todo); owner.search.text = prefix + " Todo";
        Filter(owner, TaskDifficulty.Normal, tagA);
        string todoSelected = token + "-todo-20", dailySelected = token + "-daily-12";
        owner.SelectTask(todoSelected); yield return .2;
        scroll.verticalNormalizedPosition = .38f; scroll.StopMovement(); yield return .3;
        float todoScroll = scroll.verticalNormalizedPosition;
        owner.SelectTab(TaskTab.Daily); owner.search.text = prefix + " Daily";
        Filter(owner, TaskDifficulty.Easy, tagB);
        owner.SelectTask(dailySelected); yield return .2;
        scroll.verticalNormalizedPosition = .72f; scroll.StopMovement(); yield return .3;
        float dailyScroll = scroll.verticalNormalizedPosition;
        ClickTab(owner, TaskTab.Todo, check); yield return .3;
        check(owner.search.text == prefix + " Todo" && owner.SelectedKey == todoSelected,
            "Returning to Todo retains its search and selected task");
        check(Mathf.Abs(scroll.verticalNormalizedPosition - todoScroll) < .015f,
            "Returning to Todo restores its scroll position");
        CheckFilter(owner, TaskDifficulty.Normal, new[] { tagA }, check);
        ClickTab(owner, TaskTab.Todo, check); yield return .2;
        check(owner.search.text == prefix + " Todo" && owner.SelectedKey == todoSelected
            && Mathf.Abs(scroll.verticalNormalizedPosition - todoScroll) < .015f,
            "Clicking the current tab preserves its browsing context");
        var settingsBefore = service.Settings;
        try {
            var changed = service.Settings; changed.defaultTaskTab = TaskTab.Today;
            check(service.SaveSettings(changed), "Change default tab in isolated settings");
            check(nav.ShowPage(MainMenuPage.Dashboard) && nav.ShowPage(MainMenuPage.Tasks), "Leave and return to Tasks");
            yield return .3;
            check(owner.CurrentTab == TaskTab.Todo && owner.search.text == prefix + " Todo" && owner.SelectedKey == todoSelected,
                "Returning to Tasks keeps the current tab instead of reapplying Settings default");
            check(Mathf.Abs(scroll.verticalNormalizedPosition - todoScroll) < .015f, "Page return retains list scroll");
        } finally { check(service.SaveSettings(settingsBefore), "Restore isolated default tab setting"); }

        owner.SelectTab(TaskTab.Daily); owner.SelectTab(TaskTab.Todo); owner.SelectTab(TaskTab.Daily);
        yield return .3;
        check(owner.search.text == prefix + " Daily" && owner.SelectedKey == dailySelected
            && Mathf.Abs(scroll.verticalNormalizedPosition - dailyScroll) < .015f,
            "Rapid tab changes preserve the final tab's own selection and scroll");
        CheckFilter(owner, TaskDifficulty.Easy, new[] { tagB }, check);
        if (owner.calendar) {
            var calendar = owner.calendar;
            ClickBrowseControl(owner, calendar.calendarMode, "Calendar view button", check); yield return .2;
            check(calendar.Active && owner.SelectedKey == dailySelected,
                "Mouse switching to Calendar does not clear the retained List selection in LateUpdate");
            calendar.ChangeMonth(1);
            var month = calendar.Month;
            calendar.search.text = prefix + " calendar";
            check(nav.ShowPage(MainMenuPage.Dashboard) && nav.ShowPage(MainMenuPage.Tasks), "Leave and return with Calendar active");
            yield return .2;
            check(calendar.Active && calendar.Month == month && calendar.search.text == prefix + " calendar",
                "Calendar keeps its independent month and search on page return");
            ClickBrowseControl(owner, calendar.listMode, "List view button", check); yield return .3;
            check(owner.CurrentTab == TaskTab.Daily && owner.search.text == prefix + " Daily" && owner.SelectedKey == dailySelected
                && Mathf.Abs(scroll.verticalNormalizedPosition - dailyScroll) < .015f,
                "Mouse switching from Calendar to List restores the retained list tab context");
            CheckFilter(owner, TaskDifficulty.Easy, new[] { tagB }, check);

            ClickBrowseControl(owner, calendar.calendarMode, "Calendar view button before editing another task", check); yield return .2;
            calendar.ClearFilters();
            var date = service.LocalNow.Date;
            calendar.SelectDate(date);
            string editedId = token + "-todo-5", description = "Saved from Calendar while List keeps its own selection.";
            var item = service.QueryCalendar(date, date).days[date].items.First(entry => entry.view.task.id == editedId);
            calendar.OpenItem(item); owner.CalendarEdit(item);
            check(owner.IsEditing && calendar.Active, "Calendar opens an editor for a different task from the retained Daily selection");
            owner.editor.description.text = description;
            owner.editor.transform.Find("Footer/Actions/SaveChangesButton").GetComponent<Button>().onClick.Invoke();
            yield return .2;
            var selectedCalendarItem = (CalendarTaskItem)typeof(TaskCalendarUI).GetField("selected", Hidden).GetValue(calendar);
            check(!owner.IsEditing && service.Snapshot.tasks.Single(task => task.id == editedId).description == description,
                "Calendar SaveDraft commits the edited task through the existing save button");
            check(calendar.DrawerOpen && calendar.detailRoot.gameObject.activeInHierarchy && selectedCalendarItem?.view.task.id == editedId,
                "Calendar SaveDraft keeps its own drawer on the saved task");
            check(owner.SelectedKey == dailySelected, "Calendar SaveDraft does not overwrite the independent List selection");
            check(nav.ShowPage(MainMenuPage.Dashboard) && nav.ShowPage(MainMenuPage.Tasks),
                "Leave and return after saving a different task in Calendar");
            yield return .2;
            check(calendar.Active, "Page return after Calendar SaveDraft retains Calendar mode");
            ClickBrowseControl(owner, calendar.listMode, "List view button after Calendar save", check); yield return .3;
            check(owner.CurrentTab == TaskTab.Daily && owner.search.text == prefix + " Daily" && owner.SelectedKey == dailySelected
                && Mathf.Abs(scroll.verticalNormalizedPosition - dailyScroll) < .015f,
                "Calendar save and page disable preserve the prior List tab, search, selection and scroll");
            CheckFilter(owner, TaskDifficulty.Easy, new[] { tagB }, check);
        }

        owner.SelectTab(TaskTab.Todo); owner.SelectTask(special); yield return .2;
        check(owner.SelectedKey == special && owner.search.text == prefix + " Todo", "Explicit task target keeps matching search");
        CheckFilter(owner, null, new[] { tagA }, check);
        owner.search.text = "No matching target " + token;
        Filter(owner, TaskDifficulty.Hard, tagA, tagB);
        owner.SelectTask(special); yield return .2;
        check(owner.SelectedKey == special && owner.search.text == "", "Explicit task target clears only its blocking search");
        CheckFilter(owner, null, new[] { tagA }, check);
        owner.SelectTab(TaskTab.Daily); yield return .2;
        check(owner.search.text == prefix + " Daily" && owner.SelectedKey == dailySelected, "Deep-link filter changes do not affect another tab");
        CheckFilter(owner, TaskDifficulty.Easy, new[] { tagB }, check);

        check(service.Delete(special), "Delete inactive-tab selection through the service");
        check(service.DeleteTag(tagA), "Delete inactive-tab filter tag through the service");
        owner.SelectTab(TaskTab.Todo); yield return .2;
        check(owner.SelectedKey == null && !owner.detailsRoot.gameObject.activeSelf, "Deleted selection is not restored from tab memory");
        CheckFilter(owner, null, Array.Empty<string>(), check);
        owner.SelectTab(TaskTab.Daily); owner.BeginAdd();
        owner.QuickAddInput.text = prefix + " quick add";
        owner.SelectTab(TaskTab.Todo); yield return .2;
        check(service.Snapshot.tasks.Count(task => task.title == prefix + " quick add" && task.daily) == 1 && !owner.QuickAddInput,
            "Tab switching still saves quick-add exactly once with its originating Daily defaults");
    }
}
