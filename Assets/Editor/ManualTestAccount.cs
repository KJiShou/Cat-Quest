using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only local profiles. Automated verifiers retain priority over this selection.
[InitializeOnLoad]
public static class ManualTestAccount
{
    internal enum Profile { Original, Beginner, LateGame, Demo }
    internal const string StoreVariable = "CATQUEST_TASK_TEST_STORE";
    const string MenuRoot = "Tools/Cat Quest/Test Account/";
    internal static string PreferenceKey => "CatQuest.ManualProfile." + Application.dataPath;
    internal static string StartSceneKey => PreferenceKey + ".previousStartScene";
    internal static string SavePath => Path.GetFullPath("Library/ManualTestAccount/save.json");
    internal static string BeginnerSavePath => Path.GetFullPath("Library/ManualTestAccount/beginner.json");
    static string DemoRunKey => PreferenceKey + ".demoRun";
    internal static string DemoSavePath => DemoProfile.SavePath(EditorPrefs.GetString(DemoRunKey, "default"));
    internal static Profile Selected => (Profile)EditorPrefs.GetInt(PreferenceKey, (int)Profile.Original);
    static bool resetting;
    internal static bool CanSwitch => !resetting && !EditorApplication.isPlayingOrWillChangePlaymode
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

    static ManualTestAccount()
    {
        // Retain an already-selected legacy late-game profile on first upgrade.
        if (!EditorPrefs.HasKey(PreferenceKey) && string.Equals(
            Environment.GetEnvironmentVariable(StoreVariable), SavePath, StringComparison.OrdinalIgnoreCase))
            EditorPrefs.SetInt(PreferenceKey, (int)Profile.LateGame);
        EditorApplication.delayCall += RestoreWhenIdle;
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (resetting)
            {
                EditorApplication.isPlaying = false;
                Debug.LogWarning("Wait for the Beginner account reset to finish before pressing Play.");
                return;
            }
            try { RestoreSelection(); }
            catch (Exception error) { EditorApplication.isPlaying = false; Debug.LogException(error); }
        };
    }

    static void RestoreWhenIdle()
    {
        if (!CanSwitch) return;
        try
        {
            RestoreSelection();
            string active = Environment.GetEnvironmentVariable(StoreVariable);
            if (Selected == Profile.Original ? string.IsNullOrEmpty(active)
                : string.Equals(active, ProfilePath(Selected), StringComparison.OrdinalIgnoreCase))
                ConfigureStartScene(Selected);
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    internal static void RestoreSelection(string beginnerPath = null, string lateGamePath = null)
    {
        // Never replace a verifier's disposable store with a manual or live save.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StoreVariable))) return;
        string path = ProfilePath(Selected, beginnerPath, lateGamePath);
        PrepareSave(Selected, path);
        Environment.SetEnvironmentVariable(StoreVariable, path);
    }

    static string ProfilePath(Profile profile, string beginnerPath = null, string lateGamePath = null, string demoRun = null)
    {
        return profile == Profile.Beginner ? beginnerPath ?? BeginnerSavePath
            : profile == Profile.LateGame ? lateGamePath ?? SavePath
            : profile == Profile.Demo ? (demoRun == null ? DemoSavePath : DemoProfile.SavePath(demoRun)) : null;
    }

    static void PrepareSave(Profile profile, string path)
    {
        if (profile == Profile.Demo) EnsureDemoSave(path);
        else if (path != null) EnsureSave(path, profile == Profile.Beginner);
    }

    static void Select(Profile profile, string demoRun = null)
    {
        if (!CanSwitch) return;
        try
        {
            string path = ProfilePath(profile, demoRun: demoRun);
            PrepareSave(profile, path);
            ApplySelection(profile, path, demoRun);
            Debug.Log(profile + " account selected and remembered for future Play sessions. Progress is retained. Save: "
                + GetSaveToShow(path ?? OriginalSavePath));
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    static void ApplySelection(Profile profile, string path, string demoRun = null)
    {
        ConfigureStartScene(profile);
        Environment.SetEnvironmentVariable(StoreVariable, path);
        if (demoRun != null) EditorPrefs.SetString(DemoRunKey, demoRun);
        EditorPrefs.SetInt(PreferenceKey, (int)profile);
    }

    internal static void ConfigureStartScene(Profile profile)
    {
        if (profile == Profile.Beginner || profile == Profile.Demo)
        {
            string sceneName = profile == Profile.Demo ? "CharacterHome" : "IntroStory";
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/" + sceneName + ".unity");
            if (!scene) throw new IOException(sceneName + " scene is missing.");
            if (!EditorPrefs.HasKey(StartSceneKey))
                EditorPrefs.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = scene;
        }
        else if (EditorPrefs.HasKey(StartSceneKey))
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorPrefs.GetString(StartSceneKey));
            EditorPrefs.DeleteKey(StartSceneKey);
        }
    }

    [MenuItem(MenuRoot + "Use Beginner Account")]
    public static void UseBeginnerAccount() => Select(Profile.Beginner);
    [MenuItem(MenuRoot + "Use Late Game Account")]
    public static void UseTestAccount() => Select(Profile.LateGame);
    [MenuItem(MenuRoot + "Use Original Account")]
    public static void UseNormalAccount() => Select(Profile.Original);
    [MenuItem(MenuRoot + "Use Demo Account")]
    public static void UseDemoAccount() => Select(Profile.Demo);
    [MenuItem(MenuRoot + "Start Fresh Demo Run")]
    public static void StartFreshDemo() => Select(Profile.Demo, Guid.NewGuid().ToString("N"));

    [MenuItem(MenuRoot + "Reset Beginner Account")]
    public static async void ResetBeginnerAccount()
    {
        if (!CanSwitch) return;
        resetting = true;
        EditorApplication.LockReloadAssemblies();
        try
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/IntroStory.unity"))
                throw new IOException("IntroStory scene is missing.");
            string backup = await ResetBeginnerSaveAsync(BeginnerSavePath);
            ApplySelection(Profile.Beginner, BeginnerSavePath);
            Debug.Log("Beginner account reset. Press Play to enter your name and start the story. Backup: " + backup);
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            resetting = false;
            EditorApplication.UnlockReloadAssemblies();
        }
    }

    [MenuItem(MenuRoot + "Reset Beginner Account", true)]
    static bool ValidateResetBeginner() => CanSwitch;

    // The menu always supplies BeginnerSavePath; verification supplies a disposable store.
    internal static async Task<string> ResetBeginnerSaveAsync(string path)
    {
        string backup = Path.Combine(Path.GetDirectoryName(path), "Backups",
            "beginner-reset-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        string database = DatabasePath(path);
        bool usesSqlite = HasSqliteSave(path);
        var store = usesSqlite ? null : new PlayerDataStore(path);
        if (store?.WriteBlockReason != null) throw new IOException(store.WriteBlockReason);
        Directory.CreateDirectory(backup);
        foreach (string suffix in new[] { "", ".bak", ".tmp", ".before-tutorial.bak", ".before-shop.bak" })
        {
            string source = path + suffix;
            if (!File.Exists(source)) continue;
            string target = Path.Combine(backup, Path.GetFileName(source));
            File.Copy(source, target);
            if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)))
                throw new IOException("Beginner backup verification failed: " + target);
        }
        if (usesSqlite)
        {
            var repository = new SqlitePlayerRepository(database, path);
            try
            {
                var ready = await repository.InitializeAsync();
                if (!ready.Succeeded) throw new IOException(ready.Message);
                var reset = await repository.ResetEditorProfileAsync(Path.Combine(backup, Path.GetFileName(database)));
                if (!reset.Succeeded) throw new IOException(reset.Message);
                return backup;
            }
            finally { await repository.CloseAsync(); }
        }
        // Use the existing atomic writer. A failed backup never reaches this step.
        var blank = new PlayerData { tutorial = new TutorialProgress { version = 1 } };
        if (!store.TrySave(blank, out string error)) throw new IOException(error);
        return backup;
    }

    [MenuItem(MenuRoot + "Use Beginner Account", true)]
    static bool ValidateBeginner() => Validate("Use Beginner Account", Profile.Beginner);
    [MenuItem(MenuRoot + "Use Late Game Account", true)]
    static bool ValidateTest() => Validate("Use Late Game Account", Profile.LateGame);
    [MenuItem(MenuRoot + "Use Original Account", true)]
    static bool ValidateNormal() => Validate("Use Original Account", Profile.Original);
    [MenuItem(MenuRoot + "Use Demo Account", true)]
    static bool ValidateDemo() => Validate("Use Demo Account", Profile.Demo);
    [MenuItem(MenuRoot + "Start Fresh Demo Run", true)]
    static bool ValidateFreshDemo() => CanSwitch;
    static bool Validate(string menu, Profile profile)
    {
        Menu.SetChecked(MenuRoot + menu, Selected == profile);
        return CanSwitch;
    }

    [MenuItem(MenuRoot + "Show Selected Save")]
    static void ShowSave()
    {
        string path = GetSaveToShow(ProfilePath(Selected) ?? OriginalSavePath);
        if (File.Exists(path)) EditorUtility.RevealInFinder(path);
        else Debug.Log("Selected profile has no primary save yet. Save: " + path);
    }

    static string OriginalSavePath => Path.Combine(Application.persistentDataPath, "catquest-data.json");
    static string DatabasePath(string path) => Path.ChangeExtension(path, ".sqlite3");
    static bool HasSqliteSave(string path)
    {
        string database = DatabasePath(path);
        return File.Exists(database) || File.Exists(database + ".migration.json")
            || File.Exists(database + ".recovery-pending.json");
    }

    internal static string GetSaveToShow(string path)
    {
        if (HasSqliteSave(path)) return DatabasePath(path);
        if (File.Exists(path)) return path;
        return File.Exists(path + ".bak") ? path + ".bak" : DatabasePath(path);
    }

    static bool HasSave(string path) => HasSqliteSave(path) || File.Exists(path) || File.Exists(path + ".bak");

    internal static void EnsureDemoSave(string path)
    {
        if (HasSave(path)) return;
        var store = new PlayerDataStore(path);
        if (!store.TrySave(DemoProfile.CreateSave(DateTime.Now), out string error)) throw new IOException(error);
    }

    internal static void EnsureSave(string path, bool beginner = false)
    {
        // Never reset an existing profile, including its recoverable backup.
        if (HasSave(path)) return;
        var store = new PlayerDataStore(path);
        // Use the same defaults (including onboarding) as a genuinely new player.
        var data = beginner ? store.Data : new PlayerData
        {
            username = "TEST - Master Stage", experience = 13050,
            fishCoins = 10000, tickets = 100, equipmentUnlocked = true, masterStage = 1
        };
        if (!beginner)
        {
            for (int boss = 1; boss <= 3; boss++)
                data.bossProgress.Add(new BossProgress { bossId = boss, wins = 15 });
            foreach (var definition in EquipmentRules.Catalog)
            {
                data.equipment.Add(new EquipmentItem { id = definition.Id, level = 30 });
                if (definition.Tier == 3) EquipmentRules.SetEquipped(data, definition.Slot, definition.Id);
            }
        }
        // Fresh local data: no copied tasks, account identity or upload preferences.
        if (!store.TrySave(data, out string error)) throw new IOException(error);
    }
}
