using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Observes saved authored assets. This verifier never generates or repairs UI.
[InitializeOnLoad]
public static class SettingsPlayVerification
{
    const string Flag = "CatQuest.SettingsPlayVerification";
    const string Request = "Temp/verify-settings-play.request";
    const string StoreVariable = "CATQUEST_TASK_TEST_STORE";
    const string Output = "Library/SettingsVerification";
    const string PrefabPath = "Assets/Prefabs/Settings/Settings.prefab";
    const string Vendor = "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/";
    const string ReferencePath = Vendor + "Prefabs/Prefabs_DemoScene_Panels/Light/Settings.prefab";
    static IEnumerator run;
    static double next;
    static string context, loggedError;
    static RewardScreenshotCapture capture;
    static readonly List<string> evidence = new List<string>();
    static readonly List<string> checks = new List<string>();
    static string Folder => SessionState.GetString(Flag + ".folder", Output);

    static SettingsPlayVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += StateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        EditorApplication.delayCall += RecoverAfterReload;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Flag, false) && (type == LogType.Error || type == LogType.Exception))
                loggedError = message + "\n" + stack;
        };
    }

    static void BeforeReload()
    {
        if (SessionState.GetBool(Flag, false) && SessionState.GetBool(Flag + ".enteredPlay", false)
            && !SessionState.GetBool(Flag + ".finished", false))
            Finish("FAIL: script compilation/domain reload interrupted the running verification at " + context + ". Run again after code changes finish.");
    }

    static void RecoverAfterReload()
    {
        if (!SessionState.GetBool(Flag, false) || !SessionState.GetBool(Flag + ".enteredPlay", false) || run != null) return;
        if (!SessionState.GetBool(Flag + ".finished", false))
            Finish("FAIL: a domain reload discarded the running verification. Run again after code changes finish.");
        else if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Restore();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Flag, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Flag + ".enteredPlay", true);
            evidence.Clear(); checks.Clear(); loggedError = null;
            run = Run(); next = EditorApplication.timeSinceStartup + 2;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (!SessionState.GetBool(Flag + ".finished", false)) WriteResult("FAIL: Play Mode ended before verification completed.");
            Restore();
        }
    }

    static void Poll()
    {
        if (SessionState.GetBool(Flag, false))
        {
            // Entry can be requested during the Editor's transition update; recover
            // an already-entered Play Mode if its state notification was missed.
            if (EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating
                && run == null && !SessionState.GetBool(Flag + ".enteredPlay", false)
                && !SessionState.GetBool(Flag + ".finished", false))
                StateChanged(PlayModeStateChange.EnteredPlayMode);
            if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Flag + ".deadline", ""), null, DateTimeStyles.RoundtripKind))
            { Finish("FAIL: bounded run timed out at " + context); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !EditorApplication.isPlaying || run == null) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try
            {
                Check(loggedError == null, "Unexpected Unity error: " + loggedError);
                if (run.MoveNext())
                {
                    next = EditorApplication.timeSinceStartup + Convert.ToDouble(run.Current, CultureInfo.InvariantCulture);
                    if (SessionState.GetString(Flag + ".context", "") != context)
                    {
                        SessionState.SetString(Flag + ".context", context ?? "");
                        File.WriteAllText(Folder + "/progress.txt", DateTime.UtcNow.ToString("O") + "\n" + context);
                    }
                }
                else
                {
                    Check(evidence.All(path => File.Exists(path) && new FileInfo(path).Length > 100), "A requested screenshot is missing or empty.");
                    Finish("PASS: Settings, Tags, Tag Editor and missing-configuration Account; rendered reference and 1920x1080/1280x720 captures; modal routing/focus, deferred volume commits/reload, unscaled button/toggle motion, stable IDs and unsaved task drafts, tag deletion and empty/error states. Screenshot comparison and real Windows notification/login behavior remain separate manual review.");
                }
            }
            catch (Exception e) { Finish("FAIL at " + context + ": " + e); }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        evidence.Clear(); checks.Clear(); context = "static authored bindings"; loggedError = null;
        string folder = Output + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        SessionState.SetString(Flag + ".folder", folder);
        try
        {
            ValidateBindings();
            string previous = Environment.GetEnvironmentVariable(StoreVariable);
            SessionState.SetBool(Flag + ".hadStore", previous != null);
            SessionState.SetString(Flag + ".store", previous ?? "");
            SessionState.SetString(Flag + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Flag + ".sceneSetup", SceneSignature());
            SessionState.SetInt(Flag + ".size", (int)SizeProperty.GetValue(GameView));
            var group = GameViewGroup();
            SessionState.SetInt(Flag + ".customCount", Count(group, "GetCustomCount"));
            SessionState.SetFloat(Flag + ".timeScale", Time.timeScale);
            SessionState.SetBool(Flag + ".paused", EditorApplication.isPaused);
            SessionState.SetString(Flag + ".deadline", DateTime.UtcNow.AddSeconds(240).ToString("O"));
            SessionState.SetBool(Flag + ".finished", false);
            SessionState.SetBool(Flag + ".enteredPlay", false);
            SessionState.SetBool(Flag, true);
            WriteResult("RUNNING");
            Environment.SetEnvironmentVariable(StoreVariable, Path.GetFullPath(folder + "/save.json"));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            Check(EditorSceneManager.playModeStartScene, "CharacterHome scene missing.");
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }
        catch (Exception e)
        {
            if (SessionState.GetBool(Flag, false)) Finish("FAIL starting verification: " + e);
            else WriteResult("FAIL static bindings: " + e);
        }
    }

    static void ValidateBindings()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<SettingsPanelUI>(PrefabPath);
        Check(prefab, "Authored Settings prefab is missing; verification does not create it.");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePath), "Specified Settings reference is missing.");
        Check(File.Exists(Vendor + "Preview/100_Settings.png"), "Vendor Settings preview missing.");
        foreach (var field in typeof(SettingsPanelUI).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = field.GetValue(prefab);
            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) Check((UnityEngine.Object)value, "Unbound Settings field: " + field.Name);
            if (value is Array array) foreach (var item in array) Check(item is UnityEngine.Object obj && obj, "Unbound Settings array: " + field.Name);
        }
        Check(prefab.colorButtons.Length == TagColors.Palette.Length && prefab.colorSelected.Length == TagColors.Palette.Length, "Palette bindings do not match the saved palette.");
        foreach (var visual in prefab.GetComponentsInChildren<SettingsSliderVisual>(true))
            Check(visual.handle && visual.fill && visual.icon && visual.enabledHandle && visual.mutedHandle && visual.enabledIcon && visual.mutedIcon, "Slider artwork binding missing.");
        Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(component => component), "Settings prefab contains a missing script.");
        var canvas = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        var launcher = canvas ? canvas.GetComponentInChildren<SettingsLauncher>(true) : null;
        Check(launcher && launcher.settingsPrefab == prefab && launcher.GetComponent<Button>(), "Header Settings launcher is not bound to the authored prefab.");
        var click = launcher.GetComponent<Button>().onClick;
        Check(Enumerable.Range(0, click.GetPersistentEventCount()).Any(i => click.GetPersistentTarget(i) == launcher && click.GetPersistentMethodName(i) == "Open"), "Settings header click binding missing.");
        Check(canvas.GetComponentsInChildren<TaskSystemUI>(true).All(t => t.tagTintSprite)
            && canvas.GetComponentsInChildren<EditTaskDetailsUI>(true).All(t => t.tagTintSprite), "Neutral tag artwork bindings missing.");
        var audio = AssetDatabase.LoadAssetAtPath<SettingsAudio>("Assets/Resources/SettingsAudio.prefab");
        Check(audio && audio.soundSource && audio.musicSource && audio.soundSource != audio.musicSource, "Independent serialized audio sources missing.");
    }

    static EditorWindow GameView => EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty => GameView.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static int Count(object group, string method) => (int)group.GetType().GetMethod(method).Invoke(group, null);
    static object GameViewGroup()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(type);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var method = type.GetMethod("GetGroup");
        return method.Invoke(sizes, new[] { Enum.ToObject(method.GetParameters()[0].ParameterType, 0) });
    }
    static void Resolution(Vector2Int size)
    {
        var assembly = typeof(Editor).Assembly;
        var fixedType = assembly.GetType("UnityEditor.GameViewSizeType");
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var constructor = sizeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { fixedType, typeof(int), typeof(int), typeof(string) }, null);
        var custom = constructor.Invoke(new[] { Enum.ToObject(fixedType, 1), (object)size.x, size.y, "Cat Quest Settings Verification" });
        var group = GameViewGroup(); group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { custom });
        SizeProperty.SetValue(GameView, Count(group, "GetBuiltinCount") + Count(group, "GetCustomCount") - 1);
        GameView.Show(); GameView.Focus();
    }
    static string SceneSignature() => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var scene = SceneManager.GetSceneAt(i);
        return scene.path + "|" + scene.isLoaded + "|" + (scene == SceneManager.GetActiveScene()) + "|" + scene.isDirty;
    }));
    static void Restore()
    {
        try { (run as IDisposable)?.Dispose(); } catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nCleanup error: " + e); }
        run = null; capture = null;
        try
        {
            Environment.SetEnvironmentVariable(StoreVariable, SessionState.GetBool(Flag + ".hadStore", false) ? SessionState.GetString(Flag + ".store", "") : null);
            string previousScene = SessionState.GetString(Flag + ".scene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScene);
            SizeProperty.SetValue(GameView, SessionState.GetInt(Flag + ".size", 0));
            var group = GameViewGroup(); int builtIn = Count(group, "GetBuiltinCount");
            for (int i = Count(group, "GetCustomCount") - 1; i >= SessionState.GetInt(Flag + ".customCount", 0); i--)
                group.GetType().GetMethod("RemoveCustomSize").Invoke(group, new object[] { builtIn + i });
            Time.timeScale = SessionState.GetFloat(Flag + ".timeScale", 1);
            EditorApplication.isPaused = SessionState.GetBool(Flag + ".paused", false);
            Check(SceneSignature() == SessionState.GetString(Flag + ".sceneSetup", ""), "Prior Editor scene setup/dirty state changed.");
            File.AppendAllText(Folder + "/result.txt", "\nRESTORED: prior save environment, Play Mode start scene, Game View selection/custom sizes, time scale, pause and Editor scene setup.\n");
        }
        catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nFAIL restoring environment: " + e); }
        finally { SessionState.SetBool(Flag + ".enteredPlay", false); SessionState.SetBool(Flag, false); }
    }
    static void Finish(string result)
    {
        SessionState.SetBool(Flag + ".finished", true);
        WriteResult(result);
        try { (run as IDisposable)?.Dispose(); } catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nFAIL disposing run: " + e); }
        run = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Restore();
    }
    static void WriteResult(string result)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/result.txt", DateTime.UtcNow.ToString("O") + "\n" + result + "\nContext: " + context
            + "\nReference prefab: " + ReferencePath + "\nReference preview: " + Vendor + "Preview/100_Settings.png\n"
            + string.Join("\n", checks) + "\nScreenshots:\n" + string.Join("\n", evidence.Count > 0 ? evidence : Directory.GetFiles(Folder, "*.png").OrderBy(path => path).ToList()));
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/latest-run.txt", Path.GetFullPath(Folder));
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Rect Bounds(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }
    static GameObject Hit(Vector2 position)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
        Check(hits.Count > 0, "No UI raycast at " + position);
        return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
    }
    static PointerEventData Pointer(Component component) => new PointerEventData(EventSystem.current)
    { button = PointerEventData.InputButton.Left, position = Bounds((RectTransform)component.transform).center };
    static void Click(Selectable control)
    {
        Check(control && control.IsInteractable() && control.gameObject.activeInHierarchy, "Control is unavailable: " + (control ? control.name : "null"));
        var pointer = Pointer(control);
        if (control.targetGraphic) pointer.position = Bounds(control.targetGraphic.rectTransform).center;
        var target = Hit(pointer.position);
        Check(target && (target == control.gameObject || target.transform.IsChildOf(control.transform)), "Control center is blocked: " + control.name);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
    }
    static void Backdrop(SettingsPanelUI ui)
    {
        var viewport = ui.GetComponentInParent<Canvas>().rootCanvas.pixelRect;
        var position = new Vector2(viewport.xMin + 8, viewport.yMin + 8);
        var target = Hit(position);
        Check(target && ui.backdropButtons.Any(button => button.gameObject == target), "Modal backdrop does not intercept the background.");
        ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position }, ExecuteEvents.pointerClickHandler);
    }
    static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        if (!capture) capture = new GameObject("Settings Verification Capture").AddComponent<RewardScreenshotCapture>();
        string path = Path.GetFullPath(Folder + "/" + name + ".png"); evidence.Add(path); capture.Begin(path);
    }
    static GameObject Page(SettingsPanelUI ui) => ui.CurrentView == "Tags" ? ui.tagsPage : ui.CurrentView == "TagEditor" ? ui.tagEditorPage
        : ui.CurrentView == "Account" ? ui.accountPage : ui.CurrentView == "Confirm" ? ui.confirmPage : ui.settingsPage;
    static void Layout(SettingsPanelUI ui, Vector2Int size)
    {
        Canvas.ForceUpdateCanvases();
        var viewport = TaskTabsVerification.Viewport(ui, size);
        var page = Page(ui); var popup = page.transform.Find("Popup") as RectTransform;
        Check(popup, "Active Settings popup missing.");
        var bounds = Bounds(popup);
        Check(bounds.xMin >= viewport.xMin - 2 && bounds.xMax <= viewport.xMax + 2 && bounds.yMin >= viewport.yMin - 2 && bounds.yMax <= viewport.yMax + 2,
            "Settings popup exceeds viewport: " + ui.CurrentView + " " + bounds);
        Check(new[] { ui.settingsPage, ui.tagsPage, ui.tagEditorPage, ui.accountPage, ui.confirmPage }.Count(item => item.activeSelf) == 1, "More than one Settings view is active.");
        var selected = EventSystem.current.currentSelectedGameObject;
        Check(selected && selected.transform.IsChildOf(page.transform), "Keyboard focus escaped the active modal.");
        foreach (var text in page.GetComponentsInChildren<TMP_Text>())
        {
            if (text.GetComponentInParent<ScrollRect>() || text.GetComponentInParent<TMP_InputField>()) continue;
            text.ForceMeshUpdate();
            Check(!text.isTextOverflowing, "Settings text is clipped: " + text.name + ": " + text.text);
        }
        foreach (var control in page.GetComponentsInChildren<Selectable>())
        {
            if (control.GetComponentInParent<ScrollRect>()) continue;
            var rect = Bounds((RectTransform)control.transform);
            Check(rect.xMin >= viewport.xMin - 2 && rect.xMax <= viewport.xMax + 2 && rect.yMin >= viewport.yMin - 2 && rect.yMax <= viewport.yMax + 2, "Control leaves viewport: " + control.name);
        }
    }
    static void Stable(Rect before, Component control)
    {
        var after = Bounds((RectTransform)control.transform);
        Check(Vector2.Distance(before.position, after.position) < 1 && Vector2.Distance(before.size, after.size) < 1, "Feedback moved the control's hit area: " + control.name);
    }
    static void Reset(JuicyButton feedback, string reason) => Check(Vector2.Distance(feedback.DrawScale, Vector2.one) < .005f && Mathf.Abs(feedback.DrawOffset) < .1f, reason);

    static IEnumerator Run()
    {
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var launcher = UnityEngine.Object.FindFirstObjectByType<SettingsLauncher>();
        Check(nav && nav.taskSystem && launcher && EventSystem.current, "CharacterHome Settings/navigation bindings missing.");
        var manager = GameDataManager.EnsureInstance(); var service = manager.Tasks;
        Check(Path.GetFullPath(manager.SavePath) == Path.GetFullPath(Folder + "/save.json") && service.Snapshot.tasks.Count == 0, "Run is not using its fresh isolated save.");
        var account = AccountController.EnsureInstance();
        var originalConfig = account.configuration;
        var blankConfig = ScriptableObject.CreateInstance<AccountConnectionConfig>();
        account.configuration = blankConfig;
        GameObject reference = null;
        try
        {
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                string suffix = size.x + "x" + size.y;
                context = suffix + " baseline/reference";
                Resolution(size); Check(nav.ShowPage(MainMenuPage.Dashboard), "Cannot open Dashboard."); yield return 1;
                Capture("baseline-" + suffix); yield return .15;
                var canvas = launcher.GetComponentInParent<Canvas>().rootCanvas;
                reference = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePath), canvas.transform);
                reference.name = "VerificationOnly_VendorSettings"; reference.SetActive(true);
                TaskUIBuilder.Stretch((RectTransform)reference.transform); reference.transform.SetAsLastSibling();
                var referencePanel = (RectTransform)reference.transform.Find("Popup");
                referencePanel.anchorMin = referencePanel.anchorMax = referencePanel.pivot = new Vector2(.5f, .5f);
                referencePanel.anchoredPosition = Vector2.zero;
                var canvasRect = ((RectTransform)canvas.transform).rect;
                float fit = Mathf.Min(1, (canvasRect.width - 100) / referencePanel.rect.width, (canvasRect.height - 140) / (referencePanel.rect.height + 120));
                referencePanel.localScale = Vector3.one * fit;
                yield return .3; Capture("vendor-settings-reference-" + suffix); yield return .15;
                UnityEngine.Object.Destroy(reference); reference = null; yield return .1;
                Click(launcher.GetComponent<Button>()); yield return .25;
                var ui = UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>();
                Check(ui && ui.CurrentView == "Settings", "Header did not open Settings.");
                Layout(ui, size); Check(Hit(Bounds((RectTransform)launcher.transform).center) != launcher.gameObject, "Modal leaves the header gear clickable behind it.");
                Capture("settings-" + suffix); yield return .15;

                context = suffix + " controls/motion";
                var feedback = ui.accountButton.GetComponent<JuicyButton>();
                Check(feedback && feedback.enabled && ui.accountButton.GetComponents<JuicyButton>().Length == 1, "Account button does not reuse one shared feedback owner.");
                var originalHit = Bounds((RectTransform)ui.accountButton.transform);
                var pointer = Pointer(ui.accountButton); EventSystem.current.SetSelectedGameObject(null);
                Time.timeScale = 0;
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                foreach (float delay in new[] { .04f, .12f, .25f })
                {
                    yield return delay; Stable(originalHit, ui.accountButton);
                    Capture("button-hover-" + delay.ToString("0.00", CultureInfo.InvariantCulture) + "-" + suffix); yield return .05;
                }
                Check(feedback.DrawScale.y > 1.005f, "Button hover did not animate with timeScale zero.");
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                yield return .25; Check(feedback.DrawScale.y < .98f, "Button press feedback missing."); Stable(originalHit, ui.accountButton);
                Capture("button-pressed-" + suffix); yield return .1;
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                yield return .6; Reset(feedback, "Button failed to settle after release/exit.");
                ui.accountButton.interactable = false;
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                yield return .2; Check(ui.CurrentView == "Settings", "Disabled button navigated."); Reset(feedback, "Disabled button retained feedback.");
                ui.accountButton.interactable = true;
                ExecuteEvents.Execute(ui.accountButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return .2;
                feedback.SendMessage("OnApplicationFocus", false, SendMessageOptions.RequireReceiver); Reset(feedback, "Simulated focus loss retained feedback.");
                Time.timeScale = 1;

                var toggleVisual = ui.notifications.GetComponent<SettingsToggleVisual>();
                Check(toggleVisual && toggleVisual.handle, "Notification switch visual missing.");
                bool previousNotifications = service.Settings.notificationsEnabled;
                var toggleBounds = Bounds((RectTransform)ui.notifications.transform);
                Click(ui.notifications);
                yield return .04; Capture("notification-moving-" + suffix); yield return .12;
                Check(service.Settings.notificationsEnabled != previousNotifications, "Notification toggle did not commit.");
                yield return .3;
                Check(Mathf.Abs(toggleVisual.handle.anchoredPosition.x - (ui.notifications.isOn ? toggleVisual.travel : -toggleVisual.travel)) < .5f, "Notification handle did not settle.");
                Stable(toggleBounds, ui.notifications); Capture("notification-settled-" + suffix); yield return .1;

                var audio = SettingsAudio.EnsureInstance(); Check(audio && audio.soundSource && audio.musicSource, "Settings audio sources missing.");
                float beforeVolume = service.Settings.soundVolume;
                var sliderPointer = Pointer(ui.sound);
                ExecuteEvents.Execute(ui.sound.gameObject, sliderPointer, ExecuteEvents.pointerDownHandler);
                ui.sound.value = .23f;
                yield return .1;
                Check(Mathf.Abs(service.Settings.soundVolume - beforeVolume) < .001f && Mathf.Abs(audio.soundSource.volume - .23f) < .001f, "Slider did not preview without committing during drag.");
                int volumeCommits = 0; Action countedCommit = () => volumeCommits++;
                service.Changed += countedCommit;
                try
                {
                    for (int i = 0; i < 4; i++) ExecuteEvents.Execute(ui.sound.gameObject, sliderPointer, ExecuteEvents.pointerUpHandler);
                }
                finally { service.Changed -= countedCommit; }
                Check(Mathf.Abs(service.Settings.soundVolume - .23f) < .001f, "Slider pointer release did not commit.");
                Check(volumeCommits == 1, "Repeated slider release duplicated the settings commit.");
                ui.sound.value = 0;
                ExecuteEvents.Execute(ui.sound.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.deselectHandler);
                var soundVisual = ui.sound.GetComponent<SettingsSliderVisual>();
                Check(audio.soundSource.volume == 0 && soundVisual && !soundVisual.fill.enabled && soundVisual.handle.sprite == soundVisual.mutedHandle, "Muted slider audio/artwork state did not update.");
                ui.sound.value = .23f;
                ExecuteEvents.Execute(ui.sound.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.deselectHandler);
                ui.music.value = .41f;
                ExecuteEvents.Execute(ui.music.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.deselectHandler);
                Check(Mathf.Abs(service.Settings.musicVolume - .41f) < .001f && Mathf.Abs(audio.musicSource.volume - .41f) < .001f, "Keyboard/deselect volume commit failed.");
                var reloaded = new PlayerDataStore(manager.SavePath);
                Check(Mathf.Abs(reloaded.Data.settings.soundVolume - .23f) < .001f && Mathf.Abs(reloaded.Data.settings.musicVolume - .41f) < .001f, "Settings controls did not persist through disk reload.");

                EventSystem.current.SetSelectedGameObject(ui.tagsButton.gameObject);
                ExecuteEvents.Execute(ui.tagsButton.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                yield return .3; Check(ui.CurrentView == "Tags" && EventSystem.current.currentSelectedGameObject == ui.addTag.gameObject, "Keyboard submit did not route/focus Tag Manage.");
                Layout(ui, size); Capture("tags-" + suffix); yield return .15;
                var firstTag = service.Snapshot.tags.First();
                var firstRow = ui.tagList.GetComponentsInChildren<SettingsTagRow>().Single(row => row.name == "Tag_" + firstTag.id);
                Click(firstRow.edit); yield return .2;
                Layout(ui, size); Capture("tag-editor-" + suffix); yield return .15;
                int oldCount = service.Snapshot.tags.Count;
                ui.tagName.text = " "; Click(ui.saveTag);
                Check(ui.CurrentView == "TagEditor" && !string.IsNullOrEmpty(ui.tagError.text) && service.Snapshot.tags.Count == oldCount, "Invalid tag name did not preserve the editor/catalog.");
                Capture("tag-error-" + suffix); yield return .1;
                Click(ui.tagCancel); yield return .1;
                Backdrop(ui); yield return .1; Check(ui.CurrentView == "Settings", "Tag backdrop did not return to Settings.");
                Click(ui.accountButton); yield return .2;
                Check(!account.State.configured && !ui.googleSignIn.interactable && !ui.upload.interactable && !string.IsNullOrEmpty(ui.accountStatus.text), "Missing configuration was not shown without enabling sign-in/upload.");
                Layout(ui, size); Capture("account-unconfigured-" + suffix); yield return .15;
                Click(ui.accountBack); yield return .1;
                for (int i = 0; i < 3; i++) ExecuteEvents.Execute(ui.tagsButton.gameObject, Pointer(ui.tagsButton), ExecuteEvents.pointerClickHandler);
                yield return .2;
                Check(ui.tagList.GetComponentsInChildren<SettingsTagRow>().Length == service.Snapshot.tags.Count, "Rapid navigation duplicated tag rows.");
                Check(UnityEngine.Object.FindObjectsByType<SettingsPanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "Opening Settings duplicated modal instances.");
                Backdrop(ui); yield return .1; Backdrop(ui); yield return .1;
                Check(!ui.gameObject.activeSelf && EventSystem.current.currentSelectedGameObject == launcher.gameObject, "Backdrop close failed to restore header focus.");
                Reset(feedback, "Page closure retained button feedback.");

                context = suffix + " stable tag IDs and unsaved draft";
                Check(service.Upsert(new QuestTask { title = "Saved task " + suffix, tagIds = new List<string> { firstTag.id } }, null, out var taskId), service.LastError);
                Check(service.Upsert(new QuestTask { title = "Unrelated task " + suffix }, null, out var unrelatedId), service.LastError);
                Check(nav.ShowPage(MainMenuPage.Tasks), "Cannot open Tasks."); yield return .5;
                var tasks = nav.taskSystem; tasks.SelectTab(TaskTab.Todo); yield return .1;
                Click(tasks.filterButton); yield return .1;
                var taskModal = canvas.transform.Find("TaskDialog");
                Check(taskModal && taskModal.gameObject.activeInHierarchy, "Task filter modal did not open.");
                var tagFilter = taskModal.GetComponentsInChildren<Toggle>().Single(toggle => toggle.name == firstTag.name);
                Click(tagFilter);
                Click(taskModal.GetComponentsInChildren<Button>().Single(button => button.name == "Apply")); yield return .1;
                Check(tasks.listContent.GetComponentsInChildren<Button>().Any(button => button.name == "Task_" + taskId)
                    && !tasks.listContent.GetComponentsInChildren<Button>().Any(button => button.name == "Task_" + unrelatedId), "Selected ID filter did not isolate the tagged task.");
                tasks.SelectTask(taskId); yield return .1;
                Click(tasks.detailsRoot.Find("Button/Edit Button").GetComponentInChildren<Button>()); yield return .2;
                var editor = tasks.editor;
                editor.taskTitle.text = "Unsaved draft " + suffix;
                editor.formScroll.verticalNormalizedPosition = 0; yield return .2;
                Click(editor.addSubtask); yield return .3;
                int blankRows = editor.subtaskContent.GetComponentsInChildren<TMP_InputField>().Count(field => field.text == "");
                Check(blankRows > 0, "Blank unsaved subtask fixture missing.");
                Click(launcher.GetComponent<Button>()); yield return .1; Click(ui.tagsButton); yield return .1;
                firstRow = ui.tagList.GetComponentsInChildren<SettingsTagRow>().Single(row => row.name == "Tag_" + firstTag.id);
                Click(firstRow.edit); yield return .1;
                string renamed = "Updated " + suffix;
                ui.tagName.text = renamed; Click(ui.colorButtons[1]); Click(ui.saveTag); yield return .2;
                var draft = editor.ReadDraft(out _);
                Check(draft.title == "Unsaved draft " + suffix && draft.tagIds.SequenceEqual(new[] { firstTag.id })
                    && editor.subtaskContent.GetComponentsInChildren<TMP_InputField>().Count(field => field.text == "") == blankRows, "Tag edit lost task draft identity/content/blank rows.");
                Check(service.Query(TaskTab.Todo, renamed, null, new[] { firstTag.id }).Any(view => view.task.id == taskId), "Rename broke lookup by the retained tag ID.");
                Check(tasks.listContent.GetComponentsInChildren<Button>().Any(button => button.name == "Task_" + taskId)
                    && !tasks.listContent.GetComponentsInChildren<Button>().Any(button => button.name == "Task_" + unrelatedId), "Renaming lost the active ID filter.");
                var chip = editor.tagContent.GetComponentsInChildren<TMP_Text>().Single(text => text.text == renamed + " ×");
                Check(chip.GetComponentInParent<Image>().color == TagColors.Background(TagColors.Palette[1]), "Unsaved draft chip did not update its color.");
                Click(ui.tagsBack); yield return .1; Click(ui.close); yield return .1;
                editor.formScroll.verticalNormalizedPosition = 1; yield return .2;
                Capture("task-editor-renamed-color-" + suffix); yield return .15;
                Click(launcher.GetComponent<Button>()); yield return .1; Click(ui.tagsButton); yield return .1;
                firstRow = ui.tagList.GetComponentsInChildren<SettingsTagRow>().Single(row => row.name == "Tag_" + firstTag.id);
                Click(firstRow.delete); yield return .1;
                Check(ui.CurrentView == "Confirm", "Tag delete skipped confirmation.");
                Click(ui.cancelDelete); yield return .1;
                Check(service.Snapshot.tags.Any(tag => tag.id == firstTag.id), "Cancelling delete removed the tag.");
                firstRow = ui.tagList.GetComponentsInChildren<SettingsTagRow>().Single(row => row.name == "Tag_" + firstTag.id);
                Click(firstRow.delete); yield return .1; Click(ui.confirmDelete); yield return .2;
                Check(service.Snapshot.tasks.Any(task => task.id == taskId) && !service.Snapshot.tasks.Single(task => task.id == taskId).tagIds.Contains(firstTag.id)
                    && editor.ReadDraft(out _).tagIds.Count == 0 && editor.taskTitle.text == "Unsaved draft " + suffix, "Tag deletion removed the task or damaged the unsaved draft.");
                Click(ui.tagsBack); yield return .1; Click(ui.close); yield return .1;
                tasks.CancelDraft(); Check(service.Delete(taskId) && service.Delete(unrelatedId), service.LastError);
                checks.Add("PASS " + suffix + ": raycasts, modal focus/close, real pointer/keyboard controls, motion/reset, slider preview/commit/reload, tag validation, stable-ID filter/draft rename/recolor/delete, account missing configuration.");
            }
            context = "empty tag state";
            Click(launcher.GetComponent<Button>()); yield return .1;
            var panel = UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>(); Click(panel.tagsButton); yield return .1;
            foreach (var tag in service.Snapshot.tags) Check(service.DeleteTag(tag.id), service.LastError);
            yield return .2; Check(panel.emptyTags.gameObject.activeSelf && panel.tagList.GetComponentsInChildren<SettingsTagRow>().Length == 0, "Empty tag state missing.");
            Capture("tags-empty-1280x720"); yield return .15;
            Click(panel.addTag); yield return .1; panel.tagName.text = "New tag"; Click(panel.colorButtons[4]); Click(panel.saveTag); yield return .2;
            Check(service.Snapshot.tags.Count == 1 && service.Snapshot.tags[0].colorHex == TagColors.Palette[4], "New colored tag could not be created from empty state.");
            Click(panel.tagsBack); yield return .1; Click(panel.close); yield return .1;
            checks.Add("PASS: empty tag catalog and adding a new colored tag; all verification state stayed in the isolated save.");
            context = "long names and multiple tag colors at 1280x720";
            var multiIds = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Check(service.SaveTag(null, new string('W', 29) + i, TagColors.Palette[i + 1], out var id), service.LastError);
                multiIds.Add(id);
            }
            Check(service.Upsert(new QuestTask { title = "Multiple long colored tags", tagIds = multiIds }, null, out var multiTask), service.LastError);
            var taskUi = nav.taskSystem;
            taskUi.SelectTab(TaskTab.Todo); yield return .2;
            Click(taskUi.filterButton); yield return .2;
            var filterModal = launcher.GetComponentInParent<Canvas>().rootCanvas.transform.Find("TaskDialog");
            Click(filterModal.GetComponentsInChildren<Button>().Single(button => button.name == "Clear")); yield return .2;
            taskUi.SelectTask(multiTask); yield return .2;
            Capture("task-multiple-tags-details-1280x720"); yield return .15;
            var taskRow = taskUi.listContent.Find("Task_" + multiTask);
            var rowBounds = Bounds((RectTransform)taskRow);
            foreach (var label in taskRow.GetComponentsInChildren<TMP_Text>().Where(label => label.text.StartsWith(new string('W', 29))))
            {
                var chipBounds = Bounds((RectTransform)label.transform.parent);
                Check(chipBounds.xMin >= rowBounds.xMin && chipBounds.xMax <= rowBounds.xMax, "Long task tag escapes its row.");
            }
            Click(taskUi.detailsRoot.Find("Button/Edit Button").GetComponentInChildren<Button>()); yield return .2;
            Capture("task-multiple-tags-editor-1280x720"); yield return .15;
            Check(taskUi.editor.ReadDraft(out _).tagIds.Count == 3, "Multiple tag assignments were lost.");
            var tagBounds = Bounds(taskUi.editor.tagContent);
            var selectorBounds = Bounds((RectTransform)taskUi.editor.tagSelection.transform);
            Check(tagBounds.xMax <= selectorBounds.xMax + 1, "Long tags widen their content beyond the selector.");
            var chips = taskUi.editor.tagContent.GetComponentsInChildren<TMP_Text>();
            Check(chips.Length == 3, "Multiple colored editing chips missing.");
            foreach (var label in chips)
            {
                var chipBounds = Bounds((RectTransform)label.transform.parent);
                Check(chipBounds.xMin >= tagBounds.xMin - 1 && chipBounds.xMax <= tagBounds.xMax + 1, "Long editing tag escapes its wrapping area.");
            }
            taskUi.CancelDraft();
            checks.Add("PASS: maximum-length tag names, independent colors, bounded task chips and multiple wrapping editing chips.");
        }
        finally
        {
            if (reference) UnityEngine.Object.Destroy(reference);
            if (account) account.configuration = originalConfig;
            if (blankConfig) UnityEngine.Object.Destroy(blankConfig);
        }
    }
}
