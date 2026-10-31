using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only local profiles. Automated verifiers retain priority over this selection.
[InitializeOnLoad]
public static class ManualTestAccount
{
    internal enum Profile { Original, Beginner, LateGame }
    internal const string StoreVariable = "CATQUEST_TASK_TEST_STORE";
    const string MenuRoot = "Tools/Cat Quest/Test Account/";
    internal static string PreferenceKey => "CatQuest.ManualProfile." + Application.dataPath;
    internal static string StartSceneKey => PreferenceKey + ".previousStartScene";
    internal static string SavePath => Path.GetFullPath("Library/ManualTestAccount/save.json");
    internal static string BeginnerSavePath => Path.GetFullPath("Library/ManualTestAccount/beginner.json");
    internal static Profile Selected => (Profile)EditorPrefs.GetInt(PreferenceKey, (int)Profile.Original);
    internal static bool CanSwitch => !EditorApplication.isPlayingOrWillChangePlaymode
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
            if (Selected == Profile.Beginner && string.Equals(Environment.GetEnvironmentVariable(StoreVariable),
                BeginnerSavePath, StringComparison.OrdinalIgnoreCase)) ConfigureStartScene(Profile.Beginner);
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    internal static void RestoreSelection(string beginnerPath = null, string lateGamePath = null)
    {
        // Never replace a verifier's disposable store with a manual or live save.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StoreVariable))) return;
        ApplySelection(Selected, beginnerPath, lateGamePath);
    }

    static void ApplySelection(Profile profile, string beginnerPath = null, string lateGamePath = null)
    {
        string path = profile == Profile.Beginner ? beginnerPath ?? BeginnerSavePath
            : profile == Profile.LateGame ? lateGamePath ?? SavePath : null;
        if (path != null) EnsureSave(path, profile == Profile.Beginner);
        Environment.SetEnvironmentVariable(StoreVariable, path);
    }

    static void Select(Profile profile)
    {
        if (!CanSwitch) return;
        try
        {
            ApplySelection(profile);
            EditorPrefs.SetInt(PreferenceKey, (int)profile);
            ConfigureStartScene(profile);
            Debug.Log(profile + " account selected and remembered for future Play sessions. Progress is retained. Save: "
                + (Environment.GetEnvironmentVariable(StoreVariable)
                    ?? Path.Combine(Application.persistentDataPath, "catquest-data.json")));
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    internal static void ConfigureStartScene(Profile profile)
    {
        if (profile == Profile.Beginner)
        {
            var intro = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/IntroStory.unity");
            if (!intro) throw new IOException("IntroStory scene is missing.");
            if (!EditorPrefs.HasKey(StartSceneKey))
                EditorPrefs.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = intro;
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

    [MenuItem(MenuRoot + "Reset Beginner Account")]
    public static void ResetBeginnerAccount()
    {
        if (!CanSwitch) return;
        try
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/IntroStory.unity"))
                throw new IOException("IntroStory scene is missing.");
            string backup = ResetBeginnerSave(BeginnerSavePath);
            Select(Profile.Beginner);
            Debug.Log("Beginner account reset. Press Play to enter your name and start the story. Backup: " + backup);
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    [MenuItem(MenuRoot + "Reset Beginner Account", true)]
    static bool ValidateResetBeginner() => CanSwitch;

    // The menu always supplies BeginnerSavePath; verification supplies a disposable store.
    internal static string ResetBeginnerSave(string path)
    {
        var store = new PlayerDataStore(path);
        if (store.WriteBlockReason != null) throw new IOException(store.WriteBlockReason);
        string backup = Path.Combine(Path.GetDirectoryName(path), "Backups",
            "beginner-reset-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        foreach (string suffix in new[] { "", ".bak", ".tmp", ".before-tutorial.bak" })
        {
            string source = path + suffix;
            if (!File.Exists(source)) continue;
            string target = Path.Combine(backup, Path.GetFileName(source));
            File.Copy(source, target);
            if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)))
                throw new IOException("Beginner backup verification failed: " + target);
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
    static bool Validate(string menu, Profile profile)
    {
        Menu.SetChecked(MenuRoot + menu, Selected == profile);
        return CanSwitch;
    }

    [MenuItem(MenuRoot + "Show Selected Save")]
    static void ShowSave()
    {
        string path = Selected == Profile.Beginner ? BeginnerSavePath : Selected == Profile.LateGame
            ? SavePath : Path.Combine(Application.persistentDataPath, "catquest-data.json");
        if (File.Exists(path)) EditorUtility.RevealInFinder(path);
        else Debug.Log("Selected profile has no primary save yet. Save: " + path);
    }

    internal static void EnsureSave(string path, bool beginner = false)
    {
        // Never reset an existing profile, including its recoverable backup.
        if (File.Exists(path) || File.Exists(path + ".bak")) return;
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
