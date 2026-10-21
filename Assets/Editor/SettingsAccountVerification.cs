using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SettingsAccountVerification
{
    sealed class Clock : ITaskClock { public DateTime LocalNow => new DateTime(2026, 10, 21, 12, 0, 0); public DateTime UtcNow => LocalNow.AddHours(-8); }
    sealed class Store : IPlayerDataStore
    {
        public PlayerData Data { get; private set; } = new PlayerData();
        public bool fail;
        public bool TrySave(PlayerData value, out string error) { error = fail ? "Test save failure" : null; if (fail) return false; Data = value; return true; }
    }
    sealed class Http : IAccountHttp
    {
        public readonly Queue<AccountHttpResult> replies = new Queue<AccountHttpResult>();
        public readonly List<string> methods = new List<string>(), bodies = new List<string>();
        public bool pause; public string pauseMethod; public int cancels;
        public IEnumerator Send(string url, string method, string body, string contentType, string bearer, Action<AccountHttpResult> complete)
        {
            methods.Add(method); bodies.Add(body);
            if (pause || pauseMethod == method) yield return null;
            if (replies.Count == 0) throw new Exception("Unexpected account HTTP request: " + method);
            complete(replies.Dequeue());
        }
        public void Cancel() { cancels++; }
    }
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string OldVersion = "2026-10-21T04:00:00.000001Z", NewVersion = "2026-10-21T04:00:01.000001Z";
    static SettingsAccountVerification() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-settings-account.request")) return;
        File.Delete("Temp/verify-settings-account.request");
        try { Run(); } catch (Exception error) { Debug.LogException(error); }
    }
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Set(AccountController c, string name, object value) => typeof(AccountController).GetField(name, Private).SetValue(c, value);
    static T Get<T>(AccountController c, string name) => (T)typeof(AccountController).GetField(name, Private).GetValue(c);
    static IEnumerator Synchronize(AccountController c, bool replace = false) => (IEnumerator)typeof(AccountController).GetMethod("Synchronize", Private).Invoke(c, new object[] { replace });
    static void Drain(IEnumerator routine)
    {
        using var disposable = routine as IDisposable;
        int steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 100) throw new Exception("Account verification did not settle.");
            if (routine.Current is IEnumerator nested) Drain(nested);
        }
    }
    static AccountHttpResult Reply(long code, string body = null) => new AccountHttpResult { code = code, body = body };
    static string Document(AccountAggregate aggregate, string version)
    {
        string commit = AccountRest.BuildAggregateCommit("catquest-test", aggregate, null);
        int start = commit.IndexOf("\"fields\":", StringComparison.Ordinal) + 10;
        int end = commit.IndexOf("}},\"updateTransforms\"", StringComparison.Ordinal);
        string fields = commit.Substring(start, end - start);
        return "{\"updateTime\":" + AccountRest.Quote(version) + ",\"fields\":{" + fields +
            ",\"updatedAt\":{\"timestampValue\":" + AccountRest.Quote(version) + "}}}";
    }
    static AccountController Controller(Store store, Http http, out GameObject host, out AccountConnectionConfig config)
    {
        // An inactive object never runs Awake/Start: no GameDataManager or credentials are touched.
        host = new GameObject("Isolated Account Verification"); host.SetActive(false);
        var prior = AccountController.Instance; var controller = host.AddComponent<AccountController>();
        Check(AccountController.Instance == prior, "Inactive verification object initialized the real account singleton.");
        config = ScriptableObject.CreateInstance<AccountConnectionConfig>();
        config.firebaseProjectId = "catquest-test"; config.firebaseApiKey = "test-key"; config.googleDesktopClientId = "test.apps.googleusercontent.com";
        controller.configuration = config;
        Set(controller, "service", new TaskService(store, new Clock())); Set(controller, "http", http);
        Set(controller, "uid", "test-owner"); Set(controller, "idToken", "fake-token"); Set(controller, "refreshToken", "fake-refresh");
        Set(controller, "expiresAt", DateTimeOffset.UtcNow.AddHours(1));
        return controller;
    }
    static void WithController(Action<AccountController, Store, Http> test)
    {
        var store = new Store(); var http = new Http();
        var controller = Controller(store, http, out var host, out var config);
        try { test(controller, store, http); }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(config); }
    }
    static void Bound(Store store, bool upload = true, string version = "")
    {
        store.Data.account = new AccountPreferences { boundUid = "test-owner", playerName = "Mochi", uploadEnabled = upload, lastServerUpdateTime = version };
    }

    [MenuItem("Tools/Cat Quest/Verify Settings Account")]
    public static void Run()
    {
        Directory.CreateDirectory("Library/SettingsAccountVerification");
        string context = "UTC " + DateTime.UtcNow.ToString("o") + " | Unity " + Application.unityVersion;
        try
        {
            VerifyAggregates(); VerifyOAuthAndWire(); VerifyLocalOwnership(); VerifyPublication();
            File.WriteAllText("Temp/verify-settings-account.result", "PASS: " + context +
                "\nIsolated aggregate caps/duplicates/undo, cumulative Focus seconds, Boss gates, equipped Power, OAuth PKCE/state/path, strict payload, ownership, disk reload, local write failure, conflict/lost response, opt-out and in-flight signout.\nNo Google/Firebase calls or real credentials used. Live sign-in and deployed rules remain unverified.");
            Debug.Log("Settings account verification: PASS");
        }
        catch (Exception error) { File.WriteAllText("Temp/verify-settings-account.result", "FAIL: " + context + "\n" + error); throw; }
    }
    static void VerifyAggregates()
    {
        var store = new Store(); var service = new TaskService(store, new Clock());
        string task = null;
        for (int i = 0; i < 6; i++)
        {
            Check(service.Upsert(new QuestTask { title = "PRIVATE_TASK_SENTINEL", description = "PRIVATE_DESCRIPTION", difficulty = TaskDifficulty.Hard }, null, out task), service.LastError);
            Check(service.Complete(task, true), service.LastError);
        }
        Check(store.Data.history.Last().experience == 0, "Fixture did not cross the task reward cap.");
        var receipt = store.Data.history.Last();
        Check(AccountAggregate.From(service.Snapshot, "test-owner", "Mochi").totalTasksCompleted == 6, "Capped completion missing.");
        Check(service.UndoCompletion(receipt.id), service.LastError);
        Check(AccountAggregate.From(service.Snapshot, "test-owner", "Mochi").totalTasksCompleted == 5, "Undo still counted.");
        Check(service.Complete(task, true) && service.Complete(task, true) && service.Delete(task), service.LastError);
        store.Data.history.Add(PlayerDataStore.Copy(store.Data.history.First()));
        for (int i = 0; i < 3; i++) { Check(service.BeginFocus(new FocusSessionOptions { focusMinutes = 60 }, out string id) && service.FinishFocus(id, 3600, false), service.LastError); }
        for (int i = 0; i < 2; i++) { Check(service.BeginFocus(out string id) && service.FinishFocus(id, 750, true), service.LastError); }
        Check(service.BeginFocus(out string cancelled) && service.CancelFocus(cancelled), service.LastError);
        store.Data.focusSessions.Add(PlayerDataStore.Copy(store.Data.focusSessions.First()));
        var aggregate = AccountAggregate.From(service.Snapshot, "test-owner", "Mochi");
        Check(aggregate.totalTasksCompleted == 6 && aggregate.totalFocusMinutes == 205, "Aggregate deduplication or eligible seconds after cap failed.");
        Check(store.Data.rewardDays.Single().focusMinutes == 120, "Aggregate changed Focus allowance.");
        store.Data.focusSessions.Add(new FocusSession { id = "legacy", status = FocusSessionStatus.EndedEarly, eligibleMinutes = 2 });
        Check(AccountAggregate.From(service.Snapshot, "test-owner", "Mochi").totalFocusMinutes == 207, "Legacy minute fallback failed.");
        var d = service.Snapshot;
        d.experience = 0; d.bossProgress.Single(p => p.bossId == 1).wins = 1;
        Check(AccountAggregate.From(d, "test-owner", "Mochi").bossStage == 1, "Level gate ignored.");
        d.experience = Enumerable.Range(1, 9).Sum(level => 100 + 25 * (level - 1));
        Check(AccountAggregate.From(d, "test-owner", "Mochi").bossStage == 2, "Boss 2 unlock mapping.");
        d.bossProgress.Single(p => p.bossId == 2).wins = 1;
        Check(AccountAggregate.From(d, "test-owner", "Mochi").bossStage == 2, "Boss 3 level gate ignored.");
        d.experience = Enumerable.Range(1, 19).Sum(level => 100 + 25 * (level - 1));
        Check(AccountAggregate.From(d, "test-owner", "Mochi").bossStage == 3, "Boss 3 unlock mapping.");
        d.bossProgress.Single(p => p.bossId == 3).wins = 1; d.masterStage = 4;
        d.equipment.Add(new EquipmentItem { id = "boss1-weapon", level = 1 }); d.equippedWeaponId = "boss1-weapon";
        aggregate = AccountAggregate.From(d, "test-owner", "Mochi");
        Check(aggregate.bossStage == 7 && aggregate.catLevel == 20 && aggregate.power == EquipmentRules.Calculate(d).Final.Power, "Master/final equipped stats mapping.");
        string payload = AccountRest.BuildAggregateCommit("catquest-test", aggregate, OldVersion);
        Check(!payload.Contains("PRIVATE_") && !payload.Contains("tagIds") && !payload.Contains("experience") && !payload.Contains("fishCoins"), "Private player data leaked into payload.");
    }
    static void VerifyOAuthAndWire()
    {
        Check(DesktopGoogleSignIn.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "RFC 7636 PKCE vector failed.");
        Check(DesktopGoogleSignIn.TryReadCallback("/callback/?state=expected&code=a%2Bb", "/callback/", "expected", out var valid) && valid.code == "a+b", "OAuth code parsing.");
        Check(!DesktopGoogleSignIn.TryReadCallback("/callback/?state=wrong&code=x", "/callback/", "expected", out _), "Wrong OAuth state accepted.");
        Check(!DesktopGoogleSignIn.TryReadCallback("/other/?state=expected&code=x", "/callback/", "expected", out _), "Wrong callback path accepted.");
        Check(!DesktopGoogleSignIn.TryReadCallback("/callback/?state=expected&state=expected&code=x", "/callback/", "expected", out _), "Duplicate callback parameters accepted.");
        Check(DesktopGoogleSignIn.TryReadCallback("/callback/?state=expected&error=access_denied", "/callback/", "expected", out var denied) && denied.error != null, "Consent denial not handled.");
        Check(AccountRest.Quote("A\"B\\C\n") == "\"A\\\"B\\\\C\\n\"", "JSON name escaping.");
        var aggregate = new AccountAggregate { playerId = "test-owner", playerName = "Mochi", bossStage = 1, catLevel = 1, power = 290, totalFocusMinutes = 2147483650L };
        string json = Document(aggregate, OldVersion);
        Check(AccountRest.TryRemote(json, "test-owner", out var remote) && remote.aggregate.SameAs(aggregate), "Firestore int64/field mapping failed.");
        Check(!AccountRest.TryRemote(json, "another-owner", out _), "Wrong owner document accepted.");
        Check(!AccountRest.TryRemote("{}", "test-owner", out _), "Malformed remote document accepted.");
        Check(AccountRest.BuildAggregateCommit("catquest-test", aggregate, null).Contains("\"exists\":false"), "Create lacks nonexistence guard.");
        Check(AccountRest.BuildAggregateCommit("catquest-test", aggregate, OldVersion).Contains("\"updateTime\":\"" + OldVersion + "\"") &&
            AccountRest.BuildAggregateCommit("catquest-test", aggregate, OldVersion).Contains("REQUEST_TIME"), "Update guard/server timestamp missing.");
    }
    static void VerifyLocalOwnership()
    {
        WithController((c, store, http) =>
        {
            store.Data.username = "Mochi"; store.fail = true; c.SetUploadEnabled(true);
            Check(string.IsNullOrEmpty(store.Data.account.boundUid) && http.methods.Count == 0, "Failed bind persisted/uploaded.");
            Check(Get<int>(c, "retryCount") == 3, "Failed local save would retry every frame.");
            store.fail = false; c.SetUploadEnabled(true);
            Check(store.Data.account.boundUid == "test-owner" && store.Data.account.uploadEnabled, "First opt-in did not bind local save.");
            c.SignOut(); Check(store.Data.account.boundUid == "test-owner" && !c.State.signedIn, "Signout discarded binding.");
            Set(c, "uid", "different"); Set(c, "refreshToken", "fake"); c.SetUploadEnabled(true);
            Check(c.State.wrongAccount && store.Data.account.boundUid == "test-owner", "Another account stole bound progress.");
            string path = Path.GetFullPath("Library/SettingsAccountVerification/account-" + Guid.NewGuid().ToString("N") + ".json");
            var disk = new PlayerDataStore(path); Check(disk.TrySave(PlayerDataStore.Copy(store.Data), out _), "Isolated account save failed.");
            var reloaded = new PlayerDataStore(path).Data.account;
            Check(reloaded.boundUid == "test-owner" && reloaded.playerName == "Mochi", "Account ownership/name lost on reload.");
        });
    }
    static void VerifyPublication()
    {
        WithController((c, store, http) =>
        {
            Bound(store); http.replies.Enqueue(Reply(404)); http.replies.Enqueue(Reply(200, "{\"writeResults\":[{\"updateTime\":\"" + NewVersion + "\"}]}"));
            Drain(Synchronize(c));
            Check(http.methods.SequenceEqual(new[] { "GET", "POST" }) && store.Data.account.lastServerUpdateTime == NewVersion && store.Data.account.pendingSnapshotJson == "", "First publication acknowledgement failed.");
        });
        WithController((c, store, http) =>
        {
            Bound(store, true, OldVersion); var local = AccountAggregate.From(store.Data, "test-owner", "Mochi");
            var remote = PlayerDataStore.Copy(local); remote.totalTasksCompleted = 20;
            http.replies.Enqueue(Reply(200, Document(remote, NewVersion))); Drain(Synchronize(c));
            Check(c.State.conflict && http.methods.Count == 1 && store.Data.history.Count == 0, "Remote conflict overwrote local data or auto-published.");
            http.replies.Enqueue(Reply(200, Document(remote, NewVersion)));
            http.replies.Enqueue(Reply(200, "{\"writeResults\":[{\"updateTime\":\"2026-10-21T04:00:02Z\"}]}"));
            Drain(Synchronize(c, true)); Check(!c.State.conflict && http.methods.Last() == "POST", "Explicit snapshot replacement failed.");
        });
        WithController((c, store, http) =>
        {
            Bound(store); var local = AccountAggregate.From(store.Data, "test-owner", "Mochi");
            http.replies.Enqueue(Reply(404)); http.replies.Enqueue(new AccountHttpResult { transportError = true }); Drain(Synchronize(c));
            Check(!string.IsNullOrEmpty(store.Data.account.pendingSnapshotJson), "Uncertain upload was forgotten.");
            http.replies.Enqueue(Reply(200, Document(local, NewVersion))); Drain(Synchronize(c));
            Check(http.methods.Count(method => method == "POST") == 1 && store.Data.account.lastServerUpdateTime == NewVersion, "Lost response caused another write or false conflict.");
        });
        WithController((c, store, http) =>
        {
            Bound(store, true, OldVersion); c.SetUploadEnabled(false);
            Check(store.Data.account.pendingRemoval && !store.Data.account.uploadEnabled, "Opt-out did not persist removal.");
            http.replies.Enqueue(new AccountHttpResult { transportError = true }); Drain(Synchronize(c));
            Check(store.Data.account.pendingRemoval, "Offline removal reported success.");
            var remote = AccountAggregate.From(store.Data, "test-owner", "Mochi");
            http.replies.Enqueue(Reply(200, Document(remote, OldVersion))); http.replies.Enqueue(Reply(200)); Drain(Synchronize(c));
            Check(!store.Data.account.pendingRemoval && store.Data.account.boundUid == "test-owner" && http.methods.Last() == "DELETE", "Removal lost ownership or did not delete.");
        });
        WithController((c, store, http) =>
        {
            Bound(store); http.pause = true; http.replies.Enqueue(Reply(404));
            var guarded = (IEnumerator)typeof(AccountController).GetMethod("Run", Private).Invoke(c, new object[] { Synchronize(c), Get<int>(c, "generation") });
            Check(guarded.MoveNext(), "Test request did not pause in flight."); c.SignOut(); Drain(guarded);
            Check(http.methods.SequenceEqual(new[] { "GET" }) && http.cancels > 0 && string.IsNullOrEmpty(store.Data.account.pendingSnapshotJson), "Stale callback published after signout.");
        });
        WithController((c, store, http) =>
        {
            Bound(store); http.pauseMethod = "POST";
            http.replies.Enqueue(Reply(404)); http.replies.Enqueue(Reply(200, "{\"writeResults\":[{\"updateTime\":\"" + NewVersion + "\"}]}"));
            var guarded = (IEnumerator)typeof(AccountController).GetMethod("Run", Private).Invoke(c, new object[] { Synchronize(c), Get<int>(c, "generation") });
            Check(guarded.MoveNext() && http.methods.Last() == "POST", "Test did not pause the first-create request.");
            c.SetUploadEnabled(false);
            Check(store.Data.account.pendingRemoval && !store.Data.account.uploadEnabled && http.cancels == 0, "Opt-out aborted an in-flight publication.");
            Drain(guarded);
            Check(store.Data.account.pendingRemoval && store.Data.account.lastServerUpdateTime == NewVersion, "Opt-out was lost after upload completed.");
            http.replies.Enqueue(Reply(200, Document(AccountAggregate.From(store.Data, "test-owner", "Mochi"), NewVersion))); http.replies.Enqueue(Reply(200));
            Drain(Synchronize(c));
            Check(!store.Data.account.pendingRemoval && http.methods.Last() == "DELETE", "Serialized opt-out did not remove the completed publication.");
        });
        WithController((c, store, http) =>
        {
            Bound(store); http.replies.Enqueue(Reply(404)); http.replies.Enqueue(new AccountHttpResult { transportError = true }); Drain(Synchronize(c));
            http.replies.Enqueue(Reply(404)); Drain(Synchronize(c));
            Check(http.methods.Count(method => method == "POST") == 1, "An uncertain first create was submitted twice.");
            c.SetUploadEnabled(false); http.replies.Enqueue(Reply(404)); Drain(Synchronize(c));
            Check(store.Data.account.pendingRemoval && store.Data.account.pendingWriteMayCreate, "Absent document falsely confirmed removal while first create could still arrive.");
            var pending = AccountAggregate.ReadPending(store.Data.account.pendingSnapshotJson);
            http.replies.Enqueue(Reply(200, Document(pending, NewVersion))); http.replies.Enqueue(Reply(200)); Drain(Synchronize(c));
            Check(!store.Data.account.pendingRemoval && !store.Data.account.pendingWriteMayCreate, "Late first create was not removed after reconciliation.");
        });
    }
}
