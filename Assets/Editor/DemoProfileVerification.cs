using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static SqlitePlayVerificationSupport;

[InitializeOnLoad]
public static class DemoProfileVerification
{
    const string Key = "CatQuest.DemoVerification";
    const string Request = "Temp/verify-demo-profile.request";
    const string Result = "Temp/verify-demo-profile.result";
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    static IEnumerator steps;
    static double next;
    static string Save => SessionState.GetString(Key + ".save", "");
    static string Folder => Path.GetDirectoryName(Save);
    static EditorWindow View => EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo Size => View.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    static DemoProfileVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                steps = Flatten(Run());
                next = EditorApplication.timeSinceStartup + .4;
            }
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Key, false))
        {
            if (ManualTestAccount.CanSwitch && File.Exists(Request))
            {
                File.Delete(Request);
                try { Start(); }
                catch (Exception error)
                {
                    File.WriteAllText(Result, "FAIL " + error);
                    if (SessionState.GetBool(Key, false)) Restore();
                }
            }
            return;
        }
        if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Key + ".deadline", "0")))
        { Finish("FAIL timed out"); return; }
        if (!EditorApplication.isPlaying || steps == null || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (steps.MoveNext()) next = EditorApplication.timeSinceStartup + Convert.ToDouble(steps.Current);
            else Finish("PASS");
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    static void Start()
    {
        string path = DemoProfile.SavePath(Guid.NewGuid().ToString("N"));
        ManualTestAccount.EnsureDemoSave(path);
        SessionState.SetString(Key + ".save", path);
        SessionState.SetString(Key + ".store", Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable) ?? "");
        SessionState.SetString(Key + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Key + ".size", (int)Size.GetValue(View));
        SessionState.SetString(Key + ".deadline", DateTime.UtcNow.AddMinutes(2).Ticks.ToString());
        RememberCustomSizes(Key);
        SessionState.SetBool(Key, true);
        Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, path);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        File.WriteAllText(Result, "RUNNING " + Folder);
        EditorApplication.isPlaying = true;
    }

    static void Finish(string outcome)
    {
        File.WriteAllText(Result, DateTime.UtcNow.ToString("o") + " " + outcome + "\nArtifacts: " + Folder);
        (steps as IDisposable)?.Dispose();
        steps = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Restore();
    }

    static void Restore()
    {
        SessionState.SetBool(Key, false);
        (steps as IDisposable)?.Dispose();
        steps = null;
        string prior = SessionState.GetString(Key + ".store", "");
        Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, string.IsNullOrEmpty(prior) ? null : prior);
        string scene = SessionState.GetString(Key + ".scene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(scene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        Size.SetValue(View, SessionState.GetInt(Key + ".size", 0));
        RestoreCustomSizes(Key);
        if (File.ReadAllText(Result).StartsWith("RUNNING", StringComparison.Ordinal))
            File.WriteAllText(Result, "FAIL interrupted before completion");
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        File.AppendAllText(Path.Combine(Folder, "checks.txt"), label + "\n");
    }

    static IEnumerator Until(Func<bool> condition, string label, double seconds = 8)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!condition() && EditorApplication.timeSinceStartup < deadline)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            yield return .05;
        }
        Check(condition(), label);
    }

    static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static IEnumerator Capture(RewardScreenshotCapture capture, string name)
    {
        capture.Begin(Path.Combine(Folder, name + ".png"));
        while (capture.Pending) { EditorApplication.QueuePlayerLoopUpdate(); View.Repaint(); yield return .05; }
        Check(File.Exists(Path.Combine(Folder, name + ".png")), name + " rendered");
    }

    static IEnumerator Run()
    {
        var manager = GameDataManager.EnsureInstance();
        yield return Command(manager.Ready);
        var service = manager.Service;
        Check(DemoProfile.IsActive && manager.SavePath == Path.ChangeExtension(Save, ".sqlite3"), "Demo boots from its own SQLite save");
        Check(service.RewardLevel == 24 && !AccountController.EnsureInstance().State.configured, "Near-level-up profile and offline account");
        Check(service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 2
            && service.DailyGoalProgress(DailyRewardGoal.Focus25Minutes) == 25, "Daily goal thresholds seeded");
        // Yesterday receives a full day of seeded activity even when today's session crosses midnight.
        var stats = service.QueryFocusStatisticsAsync(service.LocalNow.AddDays(-1), FocusStatisticsPeriod.Day, TimeZoneInfo.Local);
        yield return Command(stats);
        Check(Math.Abs(Value(stats).seconds - 1500) < .01, "Focus statistics show the seeded 25 minutes");
        Check(service.UpgradeEquipmentError("boss3-weapon", 23) == null && service.LootError(10) == null
            && service.BattleEntryError(1, 1) == null, "Upgrade, ten chests and Master entry available");

        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var mail = UnityEngine.Object.FindFirstObjectByType<MailInboxPanel>();
        var entry = Field<Button>(mail, "entry");
        var rows = Field<RectTransform>(mail, "rows");
        var close = Field<Button>(mail, "close");
        var action = Field<Button>(mail, "action");
        var updates = AppUpdateController.Ensure();
        yield return Until(() => Field<GameObject>(mail, "badge").activeSelf && updates.State == AppUpdateState.Available,
            "Offline reward mail and available demo update");
        var capture = mail.gameObject.AddComponent<RewardScreenshotCapture>();
        yield return 1.5;
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
        {
            typeof(SettingsPlayVerification).GetMethod("Resolution", Hidden).Invoke(null, new object[] { size });
            Check(nav.ShowPage(MainMenuPage.Leaderboard), "Leaderboard opens");
            var rank = UnityEngine.Object.FindFirstObjectByType<LeaderboardPanelUI>();
            foreach (LeaderboardKind kind in Enum.GetValues(typeof(LeaderboardKind)))
            {
                rank.Select(kind);
                yield return Until(() => rank.Snapshot?.ownRank > 0, kind + " has a demo rank");
                Check(rank.Snapshot.leaders.Count == 5 && rank.Snapshot.own.playerName == service.PlayerName
                    && rank.updated.text == "Demo rankings" && !rank.updated.isTextTruncated,
                    "Sample players and local profile use the authored ranking");
            }
            yield return .3;
            Check(Screen.width == size.x && Screen.height == size.y, "Game View matches requested resolution");
            yield return Capture(capture, "rank-" + size.x);
            entry.onClick.Invoke();
            yield return Until(() => rows.childCount == 3 && !Field<bool>(mail, "refreshing"), "Update letter and two reward letters load in Inbox");
            yield return Capture(capture, "inbox-" + size.x);
            close.onClick.Invoke();
        }

        Check(nav.ShowPage(MainMenuPage.Tasks), "Tasks page opens");
        yield return Command(service.CompleteAsync("demo-level-up"));
        Check(service.RewardLevel == 25 && service.DailyGoalProgress(DailyRewardGoal.ThreeQuests) == 3,
            "One task triggers Lv.25 and unlocks the third-task reward");
        var levelUp = UnityEngine.Object.FindFirstObjectByType<LevelUpCelebrationUI>();
        yield return Until(() => levelUp && levelUp.IsShowing && levelUp.continueButton.interactable, "Level-up celebration is visible");
        yield return Capture(capture, "level-up-1280");
        levelUp.continueButton.onClick.Invoke();
        yield return Command(service.ClaimDailyGoalAsync(DailyRewardGoal.ThreeQuests));
        yield return Command(service.ClaimDailyGoalAsync(DailyRewardGoal.Focus25Minutes));
        Check(nav.ShowPage(MainMenuPage.CatHome), "Cat Home opens");
        yield return Command(service.UpgradeEquipmentAsync("boss3-weapon", 23, "demo-verify-upgrade"));
        yield return Command(service.ClaimEquipmentAsync("boss1-weapon"));
        yield return Command(service.DrawLootAsync(1, "demo-verify-loot"));

        Check(nav.ShowPage(MainMenuPage.Focus), "Focus page opens");
        var focus = service.BeginFocusAsync();
        yield return Command(focus);
        yield return Command(service.FinishFocusAsync(Value(focus), 60, true));
        Check(nav.ShowPage(MainMenuPage.Boss), "Boss page opens");
        var battle = service.BeginBattleAsync(1, 1, "demo-verify-battle");
        yield return Command(battle);
        yield return Command(service.ResolveBattleAsync(Value(battle).id, BattleOutcome.Victory));
        Check(service.MasterStage == 2, "Real Master simulation advances the stage");

        int coins = service.FishCoins, tickets = service.Tickets;
        entry.onClick.Invoke();
        yield return Until(() => rows.childCount == 3 && !Field<bool>(mail, "refreshing"), "Inbox reopens");
        rows.GetChild(1).GetComponent<Button>().onClick.Invoke();
        yield return Until(() => action.isActiveAndEnabled && action.interactable, "Reward mail claim is available");
        action.onClick.Invoke();
        yield return Until(() => service.FishCoins == coins + 300 && service.Tickets == tickets + 3, "Mail attachment commits via real claim");
        close.onClick.Invoke();

        Check(nav.ShowPage(MainMenuPage.Chat), "Chat page opens");
        var client = ChatGptClient.EnsureInstance();
        yield return Until(() => client.Enabled, "Offline model is connected");
        client.Chat.Send("请帮我准备一个演示任务");
        yield return Until(() => !client.Chat.Busy && client.Chat.History.messages.LastOrDefault()?.drafts.Count == 1,
            "Offline Chat returns an editable Chinese task draft");
        var draft = client.Chat.History.messages.Last().drafts[0];
        var confirm = client.Chat.ConfirmAsync(draft, draft.task);
        foreach (var delay in Wait(confirm)) yield return delay;
        Check(confirm.Result, "Draft confirmed through the normal task command");
        var loaded = ReloadAsync();
        foreach (var delay in Wait(loaded)) yield return delay;
        var saved = loaded.Result;
        Check(saved.masterStage == 2 && saved.equipment.Any(item => item.id == "boss3-weapon" && item.level == 24)
            && saved.tasks.Any(task => task.id == draft.task.id) && saved.tickets == tickets + 3,
            "Master, upgrade, mail and Chat task persisted to the isolated database");

        entry.onClick.Invoke();
        yield return Until(() => !Field<bool>(mail, "refreshing"), "Inbox ready to show the update announcement");
        rows.GetChild(0).GetComponent<Button>().onClick.Invoke();
        action.onClick.Invoke();
        var updatePanel = UnityEngine.Object.FindFirstObjectByType<AppUpdatePanel>();
        Check(updatePanel && updatePanel.IsOpen, "Update announcement opens the authored Settings popup");
        yield return .4;
        yield return Capture(capture, "update-available-1280");
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Popup_Update.prefab");
        var vendor = UnityEngine.Object.Instantiate(reference, updatePanel.transform.parent);
        vendor.transform.SetAsLastSibling();
        yield return Capture(capture, "update-reference-1280");
        UnityEngine.Object.Destroy(vendor);
        yield return .2;
        updatePanel.primary.onClick.Invoke();
        Check(updates.State == AppUpdateState.Ready && updatePanel.primaryLabel.text == "Apply demo update" && updatePanel.primary.interactable,
            "The simulated download is ready to apply");
        yield return Capture(capture, "update-ready-1280");
        typeof(SettingsPlayVerification).GetMethod("Resolution", Hidden).Invoke(null, new object[] { new Vector2Int(1920, 1080) });
        yield return .4;
        Check(Screen.width == 1920 && Screen.height == 1080, "Update popup uses the requested Game View resolution");
        yield return Capture(capture, "update-ready-1920");
        updatePanel.primary.onClick.Invoke();
        yield return Until(() => updates.State == AppUpdateState.Current && updates.InstalledVersion != Application.version,
            "Applying the update records the demo version");
        updatePanel.Dismiss();
        updatePanel.GetComponent<SettingsPanelUI>().Close();
        entry.onClick.Invoke();
        yield return Until(() => !Field<bool>(mail, "refreshing") && rows.childCount == 3, "Inbox refreshes after the simulated update");
        rows.GetChild(0).GetComponent<Button>().onClick.Invoke();
        tickets = service.Tickets;
        yield return Until(() => action.isActiveAndEnabled && action.interactable, "Installed-version gift is available");
        action.onClick.Invoke();
        yield return Until(() => service.Tickets == tickets + 8 && !Field<bool>(mail, "claiming"), "Update gift commits");
        action.onClick.Invoke();
        yield return .2;
        Check(service.Tickets == tickets + 8 && !action.interactable, "The upgrade gift is claimable once");
        close.onClick.Invoke();

        if (File.Exists("Builds/Windows/CatQuest.exe"))
        {
            int widgetPreference = PlayerPrefs.GetInt("PetWidgetEnabled", -1);
            var companion = CompanionHost.Ensure();
            companion.SetEnabled(true);
            try
            {
                yield return Until(() => companion.IsOpen && Field<bool>(companion, "authenticated"),
                    "Windows companion authenticates to the demo", 35);
                var snapshot = Field<CompanionState>(companion, "lastSnapshot");
                Check(snapshot?.playerName == service.PlayerName && snapshot.ownerId == companion.OwnerId,
                    "Companion receives the isolated demo snapshot");
            }
            finally { companion.SetEnabled(false); }
            Check(!companion.IsOpen && PlayerPrefs.GetInt("PetWidgetEnabled", -1) == widgetPreference,
                "Companion closes without changing the regular widget preference");
        }
    }
}
