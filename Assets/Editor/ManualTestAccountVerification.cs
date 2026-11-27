using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ManualTestAccountVerification
{
    const string Request = "Temp/verify-manual-test-account.request";
    const string Result = "Temp/verify-manual-test-account.result";
    static bool running;

    static ManualTestAccountVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (running || !ManualTestAccount.CanSwitch || !File.Exists(Request)) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Cat Quest/Test Account/Verify Isolated Profile")]
    public static async void Run()
    {
        if (running || !ManualTestAccount.CanSwitch) return;
        running = true;
        string folder = Path.GetFullPath("Library/ManualTestAccountVerification/" + Guid.NewGuid().ToString("N"));
        string context = DateTime.UtcNow.ToString("o") + " | Unity " + Application.unityVersion;
        try
        {
            Directory.CreateDirectory(folder);
            await VerifySqlite(folder);
            if (!ManualTestAccount.CanSwitch) throw new InvalidOperationException("The Editor left Edit Mode during verification.");
            await VerifyLegacyAndSelection(folder);
            File.WriteAllText(Result, "PASS " + context + "\nLegacy and SQLite beginner reset, standalone backup, failure/retry, repeated reset and close/reload. SQLite late-game migration, Master victory and duplicate settlement. SQLite save selection and Demo seed isolation. Remembered profile, original selection, verifier priority, and start-scene restoration.\nArtifacts: " + folder);
            Debug.Log("Manual test account verification: PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL " + context + "\n" + error + "\nArtifacts: " + folder);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    static async Task VerifyLegacyAndSelection(string folder)
    {
        string previous = Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable);
        bool hadPreference = EditorPrefs.HasKey(ManualTestAccount.PreferenceKey);
        int previousPreference = EditorPrefs.GetInt(ManualTestAccount.PreferenceKey);
        var previousStartScene = EditorSceneManager.playModeStartScene;
        bool hadStartScene = EditorPrefs.HasKey(ManualTestAccount.StartSceneKey);
        string previousStartPreference = EditorPrefs.GetString(ManualTestAccount.StartSceneKey);
        string path = Path.Combine(folder, "save.json");
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
            await VerifyReset(folder);
            Check(saved.SequenceEqual(File.ReadAllBytes(path)), "Reset leaves other profile untouched");
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

    static async Task VerifyReset(string folder)
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
        string backup = await ManualTestAccount.ResetBeginnerSaveAsync(path);
        Check(primary.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json")))
            && previous.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json.bak"))), "Reset archives original bytes");
        var blank = new PlayerDataStore(path).Data;
        Check(string.IsNullOrWhiteSpace(blank.username) && blank.experience == 0 && blank.fishCoins == 0
            && blank.tickets == 0 && !blank.equipmentUnlocked && blank.tasks.Count == 0
            && blank.bossProgress.All(p => p.wins == 0) && blank.tutorial.read.Count == 0,
            "Reset clears name, progression, tasks and tutorial after reload");
        string again = await ManualTestAccount.ResetBeginnerSaveAsync(path);
        Check(again != backup && primary.SequenceEqual(File.ReadAllBytes(Path.Combine(backup, "beginner.json"))),
            "Repeated reset retains previous archive");
        string failPath = Path.Combine(folder, "reset-failure", "beginner.json");
        ManualTestAccount.EnsureSave(failPath);
        byte[] beforeFailure = File.ReadAllBytes(failPath);
        string blocker = Path.Combine(Path.GetDirectoryName(failPath), "Backups");
        File.WriteAllText(blocker, "Block backup directory creation");
        bool failed = false;
        try { await ManualTestAccount.ResetBeginnerSaveAsync(failPath); }
        catch (IOException) { failed = true; }
        Check(failed && beforeFailure.SequenceEqual(File.ReadAllBytes(failPath)), "Backup failure preserves original save");
        File.Delete(blocker);
        await ManualTestAccount.ResetBeginnerSaveAsync(failPath);
        Check(string.IsNullOrWhiteSpace(new PlayerDataStore(failPath).Data.username), "Reset retry succeeds");
        string future = Path.Combine(folder, "future-reset.json");
        const string futureJson = "{\"version\":999,\"username\":\"Keep\"}";
        File.WriteAllText(future, futureJson);
        failed = false;
        try { await ManualTestAccount.ResetBeginnerSaveAsync(future); }
        catch (IOException) { failed = true; }
        Check(failed && File.ReadAllText(future) == futureJson, "Reset respects unsupported save protection");
    }

    static async Task VerifySqlite(string folder)
    {
        string latePath = Path.Combine(folder, "sqlite-late", "save.json");
        ManualTestAccount.EnsureSave(latePath);
        var late = OpenProfile(latePath);
        try
        {
            var service = new SqlitePlayerService(late, new SystemTaskClock());
            Require(await service.InitializeAsync(), "Late-game SQLite startup");
            var data = Require(await late.ReadAllForVerificationAsync(), "Late-game SQLite data");
            Check(data.masterStage == 1 && data.tickets == 100 && data.equipment.Count == 9
                && service.BattleEntryError(1, 1) == null, "Late-game profile retains progression and can enter Master Stage");
            var attempt = Require(await service.BeginBattleAsync(1, 1), "SQLite Master Stage entry");
            Require(await service.ResolveBattleAsync(attempt.id, BattleOutcome.Victory), "SQLite Master Stage victory");
            Require(await service.ResolveBattleAsync(attempt.id, BattleOutcome.Victory), "SQLite repeated victory");
        }
        finally { await late.CloseAsync(); }
        Check(ManualTestAccount.GetSaveToShow(latePath) == late.DatabasePath, "Show Save prefers SQLite over JSON");
        File.Delete(latePath);
        File.Delete(latePath + ".bak");
        ManualTestAccount.EnsureSave(latePath);
        Check(!File.Exists(latePath), "Selecting a SQLite-only profile does not reseed JSON");

        string path = Path.Combine(folder, "sqlite-reset", "beginner.json");
        ManualTestAccount.EnsureSave(path, true);
        var beginner = OpenProfile(path);
        const string mailId = "verification-mail";
        try
        {
            var service = new SqlitePlayerService(beginner, new SystemTaskClock());
            Require(await service.InitializeAsync(), "Beginner SQLite startup");
            Check(string.IsNullOrEmpty(service.PlayerName), "Fresh SQLite beginner has no name");
            Require(await service.RenamePlayerAsync("Progressed Beginner"), "Save name to SQLite");
            Require(await beginner.ExecuteAsync(tx => {
                tx.Put(PlayerRecordKind.Tasks, new QuestTask { id = "old-task", title = "Old task" });
                tx.Put(PlayerRecordKind.Tutorial, new TutorialProgress { version = 1, read = { "old-tutorial" } });
                tx.SetMailState(mailId, 1);
                return true;
            }), "Save task, mail and tutorial progress to SQLite");

            string blocker = Path.Combine(Path.GetDirectoryName(path), "blocked-backup");
            File.WriteAllText(blocker, "Block SQLite backup directory");
            var failed = await beginner.ResetEditorProfileAsync(Path.Combine(blocker, "save.sqlite3"));
            Check(!failed.Succeeded, "SQLite can report a snapshot failure");
            beginner.FaultPoint = stage => {
                if (stage == "import-before-commit") throw new IOException("Injected reset transaction failure.");
            };
            var rolledBack = await beginner.ResetEditorProfileAsync(Path.Combine(folder, "before-failed-reset.sqlite3"));
            Check(!rolledBack.Succeeded, "SQLite can report a reset transaction failure");
            var unchanged = Require(await beginner.ReadAllForVerificationAsync(), "Read after failed reset");
            Check(unchanged.username == "Progressed Beginner" && unchanged.tasks.Count == 1
                && unchanged.tutorial.read.Count == 1, "Backup and transaction failures preserve progress");
        }
        finally { await beginner.CloseAsync(); }

        byte[] legacy = File.ReadAllBytes(path);
        string backup = await ManualTestAccount.ResetBeginnerSaveAsync(path);
        string snapshotPath = Path.Combine(backup, Path.GetFileName(beginner.DatabasePath));
        var snapshot = new SqlitePlayerRepository(snapshotPath);
        try
        {
            Require(await snapshot.InitializeAsync(), "Open standalone pre-reset SQLite backup");
            var archived = Require(await snapshot.ReadAllForVerificationAsync(), "Read pre-reset data from backup");
            Check(archived.username == "Progressed Beginner" && archived.tasks.Single().id == "old-task"
                && archived.tutorial.read.Contains("old-tutorial")
                && Require(await snapshot.ReadAsync(tx => tx.ReadMailState(mailId)), "Read archived mail") == 1,
                "Backup includes committed SQLite data and mail");
        }
        finally { await snapshot.CloseAsync(); }
        Check(legacy.SequenceEqual(File.ReadAllBytes(path)), "SQLite reset preserves the legacy source");
        string again = await ManualTestAccount.ResetBeginnerSaveAsync(path);
        Check(again != backup && File.Exists(snapshotPath), "Repeated SQLite reset keeps previous backup");
        beginner = OpenProfile(path);
        try
        {
            var service = new SqlitePlayerService(beginner, new SystemTaskClock());
            Require(await service.InitializeAsync(), "Reopen reset beginner through the runtime service");
            var blank = Require(await beginner.ReadAllForVerificationAsync(), "Read reset data");
            Check(string.IsNullOrWhiteSpace(service.PlayerName) && blank.experience == 0 && blank.tickets == 0
                && blank.tasks.Count == 0 && blank.tutorial.read.Count == 0
                && Require(await beginner.ReadAsync(tx => tx.ReadMailState(mailId)), "Read reset mail") == 0,
                "Reset clears SQLite data and restores the name-entry condition");
        }
        finally { await beginner.CloseAsync(); }
        late = OpenProfile(latePath);
        try
        {
            Require(await late.InitializeAsync(), "Reopen late-game after resetting beginner");
            var data = Require(await late.ReadAllForVerificationAsync(), "Read separate late-game data");
            Check(data.masterStage == 2 && data.tickets == 99, "Duplicate battle settlement and resetting Beginner preserve late-game progress");
        }
        finally { await late.CloseAsync(); }

        string demoPath = Path.Combine(folder, "demo", "demo.json");
        ManualTestAccount.EnsureDemoSave(demoPath);
        var demo = OpenProfile(demoPath);
        try
        {
            Require(await demo.InitializeAsync(), "Demo SQLite startup");
            var data = Require(await demo.ReadAllForVerificationAsync(), "Read demo seed");
            Check(data.username == "Cat Quest Demo" && data.tasks.Any(task => task.id == "demo-level-up")
                && !data.account.uploadEnabled, "Demo uses isolated sample data");
        }
        finally { await demo.CloseAsync(); }
    }

    static SqlitePlayerRepository OpenProfile(string path) => new SqlitePlayerRepository(Path.ChangeExtension(path, ".sqlite3"), path);

    static T Require<T>(OperationResult<T> result, string label)
    {
        Check(result.Succeeded, label + ": " + result.Message);
        return result.Value;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
