using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Called only by the isolated remaining-fixes runner; never opens a second Editor.
public static class RemainingEquipmentVerificationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static IPlayerDataStore Store(GameDataManager manager) =>
        (IPlayerDataStore)typeof(GameDataManager).GetField("store", Hidden).GetValue(manager);
    static void Save(GameDataManager manager, Action<bool,string> check)
        => check(manager.TrySaveData(out var error), "Save isolated equipment fixture: " + error);
    static IEnumerable<object> Capture(string name)
    {
        var run = RemainingFixesVerification.Capture(name);
        while (run.MoveNext()) yield return run.Current;
    }
    static void Measure(string label, Func<object> operation, string file)
    {
        for (int i = 0; i < 3; i++) GC.KeepAlive(operation());
        var milliseconds = new List<double>(24);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long heap = GC.GetTotalMemory(false);
        var total = Stopwatch.StartNew();
        for (int i = 0; i < 24 && total.Elapsed.TotalSeconds < 8; i++) {
            var start = Stopwatch.GetTimestamp();
            GC.KeepAlive(operation());
            milliseconds.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }
        long endingBytes = GC.GetAllocatedBytesForCurrentThread();
        bool allocationAvailable = bytes != 0 || endingBytes != 0;
        long allocated = allocationAvailable ? (endingBytes - bytes) / milliseconds.Count : -1;
        long retainedDelta = GC.GetTotalMemory(false) - heap;
        milliseconds.Sort();
        File.AppendAllText(file, $"{label}: n={milliseconds.Count}, median_ms={milliseconds[milliseconds.Count/2]:F4}, p95_ms={milliseconds[(int)Math.Floor((milliseconds.Count-1)*.95)]:F4}, allocation_counter_available={allocationAvailable}, managed_bytes_per_call={allocated}, retained_heap_delta_NOT_allocated_bytes={retainedDelta}\n");
    }
    public static IEnumerator Run(MainMenuNavigation nav, GameDataManager manager, bool before, string output, Action<bool,string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(manager.SavePath)
            && Path.GetFullPath(manager.SavePath).StartsWith(Path.GetFullPath("Library") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Remaining equipment checks use the runner's isolated Library save");
        var original = PlayerDataStore.Copy(manager.PlayerData);
        var previousPage = nav.CurrentPage;
        var resize = nav.GetComponentInChildren<ResisesSideMenuInLayoutGroup>(true);
        if (!resize) resize = UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expanded = (Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander", Hidden).GetValue(resize);
        bool wasExpanded = expanded.isOn;
        var page = nav.pages[(int)MainMenuPage.CatHome].GetComponent<EquipmentPageUI>();
        var service = manager.Tasks;
        try {
            manager.PlayerData.equipment.Clear();
            manager.PlayerData.equippedWeaponId = manager.PlayerData.equippedBodyOutfitId = manager.PlayerData.equippedHeadAccessoryId = null;
            manager.PlayerData.equipmentUnlocked = false;
            manager.PlayerData.rewardDays.Clear();
            Save(manager, check);
            foreach (int width in new[] { 1920, 1280 }) {
                RemainingFixesVerification.Resolution(width, width == 1920 ? 1080 : 720); yield return .3;
                check(nav.ShowPage(MainMenuPage.Dashboard), "Open Dashboard for stat readability capture"); yield return .3;
                foreach (var delay in Capture("equipment-dashboard-stats-" + width)) yield return delay;
                check(nav.ShowPage(MainMenuPage.CatHome), "Open empty Cat Home"); page.Refresh(); yield return .3;
                var empty = page.GetComponentInChildren<EquipmentInventoryUI>();
                if (!before) {
                    CheckEmptyState(empty, 1, "", "Daily Rewards", check);
                    check(!empty.autoSelect.IsInteractable(),
                        "Locked empty inventory offers Daily Rewards and disables Auto Select at " + width);
                    check(!empty.backpack.GetComponentsInChildren<Transform>().Any(child => child.name == "EmptySlot"),
                        "Empty inventory has no misleading add-slot grid");
                }
                foreach (var delay in Capture("equipment-empty-locked-" + width)) yield return delay;
                manager.PlayerData.equipmentUnlocked = true; Save(manager, check); page.Refresh(); yield return .2;
                if (!before) CheckEmptyState(empty, 4,
                    Mathf.Clamp(manager.PlayerData.bossProgress.First(progress=>progress.bossId==1).wins,0,5)+" / 5 wins", "Boss", check);
                foreach (var delay in Capture("equipment-empty-unlocked-" + width)) yield return delay;
                manager.PlayerData.equipmentUnlocked = false; Save(manager, check);
            }
            var navigation = NavigationHints(nav, expanded, before, output, check);
            try { while (navigation.MoveNext()) yield return navigation.Current; }
            finally { (navigation as IDisposable)?.Dispose(); }
            expanded.isOn = wasExpanded;
            RemainingFixesVerification.Resolution(1280, 720); yield return .3;
            manager.PlayerData.equipmentUnlocked = true;
            manager.PlayerData.fishCoins = 500;
            foreach (var definition in EquipmentRules.Catalog)
                manager.PlayerData.equipment.Add(new EquipmentItem { id = definition.Id, level = 1 });
            Save(manager, check); page.Refresh(); yield return .3;
            foreach (var delay in Capture("equipment-auto-select-before")) yield return delay;
            if (!before) {
                var checks = After(nav, manager, page, output, check);
                while (checks.MoveNext()) yield return checks.Current;
            }
            var item = page.GetComponentsInChildren<EquipmentInventoryItemUI>().First(cell => cell.name == "Item_boss1-weapon");
            var hover = item.GetComponent<EquipmentHoverTarget>();
            var pointerEvent = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, item.transform.position) };
            // Performance fixture lives only in this isolated store and is never saved.
            // No yield occurs until all temporary history lists have been restored.
            var data = manager.PlayerData;
            var history = data.history; var focus = data.focusSessions; var battles = data.battles;
            try {
                data.history = new List<TaskCompletion>(); data.focusSessions = new List<FocusSession>(); data.battles = new List<BattleAttempt>();
                for (int i = 0; i < 3000; i++) {
                    string id = "equipment-perf-" + i, day = TaskService.DateKey(service.LocalNow.Date.AddDays(-i/3));
                    data.history.Add(new TaskCompletion { id = id, taskId = id, day = day, completedLocalDay = day,
                        snapshot = new QuestTask { id = id, title = "Synthetic long-term completed task " + i, description = new string('x', 96) } });
                    data.focusSessions.Add(new FocusSession { id = id, status = FocusSessionStatus.Completed, eligibleMinutes = 25, rewardDay = day });
                    data.battles.Add(new BattleAttempt { id = id, bossId = 1, outcome = BattleOutcome.Victory });
                }
                string file = Path.Combine(output, "equipment-performance.txt");
                File.AppendAllText(file, $"{DateTime.UtcNow:O} {(before ? "before" : "after")} Unity {Application.unityVersion}; 3000 completion snapshots + 3000 Focus receipts + 3000 settled battles; warmup=3; max=24 samples/8 seconds. Main-thread call CPU and managed allocation only, not frame time or GPU.\n");
                Measure("full snapshot", () => service.Snapshot, file);
                var narrow = typeof(TaskService).GetProperty("EquipmentSnapshot");
                if (narrow != null) {
                    var snapshot = (PlayerData)narrow.GetValue(service);
                    check(snapshot.history.Count == 0 && snapshot.focusSessions.Count == 0 && snapshot.battles.Count == 0
                        && snapshot.equipment.Count == data.equipment.Count, "Equipment projection excludes all unrelated histories");
                    Measure("equipment snapshot", () => narrow.GetValue(service), file);
                }
                Measure("inventory pointer enter+exit", () => { hover.OnPointerEnter(pointerEvent); hover.OnPointerExit(pointerEvent); return null; }, file);
                check(data.history.Count == 3000 && data.focusSessions.Count == 3000 && data.battles.Count == 3000,
                    "Equipment hover leaves all synthetic history untouched");
            } finally { data.history = history; data.focusSessions = focus; data.battles = battles; page.HideHover(); }
        } finally {
            check(Store(manager).TrySave(original, out var error), "Restore isolated equipment fixture: " + error);
            expanded.isOn = wasExpanded;
            page.HideHover(); nav.ShowPage(previousPage);
        }
    }
    // A narrow style check reuses the full suite's interaction/pixel assertions.
    // The before/after groups both validate behavior: only the authored artwork changes.
    public static IEnumerator RunNavigation(MainMenuNavigation nav, GameDataManager manager, bool before, string output, Action<bool,string> check)
    {
        string isolated = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated) && Path.GetFullPath(isolated) == Path.GetFullPath(manager.SavePath)
            && Path.GetFullPath(manager.SavePath).StartsWith(Path.GetFullPath("Library") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Navigation " + (before ? "before" : "after") + " checks use the runner's isolated Library save");
        var original = PlayerDataStore.Copy(manager.PlayerData);
        var previousPage = nav.CurrentPage;
        var previousSelection = EventSystem.current.currentSelectedGameObject;
        var resize = nav.GetComponentInChildren<ResisesSideMenuInLayoutGroup>(true);
        if (!resize) resize = UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expanded = (Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander", Hidden).GetValue(resize);
        bool wasExpanded = expanded.isOn;
        try {
            manager.PlayerData.experience = 10000;
            check(manager.TrySaveData(out var error), "Save isolated unlocked navigation fixture: " + error);
            RefreshNavigationLocks(nav);
            check(nav.ShowPage(MainMenuPage.CatHome), "Open the existing page behind navigation hints");
            EventSystem.current.SetSelectedGameObject(null);
            var normal = NavigationHints(nav, expanded, false, output, check, true);
            try { while (normal.MoveNext()) yield return normal.Current; }
            finally { (normal as IDisposable)?.Dispose(); }
            manager.PlayerData.experience = 0;
            check(manager.TrySaveData(out error), "Save isolated locked navigation fixture: " + error);
            RefreshNavigationLocks(nav);
            var bossButton = nav.buttons[(int)MainMenuPage.Boss];
            check(bossButton.GetComponent<FeatureLockUI>().Locked, "Low-level navigation fixture actually locks Boss");
            expanded.isOn = false; yield return .4;
            var locked = LockedBossHints(bossButton, output, check);
            try { while (locked.MoveNext()) yield return locked.Current; }
            finally { (locked as IDisposable)?.Dispose(); }
        } finally {
            try { check(Store(manager).TrySave(original, out var error), "Restore isolated navigation fixture: " + error); }
            finally {
                EventSystem.current.SetSelectedGameObject(null);
                var pointer = new PointerEventData(EventSystem.current);
                foreach (var button in nav.buttons)
                    if (button) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                expanded.isOn = wasExpanded;
                RefreshNavigationLocks(nav);
                nav.ShowPage(previousPage);
                if (previousSelection && previousSelection.activeInHierarchy)
                    EventSystem.current.SetSelectedGameObject(previousSelection);
            }
        }
    }
    static void RefreshNavigationLocks(MainMenuNavigation nav)
    {
        foreach (var button in nav.buttons) {
            var gate = button.GetComponent<FeatureLockUI>();
            if (gate) typeof(FeatureLockUI).GetMethod("Refresh", Hidden).Invoke(gate, null);
        }
    }
    static readonly MainMenuPage[] SidebarCaptureTargets = { MainMenuPage.Tasks, MainMenuPage.CatHome, MainMenuPage.Boss };
    static Rect NavigationViewport(Component owner) => owner.GetComponentInParent<Canvas>().rootCanvas.pixelRect;
    static void CheckNavigationResolution(Component owner, Vector2Int size, string context, Action<bool,string> check)
    {
        var viewport = NavigationViewport(owner);
        check(viewport.width == size.x && viewport.height == size.y,
            "Navigation resolution at " + context + ": requested " + size.x + "x" + size.y + "; root Canvas="
            + viewport.width + "x" + viewport.height + "; " + RemainingFixesVerification.ResolutionStatus);
    }
    static IEnumerable<object> NavigationResolution(Component owner, Vector2Int size, Action<bool,string> check)
    {
        RemainingFixesVerification.Resolution(size.x, size.y);
        double deadline = EditorApplication.timeSinceStartup + 3, matchedSince = -1;
        int matchedFrame = -1;
        bool stable = false;
        while (EditorApplication.timeSinceStartup < deadline) {
            var viewport = NavigationViewport(owner);
            if (viewport.width == size.x && viewport.height == size.y) {
                if (matchedSince < 0) { matchedSince = EditorApplication.timeSinceStartup; matchedFrame = Time.frameCount; }
                if (Time.frameCount != matchedFrame && EditorApplication.timeSinceStartup - matchedSince >= .15) { stable = true; break; }
            } else matchedSince = -1;
            EditorApplication.QueuePlayerLoopUpdate();
            yield return .05;
        }
        CheckNavigationResolution(owner, size, "fixed-size startup", check);
        check(stable, "Navigation resolution remained stable across rendered frames within 3 seconds: requested "
            + size.x + "x" + size.y + "; " + RemainingFixesVerification.ResolutionStatus);
        Canvas.ForceUpdateCanvases();
        yield return .05;
        CheckNavigationResolution(owner, size, "settled layout", check);
    }
    static IEnumerable<object> CaptureNavigation(string name, Vector2Int size, string output, Action<bool,string> check)
    {
        foreach (var delay in Capture(name)) yield return delay;
        // Read the actual PNG IHDR, not Screen dimensions queried from Editor.update.
        // The latter can reflect an Editor view even when the captured Game View is fixed-size.
        byte[] header;
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(output, name + ".png")))) header = reader.ReadBytes(24);
        check(header.Length == 24 && header.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && header[12] == 'I' && header[13] == 'H' && header[14] == 'D' && header[15] == 'R',
            "Navigation screenshot contains a PNG IHDR: " + name);
        int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        check(width == size.x && height == size.y, "Actual navigation PNG matches requested " + size.x + "x" + size.y
            + ": " + name + " is " + width + "x" + height);
    }
    static IEnumerator NavigationHints(MainMenuNavigation nav, Toggle expanded, bool before, string output, Action<bool,string> check, bool allHints = false)
    {
        foreach (var size in before ? new[] { new Vector2Int(1280, 720) } : SidebarSizes) {
            foreach (var delay in NavigationResolution(nav, size, check)) yield return delay;
            string suffix = before ? "" : "-" + size.x + "x" + size.y;
            expanded.isOn = false; yield return .4;
            foreach (var target in allHints ? (MainMenuPage[])Enum.GetValues(typeof(MainMenuPage)) : SidebarCaptureTargets) {
                var button = nav.buttons[(int)target];
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position) };
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return allHints ? .1 : .3;
                CheckNavigationResolution(nav, size, target + " hover", check);
                if (!before) CheckHint(button, target, check);
                bool captureTarget = !allHints || SidebarCaptureTargets.Contains(target);
                if (captureTarget) {
                    foreach (var delay in CaptureNavigation("sidebar-hover-" + target + suffix, size, output, check)) yield return delay;
                    CheckNavigationResolution(nav, size, target + " hover screenshot", check);
                    if (!before) CheckHintPixels(button, Path.Combine(output, "sidebar-hover-" + target + suffix + ".png"), check);
                }
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(button.gameObject); yield return allHints ? .1 : .2;
                CheckNavigationResolution(nav, size, target + " keyboard focus", check);
                if (!before) CheckHint(button, target, check);
                if (captureTarget) {
                    foreach (var delay in CaptureNavigation("sidebar-keyboard-" + target + suffix, size, output, check)) yield return delay;
                    CheckNavigationResolution(nav, size, target + " keyboard screenshot", check);
                    if (!before) CheckHintPixels(button, Path.Combine(output, "sidebar-keyboard-" + target + suffix + ".png"), check);
                }
                EventSystem.current.SetSelectedGameObject(null);
            }
            expanded.isOn = true; yield return .4;
            var tasksButton = nav.buttons[(int)MainMenuPage.Tasks];
            EventSystem.current.SetSelectedGameObject(tasksButton.gameObject); yield return .2;
            CheckNavigationResolution(nav, size, "expanded sidebar", check);
            if (!before) {
                foreach (var button in nav.buttons) {
                    var hint = (GameObject)typeof(SidebarNavigationButton).GetField("navigationTooltip", Hidden).GetValue(button);
                    check(hint && !hint.activeSelf, "Expanded sidebar uses its existing visible page label at " + size);
                    CheckButtonHit(button, check);
                }
            }
            foreach (var delay in CaptureNavigation("sidebar-expanded" + suffix, size, output, check)) yield return delay;
            CheckNavigationResolution(nav, size, "expanded screenshot", check);
            if (!before) {
                expanded.isOn = false; yield return .4;
                CheckNavigationResolution(nav, size, "collapsed keyboard focus", check);
                CheckHint(tasksButton, MainMenuPage.Tasks, check);
                var hint = (GameObject)typeof(SidebarNavigationButton).GetField("navigationTooltip", Hidden).GetValue(tasksButton);
                tasksButton.gameObject.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
                check(!hint.activeSelf, "Application focus loss clears navigation hint at " + size);
            }
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
    static IEnumerator LockedBossHints(Button bossButton, string output, Action<bool,string> check)
    {
        foreach (var size in SidebarSizes) {
            foreach (var delay in NavigationResolution(bossButton, size, check)) yield return delay;
            var pointer = new PointerEventData(EventSystem.current) { position=RectTransformUtility.WorldToScreenPoint(null,bossButton.transform.position) };
            ExecuteEvents.Execute(bossButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return .2;
            CheckNavigationResolution(bossButton, size, "locked Boss hover", check);
            CheckHint(bossButton, MainMenuPage.Boss, check);
            foreach (var delay in CaptureNavigation("sidebar-locked-boss-name-and-reason-" + size.x + "x" + size.y, size, output, check)) yield return delay;
            CheckNavigationResolution(bossButton, size, "locked Boss screenshot", check);
            CheckHintPixels(bossButton, Path.Combine(output, "sidebar-locked-boss-name-and-reason-" + size.x + "x" + size.y + ".png"), check);
            ExecuteEvents.Execute(bossButton.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            EventSystem.current.SetSelectedGameObject(bossButton.gameObject); yield return .1;
            CheckNavigationResolution(bossButton, size, "locked Boss keyboard focus", check);
            CheckHint(bossButton, MainMenuPage.Boss, check);
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
    static readonly Vector2Int[] SidebarSizes = {
        new Vector2Int(1024, 768), new Vector2Int(1280, 720),
        new Vector2Int(1920, 1080), new Vector2Int(2560, 1080)
    };
    static List<RaycastResult> Raycasts(Vector2 point)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        return hits;
    }
    static void CheckButtonHit(Button button, Action<bool,string> check)
    {
        var viewport = NavigationViewport(button);
        var rect = (RectTransform)button.transform;
        var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var hits = Raycasts(point);
        check(point.x >= viewport.xMin && point.y >= viewport.yMin && point.x <= viewport.xMax && point.y <= viewport.yMax
            && hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform),
            "Sidebar button remains inside the viewport and is the top pointer target: " + button.name + " at " + viewport.width + "x" + viewport.height);
    }
    static void CheckHint(Button button, MainMenuPage target, Action<bool,string> check)
    {
        var viewport = NavigationViewport(button);
        var gate = button.GetComponent<FeatureLockUI>();
        var nameHint = (GameObject)typeof(SidebarNavigationButton).GetField("navigationTooltip", Hidden).GetValue(button);
        var hint = gate && gate.Locked ? gate.tooltip : nameHint;
        string expected = target == MainMenuPage.CatHome ? "Cat Home" : target.ToString();
        check(hint && hint.activeInHierarchy && hint.GetComponentInChildren<TMPro.TMP_Text>().text.Contains(expected),
            "Collapsed navigation identifies " + expected + " for hover/keyboard focus");
        check(hint.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget), "Navigation hint does not intercept pointer input");
        if (gate && gate.Locked) check(!nameHint.activeSelf && gate.reason.text.Contains("Unlocks at Lv."), "Locked navigation shows one name-and-reason hint");
        var corners = new Vector3[4]; ((RectTransform)hint.transform).GetWorldCorners(corners);
        check(corners.All(corner => { var point=RectTransformUtility.WorldToScreenPoint(null,corner); return point.x>=viewport.xMin && point.y>=viewport.yMin && point.x<=viewport.xMax && point.y<=viewport.yMax; }),
            "Navigation hint stays within the rendered window at " + viewport.width + "x" + viewport.height);
        // Compare actual event-system hits with and without the visible hint. This
        // catches a raycaster/CanvasGroup obstruction beyond individual graphic flags.
        var rect = (RectTransform)hint.transform;
        var centre = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var visibleHits = Raycasts(centre);
        List<RaycastResult> hiddenHits;
        hint.SetActive(false);
        try { hiddenHits = Raycasts(centre); }
        finally { hint.SetActive(true); }
        check(visibleHits.All(hit => !hit.gameObject.transform.IsChildOf(hint.transform))
            && visibleHits.Select(hit => hit.gameObject).SequenceEqual(hiddenHits.Select(hit => hit.gameObject)),
            "Visible navigation hint preserves background pointer targets for " + expected + " at " + viewport.width + "x" + viewport.height);
        CheckButtonHit(button, check);
    }
    static void CheckHintPixels(Button button, string file, Action<bool,string> check)
    {
        var gate = button.GetComponent<FeatureLockUI>();
        var hint = gate && gate.Locked ? gate.tooltip : (GameObject)typeof(SidebarNavigationButton).GetField("navigationTooltip", Hidden).GetValue(button);
        var canvas = hint.GetComponent<Canvas>();
        check(canvas.overrideSorting && hint.GetComponentsInChildren<MaskableGraphic>().All(graphic => !graphic.maskable && !graphic.canvasRenderer.cull),
            "Rendered sidebar hint escapes the button mask and is not culled: " + Path.GetFileName(file));
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try {
            check(texture.LoadImage(File.ReadAllBytes(file)), "Load actual sidebar screenshot for visible-pixel verification");
            var corners = new Vector3[4]; ((RectTransform)hint.transform).GetWorldCorners(corners);
            var lower = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var upper = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            // The inner card must contain both its light background and dark text.
            // This rejects the previous active-but-invisible hint over page/preview UI.
            int x0 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(lower.x, upper.x, .08f)), 0, texture.width - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(lower.x, upper.x, .92f)), x0 + 1, texture.width);
            int y0 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(lower.y, upper.y, .12f)), 0, texture.height - 1);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(lower.y, upper.y, .88f)), y0 + 1, texture.height);
            int pale = 0, ink = 0, total = (x1 - x0) * (y1 - y0);
            var pixels = texture.GetPixels32(); Color expected = TaskUIBuilder.Ink;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) {
                Color color = pixels[y * texture.width + x];
                if (color.r > .87f && color.g > .87f && color.b > .87f) pale++;
                float dr = color.r - expected.r, dg = color.g - expected.g, db = color.b - expected.b;
                if (dr * dr + dg * dg + db * db < .025f) ink++;
            }
            string report = Path.Combine(Path.GetDirectoryName(file), "sidebar-visibility.csv");
            if (!File.Exists(report)) File.WriteAllText(report, "screenshot,innerCardPixels,lightPixels,inkPixels\n");
            File.AppendAllText(report,
                Path.GetFileName(file) + "," + total + "," + pale + "," + ink + "\n");
            check(total > 0 && pale > total * .55f && ink > total * .003f,
                "Actual screenshot contains the visible hint card and label: " + Path.GetFileName(file) + " (light=" + pale + ", ink=" + ink + ", total=" + total + ")");
        } finally { UnityEngine.Object.Destroy(texture); }
    }
    static void CheckEmptyState(EquipmentInventoryUI inventory,int icon,string threshold,string action,Action<bool,string> check)
    {
        check(inventory.emptyState && inventory.emptyState.activeInHierarchy && inventory.emptyAction.IsInteractable(), "Empty equipment keeps an actionable next step");
        check(inventory.emptyIcon && inventory.emptyIcon.gameObject.activeInHierarchy && inventory.emptyStateIcons.Length==5
            && inventory.emptyIcon.sprite && inventory.emptyIcon.sprite==inventory.emptyStateIcons[icon] && !inventory.emptyIcon.raycastTarget,
            "Empty equipment displays its authored destination icon: "+action);
        check(inventory.emptyIcon.color==(icon==0||icon==2?TaskUIBuilder.Ink:Color.white),
            "White All/Lock pictograms keep contrast while full-color destination artwork keeps its colors");
        check(!inventory.emptyMessage.gameObject.activeSelf && inventory.emptyHeading.text==threshold
            && inventory.emptyHeading.gameObject.activeSelf==!string.IsNullOrEmpty(threshold) && inventory.emptyActionLabel.text==action,
            "Empty equipment uses only a necessary threshold and its action: "+action);
        inventory.emptyActionLabel.ForceMeshUpdate();
        if(inventory.emptyHeading.gameObject.activeInHierarchy)inventory.emptyHeading.ForceMeshUpdate();
        check(!inventory.emptyActionLabel.isTextOverflowing && (!inventory.emptyHeading.gameObject.activeInHierarchy || !inventory.emptyHeading.isTextOverflowing),
            "Empty equipment action and short threshold fit their existing authored labels");
    }
    static IEnumerable<object> CaptureEmptyBranch(EquipmentInventoryUI inventory,int icon,string threshold,string action,string name,Action<bool,string> check)
    {
        foreach(int width in new[]{1920,1280}) {
            int height=width==1920?1080:720;
            RemainingFixesVerification.Resolution(width,height);yield return .3;
            var viewport=inventory.GetComponentInParent<Canvas>().rootCanvas.pixelRect;
            check(viewport.width==width&&viewport.height==height,"Empty branch actual Canvas matches capture size: "+name+" "+width);
            CheckEmptyState(inventory,icon,threshold,action,check);
            foreach(var wait in Capture(name+"-"+width))yield return wait;
        }
    }
    static void CheckSelectionFeedback(EquipmentInventoryUI inventory,bool changed,double powerDelta,Action<bool,string> check)
    {
        check(inventory.autoSelectFeedback && inventory.autoSelectFeedback.activeInHierarchy && inventory.autoSelectPowerDelta,
            "Committed Auto Select exposes its authored check feedback");
        var change=inventory.autoSelectPowerDelta.transform.parent.gameObject;
        string expected=changed?powerDelta.ToString("+0.##;-0.##;0",System.Globalization.CultureInfo.InvariantCulture):"";
        check(change.activeSelf==changed && inventory.autoSelectPowerDelta.text==expected,
            changed?"Auto Select shows the actual committed Power difference beside its icon":"Unchanged Auto Select shows only the check, without a stale Power change");
        check(!inventory.notice.gameObject.activeSelf && inventory.autoSelectFeedback.GetComponentsInChildren<Graphic>(true).All(graphic=>!graphic.raycastTarget),
            "Success feedback has no duplicate explanation and does not block input");
    }
    static IEnumerator RuleHintChecks(EquipmentInventoryUI inventory,EquipmentPageUI page,Action<bool,string> check)
    {
        check(inventory.autoSelectRuleHint && !inventory.autoSelectRuleHint.activeSelf,
            "Auto Select criterion is initially hidden");
        var target=inventory.autoSelect.gameObject;
        var pointer=new PointerEventData(EventSystem.current);
        var previousSelection=EventSystem.current.currentSelectedGameObject;
        var previousKeyboard=Keyboard.current;
        var keyboard=InputSystem.AddDevice<Keyboard>();
        try {
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);yield return .1;
            check(inventory.autoSelectRuleHint.activeInHierarchy && inventory.autoSelectRuleHint.GetComponentsInChildren<TMPro.TMP_Text>().Any(label=>label.text.Contains("Highest Power")&&label.text.Contains("sets")),
                "Serialized hover event reveals the short total-Power/set criterion");
            check(inventory.autoSelectRuleHint.GetComponentsInChildren<Graphic>(true).All(graphic=>!graphic.raycastTarget), "Auto Select rule hint never intercepts input");
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerExitHandler);yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Pointer exit dismisses the Auto Select rule");
            EventSystem.current.SetSelectedGameObject(null);EventSystem.current.SetSelectedGameObject(target);yield return .1;
            check(inventory.autoSelectRuleHint.activeInHierarchy,"Keyboard selection reveals the Auto Select rule");
            EventSystem.current.SetSelectedGameObject(null);yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Keyboard deselection dismisses the rule");
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Pointer down dismisses the rule before action feedback");
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerUpHandler);
            EventSystem.current.SetSelectedGameObject(null);EventSystem.current.SetSelectedGameObject(target);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return .12;
            check(!inventory.autoSelectRuleHint.activeSelf,"Real Escape input dismisses the Auto Select rule");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return .12;
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);inventory.enabled=false;yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Disabling the inventory dismisses its rule hint");
            inventory.enabled=true;yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Re-enabling the inventory does not revive a stale hint");
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);
            page.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);yield return .1;
            check(!inventory.autoSelectRuleHint.activeSelf,"Application focus loss dismisses the rule hint");
        } finally {
            inventory.enabled=true;inventory.HideAutoSelectRule(null);
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerExitHandler);
            InputSystem.RemoveDevice(keyboard);if(previousKeyboard!=null)previousKeyboard.MakeCurrent();
            EventSystem.current.SetSelectedGameObject(previousSelection&&previousSelection.activeInHierarchy?previousSelection:null);
        }
    }
    static IEnumerator After(MainMenuNavigation nav, GameDataManager manager, EquipmentPageUI page, string output, Action<bool,string> check)
    {
        var service = manager.Tasks;
        var inventory = page.GetComponentInChildren<EquipmentInventoryUI>();
        check(!inventory.notice.gameObject.activeSelf && inventory.autoSelectFeedback && !inventory.autoSelectFeedback.activeSelf,
            "Ordinary equipment has no persistent explanation or stale result feedback");
        foreach(int width in new[]{1920,1280}) {
            RemainingFixesVerification.Resolution(width,width==1920?1080:720);yield return .3;
            var pointer=new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(inventory.autoSelect.gameObject,pointer,ExecuteEvents.pointerEnterHandler);yield return .1;
            var label=inventory.autoSelectRuleHint.GetComponentInChildren<TMPro.TMP_Text>();label.ForceMeshUpdate();
            var corners=new Vector3[4];((RectTransform)inventory.autoSelectRuleHint.transform).GetWorldCorners(corners);
            var viewport=inventory.GetComponentInParent<Canvas>().rootCanvas.pixelRect;
            check(!label.isTextOverflowing && corners.All(point=>viewport.Contains((Vector2)point)),
                "On-demand Auto Select rule fits the actual viewport and label at "+width);
            foreach(var delay in Capture("equipment-auto-select-rule-"+width))yield return delay;
            ExecuteEvents.Execute(inventory.autoSelect.gameObject,pointer,ExecuteEvents.pointerExitHandler);
        }
        var ruleChecks=RuleHintChecks(inventory,page,check);
        try { while(ruleChecks.MoveNext())yield return ruleChecks.Current; }
        finally { (ruleChecks as IDisposable)?.Dispose(); }
        var copy = service.EquipmentSnapshot;
        copy.equipment[0].level = 99; copy.bossProgress[0].wins = 999;
        check(service.EquipmentSnapshot.equipment[0].level != 99 && service.EquipmentSnapshot.bossProgress[0].wins != 999,
            "Equipment projection owns detached item and mastery copies");
        string unchanged = JsonUtility.ToJson(service.Snapshot);
        using (var locked = File.Open(manager.SavePath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            inventory.autoSelect.onClick.Invoke(); yield return .15;
            check(!string.IsNullOrEmpty(service.LastError) && inventory.notice.text == service.LastError
                && JsonUtility.ToJson(service.Snapshot) == unchanged, "Failed Auto Select shows the save error and changes no equipment or rewards");
            check(!inventory.autoSelectFeedback.activeSelf && typeof(EquipmentPageUI).GetField("selectionFeedback", Hidden).GetValue(page) == null, "Failed Auto Select does not celebrate");
        }
        var settingsBefore = service.Settings;
        var settings = service.Settings; settings.reduceMotion = false;
        check(service.SaveSettings(settings), "Enable ordinary local feedback for Auto Select regression");
        double previousPower=service.EquipmentStats.Final.Power;
        var highlights=(Image[])typeof(EquipmentPageUI).GetField("slotBackgrounds",Hidden).GetValue(page);
        var positions=highlights.Select(image=>image.rectTransform.anchoredPosition).ToArray();
        var scales=highlights.Select(image=>image.rectTransform.localScale).ToArray();
        inventory.autoSelect.onClick.Invoke(); yield return .1;
        CheckSelectionFeedback(inventory,true,service.EquipmentStats.Final.Power-previousPower,check);
        var stats=page.GetComponentInChildren<EquipmentStatsUI>();
        var currentStats=service.EquipmentStats.Final;
        check(stats && stats.totalPower.text==currentStats.Power.ToString("N0") && stats.values[1].text==currentStats.atk.ToString("0.##")
            && stats.values[2].text==currentStats.def.ToString("0.##") && stats.values[3].text==currentStats.aspd.ToString("0.00")+"/s",
            "Existing Power and stat rows display the authoritative committed loadout");
        check(typeof(EquipmentPageUI).GetField("selectionFeedback",Hidden).GetValue(page)!=null,"Changed slots still receive ordinary success highlight");
        check(service.EquippedItem(EquipmentSlot.Weapon) == "boss3-weapon" && service.EquippedItem(EquipmentSlot.Body) == "boss3-body"
            && service.EquippedItem(EquipmentSlot.Head) == "boss3-head", "Auto Select still equips the highest-Power full set");
        foreach (var delay in Capture("equipment-auto-select-feedback")) yield return delay;
        string motionFile=Path.Combine(output,"equipment-auto-select-motion.csv");
        File.WriteAllText(motionFile,"index,elapsedSeconds,weaponR,weaponG,weaponB\n");
        float motionStart=Time.unscaledTime;
        for(int frame=0;frame<10;frame++) {
            float frameStart=Time.unscaledTime;
            foreach(var delay in Capture("equipment-auto-select-motion-"+frame.ToString("D3")))yield return delay;
            var color=highlights[0].color;
            File.AppendAllText(motionFile,string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1:F4},{2:F5},{3:F5},{4:F5}\n",frame,Time.unscaledTime-motionStart,color.r,color.g,color.b));
            float remaining=.12f-(Time.unscaledTime-frameStart);
            if(remaining>0)yield return (double)remaining;
        }
        yield return 1.3;
        check(typeof(EquipmentPageUI).GetField("selectionFeedback", Hidden).GetValue(page) == null, "Local slot highlight settles without moving hit areas");
        check(highlights.Select(image=>image.rectTransform.anchoredPosition).SequenceEqual(positions)
            && highlights.Select(image=>image.rectTransform.localScale).SequenceEqual(scales),"Auto Select highlight preserves slot layout and hit areas");
        unchanged=JsonUtility.ToJson(service.Snapshot);
        inventory.autoSelect.onClick.Invoke(); inventory.autoSelect.onClick.Invoke(); yield return .1;
        CheckSelectionFeedback(inventory,false,0,check);
        check(JsonUtility.ToJson(service.Snapshot)==unchanged && typeof(EquipmentPageUI).GetField("selectionFeedback",Hidden).GetValue(page)==null,
            "Rapid repeated Auto Select does not change rewards or celebrate an unchanged loadout");
        using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            inventory.autoSelect.onClick.Invoke();yield return .1;
            check(!inventory.autoSelectFeedback.activeSelf && inventory.notice.gameObject.activeSelf && inventory.notice.text==service.LastError
                && JsonUtility.ToJson(service.Snapshot)==unchanged,"A failure after success clears the prior check/delta and preserves the committed loadout");
        }
        inventory.autoSelect.onClick.Invoke();yield return .1;
        CheckSelectionFeedback(inventory,false,0,check);
        check(service.Unequip(EquipmentSlot.Head),"Prepare an interrupted ordinary success highlight");
        inventory.autoSelect.onClick.Invoke();
        check(typeof(EquipmentPageUI).GetField("selectionFeedback",Hidden).GetValue(page)!=null,"Ordinary highlight is running before the failed retry");
        unchanged=JsonUtility.ToJson(service.Snapshot);
        using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            inventory.autoSelect.onClick.Invoke();yield return .1;
            check(!inventory.autoSelectFeedback.activeSelf && typeof(EquipmentPageUI).GetField("selectionFeedback",Hidden).GetValue(page)==null
                && inventory.notice.text==service.LastError && JsonUtility.ToJson(service.Snapshot)==unchanged,
                "Failed retry stops a prior running highlight and clears success without changing the saved loadout");
        }
        inventory.autoSelect.onClick.Invoke();yield return .1;
        CheckSelectionFeedback(inventory,false,0,check);
        page.SendMessage("OnApplicationFocus",false,SendMessageOptions.RequireReceiver);
        check(!inventory.autoSelectFeedback.activeSelf,"Application focus loss clears stale success feedback");
        inventory.autoSelect.onClick.Invoke();page.Refresh();
        check(!inventory.autoSelectFeedback.activeSelf,"Refresh clears stale success feedback");
        inventory.autoSelect.onClick.Invoke();nav.ShowPage(MainMenuPage.Dashboard);yield return .1;
        check(!inventory.autoSelectFeedback.activeSelf && !inventory.autoSelectRuleHint.activeSelf,"Leaving equipment clears result and rule feedback");
        check(nav.ShowPage(MainMenuPage.CatHome),"Return to equipment after feedback lifecycle regression");yield return .1;
        check(!inventory.autoSelectFeedback.activeSelf,"Reopening equipment does not revive success feedback");
        check(service.Unequip(EquipmentSlot.Head), "Prepare a single changed slot");
        settings = service.Settings; settings.reduceMotion = true;
        check(service.SaveSettings(settings), "Enable Reduce motion in isolated settings");
        previousPower=service.EquipmentStats.Final.Power;
        inventory.autoSelect.onClick.Invoke(); yield return .1;
        CheckSelectionFeedback(inventory,true,service.EquipmentStats.Final.Power-previousPower,check);
        check(typeof(EquipmentPageUI).GetField("selectionFeedback", Hidden).GetValue(page) == null,
            "Reduce motion retains the static committed check/Power difference without slot animation");
        check(service.SaveSettings(settingsBefore), "Restore isolated motion preference");
        check(service.BeginBattle(1,0,out var attempt),"Create an isolated active battle for the existing equipment gate");
        page.Refresh();
        check(!inventory.autoSelect.IsInteractable() && inventory.notice.gameObject.activeSelf && inventory.notice.text.Contains("battle")
            && !inventory.autoSelectFeedback.activeSelf,"An active battle keeps its actionable reason and disables Auto Select");
        unchanged=JsonUtility.ToJson(service.Snapshot);
        ExecuteEvents.Execute(inventory.autoSelect.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return .1;
        check(JsonUtility.ToJson(service.Snapshot)==unchanged && !inventory.autoSelectFeedback.activeSelf,"Keyboard submit on disabled Auto Select changes no loadout or battle state");
        check(service.ResolveBattle(attempt.id,BattleOutcome.Stopped),"Close the isolated gate-check battle through its existing service");
        page.Refresh();
        check(inventory.autoSelect.IsInteractable() && !inventory.notice.gameObject.activeSelf,"Equipment gate reason clears when the battle is no longer active");
        var owned = manager.PlayerData.equipment.Select(item => new EquipmentItem { id=item.id,level=item.level }).ToList();
        manager.PlayerData.equipment = owned.Where(item => item.id == "boss1-weapon").ToList();
        manager.PlayerData.equippedWeaponId = manager.PlayerData.equippedBodyOutfitId = manager.PlayerData.equippedHeadAccessoryId = null;
        Save(manager, check); page.Refresh(); inventory.filters[1].onClick.Invoke(); yield return .1;
        CheckEmptyState(inventory,0,"","All equipment",check);
        foreach(var wait in CaptureEmptyBranch(inventory,0,"","All equipment","equipment-empty-filtered",check))yield return wait;
        inventory.emptyAction.onClick.Invoke(); yield return .1;
        check(!inventory.emptyState.activeSelf && page.GetComponentsInChildren<EquipmentInventoryItemUI>().Any(), "Filter reset reveals owned equipment");
        manager.PlayerData.equipment.Clear(); manager.PlayerData.equipmentUnlocked = false; manager.PlayerData.rewardDays.Clear();
        Save(manager, check); page.Refresh();
        inventory.emptyAction.onClick.Invoke(); yield return .1;
        check(nav.CurrentPage == MainMenuPage.Dashboard, "Empty locked inventory opens existing Daily Reward cards");
        manager.PlayerData.equipmentUnlocked = true;
        int experience = manager.PlayerData.experience; manager.PlayerData.experience = 0;
        Save(manager, check); nav.ShowPage(MainMenuPage.CatHome); page.Refresh();
        CheckEmptyState(inventory,2,"Lv.5","Tasks",check);
        foreach(var wait in CaptureEmptyBranch(inventory,2,"Lv.5","Tasks","equipment-empty-level-locked",check))yield return wait;
        var bossButton = nav.buttons[(int)MainMenuPage.Boss];
        var gate = bossButton.GetComponent<FeatureLockUI>();
        typeof(FeatureLockUI).GetMethod("Refresh", Hidden).Invoke(gate, null);
        var toggle = (Toggle)typeof(SidebarNavigationButton).GetField("expandedToggle", Hidden).GetValue(bossButton);
        bool expanded = toggle.isOn; toggle.isOn = false; yield return .4;
        var lockedNavigation = LockedBossHints(bossButton, output, check);
        try { while (lockedNavigation.MoveNext()) yield return lockedNavigation.Current; }
        finally { (lockedNavigation as IDisposable)?.Dispose(); }
        toggle.isOn = expanded;
        RemainingFixesVerification.Resolution(1280, 720); yield return .3;
        inventory.emptyAction.onClick.Invoke(); yield return .1;
        check(nav.CurrentPage == MainMenuPage.Tasks, "Low-level next step opens Tasks");
        manager.PlayerData.experience = experience;
        foreach (var boss in manager.PlayerData.bossProgress) { boss.wins=0; boss.pendingClaims=0; boss.claimedComponents=0; }
        Save(manager, check); nav.ShowPage(MainMenuPage.CatHome); page.Refresh();
        CheckEmptyState(inventory,4,"0 / 5 wins","Boss",check);
        manager.PlayerData.bossProgress[0].wins=2;Save(manager,check);page.Refresh();
        CheckEmptyState(inventory,4,"2 / 5 wins","Boss",check);
        typeof(FeatureLockUI).GetMethod("Refresh", Hidden).Invoke(gate, null);
        int tickets = service.Tickets;
        inventory.emptyAction.onClick.Invoke(); yield return .1;
        check(nav.CurrentPage == MainMenuPage.Boss && service.Tickets == tickets && !service.HasActiveBattle, "Empty inventory opens Boss selection without spending an entry");
        manager.PlayerData.bossProgress[0].wins = 5; Save(manager, check);
        nav.ShowPage(MainMenuPage.CatHome); page.Refresh();
        CheckEmptyState(inventory,3,"","Claim",check);
        foreach(var wait in CaptureEmptyBranch(inventory,3,"","Claim","equipment-empty-claim-ready",check))yield return wait;
        inventory.emptyAction.onClick.Invoke(); yield return .1;
        var claims = nav.pages[(int)MainMenuPage.Boss].GetComponent<EquipmentPageUI>();
        check(nav.CurrentPage == MainMenuPage.Boss && claims.claimView.gameObject.activeInHierarchy && claims.claimView.Boss == 1,
            "Claim next step opens the correct existing mastery dialog");
        claims.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();
        manager.PlayerData.equipment = owned;
        manager.PlayerData.equippedWeaponId = manager.PlayerData.equippedBodyOutfitId = manager.PlayerData.equippedHeadAccessoryId = null;
        Save(manager, check); nav.ShowPage(MainMenuPage.CatHome); page.Refresh();
        yield return .2;
    }
}
