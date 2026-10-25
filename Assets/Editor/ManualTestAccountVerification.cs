using System;
using System.IO;
using System.Linq;
using UnityEditor;
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
            // Validate switching off isolation without ever initializing the real data store.
            Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, path);
            ManualTestAccount.UseNormalAccount();
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManualTestAccount.StoreVariable)), "Normal selection clears override");
            File.WriteAllText(Result, "PASS " + context + "\nIsolated seed, mastery, equipment cap, Master entry/victory, duplicate settlement, repeated selection, reload, normal selection.\nArtifacts: " + folder);
            Debug.Log("Manual test account verification: PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL " + context + "\n" + error);
            Debug.LogException(error);
        }
        finally { Environment.SetEnvironmentVariable(ManualTestAccount.StoreVariable, previous); }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
