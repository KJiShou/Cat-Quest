using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class WebVerification
{
    const string Request = "Temp/verify-web.request";
    sealed class Storage
    {
        public string value = "";
        public bool fail;
        public BrowserPlayerSave Open() => new BrowserPlayerSave(() => value, (expected, next) => {
            if (fail) return "Storage quota exceeded";
            if (expected != value) return "Changed in another tab";
            value = next; return null;
        });
        public PlayerDataStore Store() => new PlayerDataStore("isolated-browser-save.json", Open());
    }
    static WebVerification() { EditorApplication.update += Poll; }
    static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        string folder = "Library/WebVerification/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(folder);
        try {
            Run(); EquipmentVerification.Run();
            File.WriteAllText(folder + "/result.txt", "PASS " + DateTime.UtcNow.ToString("O") +
                "\nBrowser store: task success, failed save, retry, duplicate completion, reload, stale writer, recovery, future version, Focus interruption.\nEquipmentVerification aggregate passed.");
            File.WriteAllText("Temp/verify-web.result", folder + "/result.txt");
        } catch (Exception error) {
            File.WriteAllText(folder + "/result.txt", "FAIL " + error);
            File.WriteAllText("Temp/verify-web.result", folder + "/result.txt"); Debug.LogException(error);
        }
    }
    public static void Run()
    {
        var storage = new Storage(); var store = storage.Store();
        var service = new TaskService(store, new SystemTaskClock());
        Check(service.Upsert(new QuestTask { title = "Browser regression", difficulty = TaskDifficulty.Normal }, null, out string id), service.LastError);
        string before = storage.value; int coins = service.FishCoins;
        storage.fail = true;
        Check(!service.Complete(id, true) && service.FishCoins == coins && storage.value == before, "A failed browser save granted a reward or changed stored data.");
        storage.fail = false;
        Check(service.Complete(id, true), service.LastError);
        int rewarded = service.FishCoins;
        service.Complete(id, true);
        Check(service.FishCoins == rewarded && store.Data.history.Count == 1, "Duplicate browser completion rewarded twice.");
        var reload = storage.Store();
        Check(reload.Data.fishCoins == rewarded && reload.Data.history.Count == 1, "Browser progress did not survive reload.");
        var stale = storage.Store();
        var next = PlayerDataStore.Copy(reload.Data); next.username = "New owner";
        Check(reload.TrySave(next, out _), "Fresh browser writer failed.");
        next = PlayerDataStore.Copy(stale.Data); next.username = "Stale owner";
        Check(!stale.TrySave(next, out _) && storage.Store().Data.username == "New owner", "A stale tab overwrote browser progress.");
        var record = JsonUtility.FromJson<RecoveryRecord>(storage.value);
        record.primary = "corrupt"; storage.value = JsonUtility.ToJson(record);
        var recovery = storage.Store();
        Check(recovery.LoadNotice != null && recovery.Data.history.Count == 1, "Browser backup recovery failed.");
        Check(recovery.TrySave(PlayerDataStore.Copy(recovery.Data), out _), "Recovered browser data could not save.");
        record = JsonUtility.FromJson<RecoveryRecord>(storage.value);
        Check(record.corrupt == "corrupt" && !string.IsNullOrEmpty(record.backup), "Corrupt browser save replaced the valid recovery copy.");
        record.primary = "{\"version\":999}"; storage.value = JsonUtility.ToJson(record);
        var future = storage.Store(); before = storage.value;
        Check(future.WriteBlockReason != null && !future.TrySave(new PlayerData(), out _) && storage.value == before, "Future browser save was overwritten.");
        var focusStorage = new Storage(); var focusStore = focusStorage.Store();
        var data = PlayerDataStore.Copy(focusStore.Data); data.experience = 100000;
        Check(focusStore.TrySave(data, out _), "Focus fixture save failed.");
        var tasks = new TaskService(focusStore, new SystemTaskClock());
        var focus = new FocusController(tasks);
        Check(focus.Start(), "Browser Focus did not start: " + focus.Message);
        focus.Advance(11);
        Check(focusStore.Data.focusSessions.Single().status == FocusSessionStatus.Invalid && tasks.FishCoins == 0, "Suspended browser Focus granted rewards.");
        Check(focus.Start(), "Browser Focus restart failed.");
        new TaskService(focusStorage.Store(), new SystemTaskClock());
        Check(focusStorage.Store().Data.focusSessions.All(s => s.status != FocusSessionStatus.Running), "Reload failed to invalidate an interrupted browser session.");
    }
    [Serializable] sealed class RecoveryRecord { public int format = 1; public string primary, backup, beforeTutorial, corrupt; }
}
