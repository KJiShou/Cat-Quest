using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CatQuest.Updates;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SqlitePlayVerificationSupport;

[InitializeOnLoad]
public static class MailboxVerification
{
    const string Key = "CatQuest.MailboxVerification";
    const string PlayRequest = "Temp/verify-mailbox-play.request";
    const string StorageRequest = "Temp/verify-mailbox-storage.request";
    const BindingFlags InstanceHidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type SettingsVerifier = typeof(SettingsPlayVerification);
    static EditorWindow GameView => (EditorWindow)SettingsVerifier.GetProperty("GameView", StaticHidden).GetValue(null);
    static PropertyInfo Size => (PropertyInfo)SettingsVerifier.GetProperty("SizeProperty", StaticHidden).GetValue(null);
    static string Folder => SessionState.GetString(Key + ".folder", "");
    static IEnumerator play;
    static double next;
    static bool storageRunning;
    static string published;
    static bool offline;

    static MailboxVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                published = FixtureCatalog();
                offline = false;
                MailInboxPanel.VerificationFeed = () => offline
                    ? Task.FromException<string>(new IOException("Offline fixture"))
                    : Task.FromResult(published);
                play = Flatten(RunPlay());
                next = EditorApplication.timeSinceStartup + .2;
            }
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || storageRunning) return;
        if (SessionState.GetBool(Key, false))
        {
            if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Key + ".deadline", "0")))
            { Finish("FAIL: timeout"); return; }
            if (!EditorApplication.isPlaying || play == null || EditorApplication.timeSinceStartup < next) return;
            try
            {
                if (!play.MoveNext()) { Finish("PASS"); return; }
                next = EditorApplication.timeSinceStartup + Convert.ToDouble(play.Current);
            }
            catch (Exception error) { Finish("FAIL: " + error); }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(StorageRequest)) { File.Delete(StorageRequest); RunStorage(); return; }
        if (!File.Exists(PlayRequest)) return;
        SessionState.SetBool(Key + ".standardSizes", File.ReadAllText(PlayRequest).Trim() == "standard");
        File.Delete(PlayRequest);
        string folder = NewFolder("play");
        SessionState.SetString(Key + ".folder", folder);
        string prior = Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        SessionState.SetBool(Key + ".hadStore", prior != null);
        SessionState.SetString(Key + ".store", prior ?? "");
        SessionState.SetString(Key + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Key + ".size", (int)Size.GetValue(GameView));
        RememberCustomSizes(Key);
        SessionState.SetString(Key + ".deadline", DateTime.UtcNow.AddMinutes(2).Ticks.ToString());
        SessionState.SetBool(Key, true);
        File.WriteAllText(Path.Combine(folder, "result.txt"), "RUNNING");
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(Path.Combine(folder, "mail.json")));
        File.WriteAllText(Path.Combine(folder, "mail.json"), @"{""version"":6,""username"":""Mail verifier"",""experience"":10000,""tutorial"":{""version"":1,""dismissed"":true}}");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        EditorApplication.isPlaying = true;
    }

    static string NewFolder(string mode)
    {
        string folder = Path.GetFullPath("Library/MailboxVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + mode);
        Directory.CreateDirectory(folder);
        return folder;
    }

    static async void RunStorage()
    {
        storageRunning = true;
        string folder = NewFolder("storage");
        string result = Path.Combine(folder, "result.txt");
        File.WriteAllText(result, "RUNNING");
        try { await MailStorageVerification.RunAsync(folder, (ok, message) => Record(folder, ok, message)); File.WriteAllText(result, "PASS"); }
        catch (Exception error) { File.WriteAllText(result, "FAIL: " + error); }
        finally { storageRunning = false; }
    }

    static void Restore()
    {
        MailInboxPanel.VerificationFeed = null;
        (play as IDisposable)?.Dispose();
        play = null;
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", SessionState.GetBool(Key + ".hadStore", false)
            ? SessionState.GetString(Key + ".store", "") : null);
        var scene = SessionState.GetString(Key + ".scene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(scene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        Size.SetValue(GameView, SessionState.GetInt(Key + ".size", 0));
        RestoreCustomSizes(Key);
        SessionState.SetBool(Key, false);
        File.WriteAllText(Path.Combine(Folder, "restored.txt"), DateTime.UtcNow.ToString("O"));
    }

    static void Finish(string outcome)
    {
        File.WriteAllText(Path.Combine(Folder, "result.txt"), outcome);
        (play as IDisposable)?.Dispose();
        play = null;
        EditorApplication.isPlaying = false;
    }

    static void Record(string folder, bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        File.AppendAllText(Path.Combine(folder, "checks.txt"), "PASS: " + message + "\n");
    }
    static void Check(bool ok, string message) => Record(Folder, ok, message);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, InstanceHidden).GetValue(target);

    static PointerEventData ButtonPointer(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = rect.GetComponentInParent<Canvas>();
        var data = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center))
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        Check(button.IsInteractable() && hits.Count > 0
            && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            button.name + " is accessible at " + Screen.width + "×" + Screen.height
                + $" (screen {data.position}, rect {rect.rect}, parent {((RectTransform)rect.parent).rect}, active {button.IsActive()}, cull {button.targetGraphic.canvasRenderer.cull}, hit {(hits.Count > 0 ? hits[0].gameObject.name : "none")})");
        return data;
    }

    static void Click(Button button) => ExecuteEvents.Execute(button.gameObject, ButtonPointer(button), ExecuteEvents.pointerClickHandler);

    static IEnumerator WaitFor(Func<bool> condition, string label)
    {
        double deadline = EditorApplication.timeSinceStartup + 10;
        while (!condition() && EditorApplication.timeSinceStartup < deadline) yield return .05;
        Check(condition(), label);
    }

    static IEnumerator Capture(RewardScreenshotCapture capture, string name)
    {
        capture.Begin(Path.Combine(Folder, name + ".png"));
        while (capture.Pending)
        { EditorApplication.QueuePlayerLoopUpdate(); GameView.Repaint(); yield return .05; }
    }

    static IEnumerator RunPlay()
    {
        var manager = GameDataManager.EnsureInstance();
        yield return Command(manager.Ready);
        Check(manager.SavePath.Contains("MailboxVerification"), "Isolated mailbox save");
        var panel = UnityEngine.Object.FindFirstObjectByType<MailInboxPanel>();
        Check(panel, "Authored mailbox bound to CharacterHome");
        var entry = Field<Button>(panel, "entry");
        var entrySlot = Field<GameObject>(panel, "entrySlot");
        var badge = Field<GameObject>(panel, "badge");
        var modal = Field<GameObject>(panel, "modal");
        var rows = Field<RectTransform>(panel, "rows");
        var action = Field<Button>(panel, "action");
        var delete = Field<Button>(panel, "delete");
        var close = Field<Button>(panel, "close");
        var status = Field<TMP_Text>(panel, "status");
        Check(Field<GameObject>(panel, "listFade") && Field<MailRewardIcons>(panel, "attachments") && delete && delete.GetComponent<JuicyButton>(),
            "Inbox detail, delete control, and scroll artwork are bound");
        var capture = panel.gameObject.AddComponent<RewardScreenshotCapture>();
        var sidebar = UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expander = Field<Toggle>(sidebar, "expander");
        yield return WaitFor(() => badge.activeSelf, "Published reward shows the envelope badge");

        var resolutions = SessionState.GetBool(Key + ".standardSizes", false)
            ? new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720) }
            : new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1024,768), new Vector2Int(2560,1080) };
        foreach (var size in resolutions)
        {
            SettingsVerifier.GetMethod("Resolution", StaticHidden).Invoke(null, new object[] { size });
            foreach (bool expanded in new[] { true, false })
            {
                expander.isOn = expanded;
                yield return .35;
                string suffix = size.x + "x" + size.y + (expanded ? "-expanded" : "-collapsed");
                Check(Screen.width == size.x && Screen.height == size.y, "Actual render size " + suffix);
                if ((size.x == 1920 || size.x == 1024) && expanded)
                {
                    entrySlot.SetActive(false);
                    yield return .2;
                    yield return Capture(capture, "dashboard-before-envelope-" + size.x);
                    entrySlot.SetActive(true);
                    yield return .2;
                    yield return Capture(capture, "dashboard-with-envelope-" + size.x);
                }
                ButtonPointer(UnityEngine.Object.FindFirstObjectByType<SettingsLauncher>().GetComponent<Button>());
                Click(entry);
                yield return WaitFor(() => rows.childCount > 0 && !Field<bool>(panel, "refreshing"), "Inbox loads at " + suffix);
                var reward = rows.GetChild(0).GetComponent<Button>();
                yield return Capture(capture, "inbox-" + suffix);
                var rowView = reward.GetComponent<MailRowView>();
                var icons = Field<MailRewardIcons>(rowView, "rewards");
                Check(icons && icons.GetComponentsInChildren<Image>().Count(image => image.sprite && !image.raycastTarget) == 3,
                    "The reward row shows the Fish Coin, EXP and Boss Ticket sprites");
                ButtonPointer(Field<Button>(rowView, "primary"));
                Click(reward);
                yield return WaitFor(() => action.IsInteractable(), "Reward shows its claim action");
                yield return .2;
                yield return Capture(capture, "mailbox-" + suffix);
                ButtonPointer(action);
                ButtonPointer(delete);
                ButtonPointer(close);
                Check(close.navigation.mode == Navigation.Mode.Explicit && delete.navigation.selectOnDown == action
                    && action.navigation.selectOnDown == close,
                    "Keyboard focus stays inside the modal");
                Click(close);
                Check(!modal.activeSelf && EventSystem.current.currentSelectedGameObject == entry.gameObject, "Closing restores envelope focus");
            }
        }

        var reference = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Inbox.prefab");
        var vendor = UnityEngine.Object.Instantiate(reference, panel.transform, false);
        try { yield return .2; yield return Capture(capture, "simplecasual-reference"); }
        finally { UnityEngine.Object.DestroyImmediate(vendor); }

        offline = true;
        Click(entry);
        yield return WaitFor(() => status.text.Contains("offline"), "Offline inbox keeps the cached letter");
        Click(rows.GetChild(0).GetComponent<Button>());
        yield return .15;
        Click(action);
        yield return WaitFor(() => status.text.Contains("internet"), "Offline claim gives a retry message");
        Check(manager.Service.FishCoins == 0, "Offline claim does not pay rewards");
        offline = false;
        Click(action);
        action.onClick.Invoke();
        yield return WaitFor(() => status.text.Contains("Reward added") && manager.Service.FishCoins == 45, "Retry claims once and publishes the balance");
        Check(!badge.activeSelf && !action.IsInteractable(), "Claimed mail clears badge and disables claiming");

        yield return .15;
        Click(Field<Button>(panel, "back"));
        yield return .15;
        var claimedLabel = Field<TMP_Text>(rows.GetChild(0).GetComponent<MailRowView>(), "primaryLabel");
        claimedLabel.ForceMeshUpdate();
        Check(claimedLabel.text == "Claimed" && claimedLabel.textInfo.lineCount == 1 && !claimedLabel.isTextOverflowing,
            "Claimed stays readable on one line");
        yield return Capture(capture, "inbox-claimed");
        var controller = AppUpdateController.Instance;
        const string version = "99.0.0";
        var release = new ReleaseManifest { version = version, notes = "Mailbox verification release notes.",
            url = UpdateRules.Repository + "/releases/download/v99.0.0/test.zip", sha256 = new string('a', 64), size = 10 };
        typeof(AppUpdateController).GetProperty("Release").SetValue(controller, release);
        typeof(AppUpdateController).GetMethod("Set", InstanceHidden).Invoke(controller, new object[] { AppUpdateState.Available, "Isolated update fixture" });
        yield return WaitFor(() => rows.childCount == 2 && badge.activeSelf, "A validated newer release creates unread mail");
        Click(rows.GetChild(0).GetComponent<Button>());
        Check(Field<TMP_Text>(panel, "body").text.StartsWith(release.notes, StringComparison.Ordinal), "Update mail shows release notes");
        yield return WaitFor(() => !badge.activeSelf, "Reading the update clears its badge");
        yield return .15;
        Click(action);
        Check(!modal.activeSelf && UnityEngine.Object.FindFirstObjectByType<AppUpdatePanel>().IsOpen,
            "Update action opens the existing update panel");

        UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>().Close();
        var observe = typeof(SqlitePlayerService).GetMethod("ObserveMailVersionAsync", InstanceHidden);
        var latest = new Version(Application.version);
        string installedFixture = (latest.Major + 1) + ".0.0";
        yield return Command((Task<OperationResult<bool>>)observe.Invoke(manager.Service, new object[] { installedFixture }));
        yield return WaitFor(() => badge.activeSelf, "A newly installed version shows the red inbox dot");
        var icon = entry.transform.Find("EnvelopeIcon").GetComponent<Image>();
        var dot = badge.GetComponent<Image>();
        Check(icon.sprite.name.Contains("MenuIcon02_Postbox") && icon.color.a == 1 && dot.color.a == 1
            && dot.color.r > .85f && dot.color.g < .25f, "The opaque SimpleCasual Inbox icon shows a solid red dot");
        offline = true;
        Click(entry);
        yield return WaitFor(() => rows.childCount == 3 && !Field<bool>(panel, "refreshing"), "Update gift, release notice and global gift are separate letters");
        var updateGift = rows.GetChild(1).GetComponent<MailRowView>();
        var giftIcons = Field<MailRewardIcons>(updateGift, "rewards");
        var values = giftIcons.GetComponentsInChildren<TMP_Text>().Select(label => label.text).ToArray();
        Check(values.SequenceEqual(new[] { "888", "888", "8" }), "Installed gift row shows 888 Fish Coins, 888 EXP, and 8 Boss Tickets");
        yield return Capture(capture, "inbox-update-gift");
        var baseline = manager.Service.Experience;

        Click(rows.GetChild(2).GetComponent<Button>());
        yield return .15;
        Click(delete);
        Check(status.text == "Delete this letter?" && action.IsInteractable(), "Delete requires an explicit confirmation");
        yield return Capture(capture, "confirm-delete-claimed");
        Click(delete);
        Check(!action.IsInteractable() && rows.childCount == 3, "Keep mail restores the claimed view without deleting");
        Click(delete);
        yield return .15;
        Click(action);
        action.onClick.Invoke();
        yield return WaitFor(() => rows.childCount == 2 && status.text == "Mail deleted.", "Claimed mail is removed once, even offline");
        Check(manager.Service.Experience == baseline && manager.Service.FishCoins == 45, "Deleting mail leaves claimed rewards intact");
        offline = false;
        Click(close);
        yield return .15;
        Click(entry);
        yield return WaitFor(() => !Field<bool>(panel, "refreshing") && rows.childCount == 2, "Refreshing does not reintroduce deleted mail");
        offline = true;
        Click(Field<Button>(rows.GetChild(1).GetComponent<MailRowView>(), "primary"));
        yield return WaitFor(() => manager.Service.Experience == baseline + 888 && !badge.activeSelf, "Installed update gift claims offline and clears the red dot");
    }

    static string FixtureCatalog() =>
        @"{""format"":1,""letters"":[{""id"":""mailbox-play-check"",""title"":""A parcel for your next quest"",
            ""body"":""Thanks for playing Cat Quest! Here are some supplies for your next adventure."",
            ""publishedUtc"":""2020-01-01T00:00:00Z"",""fishCoins"":45,""experience"":25,""bossTickets"":1}]}";
}
