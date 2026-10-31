using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ManualTestAccountVerification
{
    const string Request = "Temp/verify-manual-test-account.request";
    const string Result = "Temp/verify-manual-test-account.result";

    static ManualTestAccountVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!ManualTestAccount.CanSwitch || !File.Exists(Request)) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Cat Quest/Test Account/Verify Isolated Profile")]
    public static void Run()
    {
        if (!ManualTestAccount.CanSwitch) return;
        string previous = Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable);
        bool hadPreference = EditorPrefs.HasKey(ManualTestAccount.PreferenceKey);
        int previousPreference = EditorPrefs.GetInt(ManualTestAccount.PreferenceKey);
        var previousStartScene = EditorSceneManager.playModeStartScene;
        bool hadStartScene = EditorPrefs.HasKey(ManualTestAccount.StartSceneKey);
        string previousStartPreference = EditorPrefs.GetString(ManualTestAccount.StartSceneKey);
        string folder = Path.GetFullPath("Library/ManualTestAccountVerification/" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "save.json");
        string context = DateTime.UtcNow.ToString("o") + " | Unity " + Application.unityVersion;
        Directory.CreateDirectory(folder);
        try
        {
            ManualTestAccount.EnsureSave(path);
            var store = new PlayerDataStore(path);
            var service = new TaskService(store, new SystemTaskClock());
            TaskService.CatLevel(store.Data.experience, out int level, out _, out _);
            Check(level == 30 && store.Data.equipment.Count == 9, "Late-game profile");
            Check(store.Data.equipment.All(item => item.level <= level), "Equipment level cap");
            Check(store.Data.bossProgress.All(p => p.wins == 15 && p.claimedComponents == 3 && p.pendingClaims == 0), "Mastery consistency");
            Check(string.IsNullOrEmpty(store.Data.account.boundUid) && !store.Data.account.uploadEnabled
                && store.Data.tasks.Count == 0, "No real account or private tasks copied");
            Check(service.BattleEntryError(1, 1) == null, "Master unlocked");
            Check(service.BeginBattle(1, 1, out var battle), "Master entry");
            Check(service.ResolveBattle(battle.id, BattleOutcome.Victory), "Master settlement");
            Check(store.Data.masterStage == 2 && store.Data.tickets == 99, "Actual simulated victory advances stage");
            int experience = store.Data.experience;
            Check(service.ResolveBattle(battle.id, BattleOutcome.Victory)
                && store.Data.experience == experience, "Duplicate settlement");
            byte[] saved = File.ReadAllBytes(path);
            ManualTestAccount.EnsureSave(path);
            Check(saved.SequenceEqual(File.ReadAllBytes(path)), "Selecting again preserves progress");
            var reloaded = new PlayerDataStore(path);
            Check(reloaded.Data.masterStage == 2 && reloaded.Data.tickets == 99, "Reload persistence");
            string beginnerPath = Path.Combine(folder, "beginner.json");
            ManualTestAccount.EnsureSave(beginnerPath, true);
            var beginner = new PlayerDataStore(beginnerPath);
            TaskService.CatLevel(beginner.Data.experience, out int beginnerLevel, out _, out _);
            Check(beginnerLevel == 1 && beginner.Data.fishCoins == 0 && beginner.Data.tickets == 0
                && !beginner.Data.equipmentUnlocked && beginner.Data.bossProgress.All(p => p.wins == 0)
                && beginner.Data.tutorial != null && beginner.Data.tutorial.read.Count == 0,
                "Beginner uses new-player progression and onboarding");
            Check(string.IsNullOrWhiteSpace(beginner.Data.username) && beginner.Data.experience == 0
                && beginner.Data.history.Count == 0 && beginner.Data.focusSessions.Count == 0
                && beginner.Data.battles.Count == 0 && beginner.Data.rewardDays.Count == 0,
                "Blank beginner does not bypass story or contain progress");
            EditorPrefs.DeleteKey(ManualTestAccount.StartSceneKey);
            ManualTestAccount.ConfigureStartScene(ManualTestAccount.Profile.Beginner);
            Check(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == "Assets/Scenes/IntroStory.unity",
                "Beginner starts from story regardless of open scene");
            ManualTestAccount.ConfigureStartScene(ManualTestAccount.Profile.Beginner);
            ManualTestAccount.ConfigureStartScene(ManualTestAccount.Profile.Original);
            Check(EditorSceneManager.playModeStartScene == previousStartScene, "Repeated beginner selection preserves prior start scene");
            Check(beginner.Data.tasks.Count == 0 && string.IsNullOrEmpty(beginner.Data.account.boundUid)
                && !beginner.Data.account.uploadEnabled, "Beginner has no private data or cloud identity");
            var candidate = PlayerDataStore.Copy(beginner.Data);
            candidate.fishCoins = 17;
            Check(beginner.TrySave(candidate, out string saveError), saveError);
            byte[] beginnerSaved = File.ReadAllBytes(beginnerPath);
            foreach (var profile in new[] { ManualTestAccount.Profile.Beginner, ManualTestAccount.Profile.LateGame,
                ManualTestAccount.Profile.Beginner })
            {
                EditorPrefs.SetInt(ManualTestAccount.PreferenceKey, (int)profile);
                Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, null);
                ManualTestAccount.RestoreSelection(beginnerPath, path);
                Check(ManualTestAccount.Selected == profile && Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable)
                    == (profile == ManualTestAccount.Profile.Beginner ? beginnerPath : path), "Remembered profile restored");
            }
            Check(beginnerSaved.SequenceEqual(File.ReadAllBytes(beginnerPath)) && saved.SequenceEqual(File.ReadAllBytes(path)),
                "Switching profiles preserves both saves byte-for-byte");
            Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, "isolated-verifier-sentinel");
            ManualTestAccount.RestoreSelection(beginnerPath, path);
            Check(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable) == "isolated-verifier-sentinel",
                "Automated verifier override takes priority");
            File.Copy(beginnerPath, beginnerPath + ".bak", true);
            File.Delete(beginnerPath);
            ManualTestAccount.EnsureSave(beginnerPath, true);
            Check(!File.Exists(beginnerPath) && new PlayerDataStore(beginnerPath).Data.fishCoins == 17,
                "Backup-only profile is recovered, never reseeded");
            Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, null);
            string blockedPath = Path.Combine(path, "cannot-create.json");
            bool failed = false;
            try { ManualTestAccount.RestoreSelection(blockedPath, path); }
            catch (IOException) { failed = true; }
            Check(failed && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable)),
                "Failed seed does not change active override");
            ManualTestAccount.RestoreSelection(beginnerPath, path);
            Check(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable) == beginnerPath, "Selection retries after failure");
            // Validate switching off isolation without ever initializing the real data store.
            Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, path);
            ManualTestAccount.UseNormalAccount();
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable)), "Normal selection clears override");
            Check(ManualTestAccount.Selected == ManualTestAccount.Profile.Original, "Original selection persisted");
            ManualTestAccount.RestoreSelection(beginnerPath, path);
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable)), "Original remains selected on restore");
            VerifyReset(folder);
            Check(saved.SequenceEqual(File.ReadAllBytes(path)), "Reset leaves other profile untouched");
            File.WriteAllText(Result, "PASS " + context + "\nBeginner reset: blank reload, byte-exact primary/backup archive, repeated reset, backup failure/retry, newer-save protection, other-profile preservation. Blank beginner/name/story entry, start-scene selection/restoration, isolated late-game and beginner seeds, mastery, equipment cap, Master entry/victory, duplicate settlement, repeated selection, reload, remembered selection restoration, verifier priority, backup-only recovery, seed failure/retry, original selection.\nArtifacts: " + folder);
            Debug.Log("Manual test account verification: PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL " + context + "\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            if (hadPreference) EditorPrefs.SetInt(ManualTestAccount.PreferenceKey, previousPreference);
            else EditorPrefs.DeleteKey(ManualTestAccount.PreferenceKey);
            Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, previous);
            EditorSceneManager.playModeStartScene = previousStartScene;
            if (hadStartScene) EditorPrefs.SetString(ManualTestAccount.StartSceneKey, previousStartPreference);
            else EditorPrefs.DeleteKey(ManualTestAccount.StartSceneKey);
        }
    }

    static void VerifyReset(string folder)
    {
        string path = Path.Combine(folder, "reset", "beginner.json");
        ManualTestAccount.EnsureSave(path);
        var store = new PlayerDataStore(path);
        var progressed = PlayerDataStore.Copy(store.Data);
        progressed.tasks.Add(new QuestTask { id = "test", title = "Old test task" });
        progressed.tutorial = new TutorialProgress { version = 1 };
        progressed.tutorial.read.Add("old-tutorial");
        Check(store.TrySave(progressed, out string error), error);
        byte[] primary = File.ReadAllBytes(path), previous = File.ReadAllBytes(path + ".bak");
        string backup = ManualTestAccount.ResetBeginnerSave(path);
        Check(primary.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json")))
            && previous.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json.bak"))), "Reset archives original bytes");
        var blank = new PlayerDataStore(path).Data;
        Check(string.IsNullOrWhiteSpace(blank.username) && blank.experience == 0 && blank.fishCoins == 0
            && blank.tickets == 0 && !blank.equipmentUnlocked && blank.tasks.Count == 0
            && blank.bossProgress.All(p => p.wins == 0) && blank.tutorial.read.Count == 0,
            "Reset clears name, progression, tasks and tutorial after reload");
        string again = ManualTestAccount.ResetBeginnerSave(path);
        Check(again != backup && primary.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json"))),
            "Repeated reset retains previous archive");
        string failPath = Path.Combine(folder, "reset-failure", "beginner.json");
        ManualTestAccount.EnsureSave(failPath);
        byte[] beforeFailure = File.ReadAllBytes(failPath);
        string blocker = Path.Combine(Path.GetDirectoryName(failPath), "Backups");
        File.WriteAllText(blocker, "Block backup directory creation");
        bool failed = false;
        try { ManualTestAccount.ResetBeginnerSave(failPath); }
        catch (IOException) { failed = true; }
        Check(failed && beforeFailure.SequenceEqual(File.ReadAllBytes(failPath)), "Backup failure preserves original save");
        File.Delete(blocker);
        ManualTestAccount.ResetBeginnerSave(failPath);
        Check(string.IsNullOrWhiteSpace(new PlayerDataStore(failPath).Data.username), "Reset retry succeeds");
        string future = Path.Combine(folder, "future-reset.json");
        const string futureJson = "{\"version\":999,\"username\":\"Keep\"}";
        File.WriteAllText(future, futureJson);
        failed = false;
        try { ManualTestAccount.ResetBeginnerSave(future); }
        catch (IOException) { failed = true; }
        Check(failed && File.ReadAllText(future) == futureJson, "Reset respects unsupported save protection");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
