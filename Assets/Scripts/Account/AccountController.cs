using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

// One optional publication client. It never restores or settles gameplay from the network.
public sealed class AccountController : MonoBehaviour
{
    public static AccountController Instance { get; private set; }
    public event Action Changed;
    public AccountConnectionConfig configuration;
    public AccountViewState State
    {
        get
        {
            var prefs = service?.AccountSettings ?? new AccountPreferences();
            return new AccountViewState {
                configured = Configured, busy = busy || saving, signedIn = SignedIn, uploadEnabled = prefs.uploadEnabled,
                pendingRemoval = prefs.pendingRemoval, conflict = conflict, wrongAccount = WrongAccount(prefs),
                playerName = DisplayName(prefs), accountLabel = SignedIn ? (string.IsNullOrEmpty(email) ? "Google account" : email) : "Not signed in",
                status = !AppPlatform.WindowsDesktop ? AppPlatform.DesktopMessage : Configured ? status : configuration ? configuration.ConfigurationError : "Google sign-in is not available yet. Local tasks and progress remain available.",
                comparison = comparison
            };
        }
    }
    SqlitePlayerService service;
    IAccountHttp http = new AccountHttp();
    Coroutine operation;
    DesktopGoogleSignIn browser;
    string uid, idToken, refreshToken, email, credentialTarget;
    string status = "Sign in with Google to optionally share ranking statistics.", comparison = "";
    DateTimeOffset expiresAt;
    bool busy, signingIn, dirty, conflict, memoryOnly, initializing, restoreAttempted;
    int pendingLocalSaves;
    bool saving => pendingLocalSaves > 0;
    bool? requestedUploadConsent;
    int generation, retryCount;
    float nextSync;
    AccountRemoteSnapshot conflictingRemote;
    bool Configured => AppPlatform.WindowsDesktop && configuration && configuration.IsConfigured;
    bool SignedIn => !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(refreshToken);

    // Read-only ranking requests share authentication, never publication or settlement.
    public string RankingContext => generation + "|" + uid + "|" + service?.AccountSettings.uploadEnabled + "|" +
        service?.AccountSettings.pendingRemoval + "|" + service?.AccountSettings.lastServerUpdateTime;
    internal IEnumerator AuthorizeRanking(Action<string, string, string> complete)
    {
        int owner = generation;
        float deadline = Time.realtimeSinceStartup + 25;
        while ((busy || saving) && owner == generation && Time.realtimeSinceStartup < deadline) yield return null;
        if (busy || saving || owner != generation || !Configured || !SignedIn) { complete(null, null, null); yield break; }
        bool done = false, valid = false;
        StartOperation(PrepareRankingToken(ok => { valid = ok; done = true; }));
        while (!done && owner == generation && Time.realtimeSinceStartup < deadline) yield return null;
        if (!done || !valid || owner != generation || !SignedIn) complete(null, null, null);
        else complete(configuration.firebaseProjectId, uid, idToken);
    }
    IEnumerator PrepareRankingToken(Action<bool> complete) { yield return EnsureToken(complete); }

    public static AccountController EnsureInstance()
    {
        if (Instance) { Instance.Initialize(); return Instance; }
        var existing = FindFirstObjectByType<AccountController>();
        if (existing) { Instance = existing; existing.Initialize(); return existing; }
        return new GameObject("AccountController").AddComponent<AccountController>();
    }
    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        Initialize();
    }
    void Initialize()
    {
        if (service != null || initializing) return;
        initializing = true;
        try
        {
            service = GameDataManager.EnsureInstance().Service;
            if (service == null) return; // Companion is a control client, not an account/storage owner.
            if (!configuration) configuration = Resources.Load<AccountConnectionConfig>("AccountConnectionConfig");
            service.RecordsChanged += OnLocalChanged;
            if(service.IsReady) OnLocalChanged(null);
            if (Configured) credentialTarget = AccountCredentials.Target(configuration.firebaseProjectId, GameDataManager.Instance.CredentialStoreIdentity);
        }
        finally { initializing = false; }
    }
    void OnEnable()
    {
        if (Instance && Instance != this) return;
        Instance = this; Initialize(); RestoreRememberedSession();
    }
    void Start() => RestoreRememberedSession();
    void RestoreRememberedSession()
    {
        if (restoreAttempted) return;
        if (service == null || !service.IsReady) return;
        restoreAttempted = true;
        // Verification stores must never read the real user's Windows credential.
        if (!Configured || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")) ||
            CompanionProtocol.HasArgument("--focus-smoke")) return;
        var saved = AccountCredentials.Read(credentialTarget);
        if (saved != null) StartOperation(Restore(saved));
    }
    void Update()
    {
        if (!restoreAttempted && service != null && service.IsReady) RestoreRememberedSession();
        if (service == null || !service.IsReady || !Configured || !SignedIn || busy || saving || conflict || retryCount >= 3 || Time.unscaledTime < nextSync) return;
        var prefs = service.AccountSettings;
        if (WrongAccount(prefs)) return;
        if (prefs.pendingRemoval || (prefs.uploadEnabled && dirty)) StartOperation(Synchronize(false));
    }
    bool WrongAccount(AccountPreferences prefs) => SignedIn && !string.IsNullOrEmpty(prefs.boundUid) && prefs.boundUid != uid;
    string DisplayName(AccountPreferences prefs) => string.IsNullOrEmpty(prefs.playerName) ? (service?.PlayerName ?? "") : prefs.playerName;
    void Notify() { Changed?.Invoke(); }
    void SetStatus(string value) { status = value; Notify(); }
    AccountAggregate observedLocalAggregate;
    int aggregateObservation;
    async void OnLocalChanged(IReadOnlyCollection<PlayerRecordKind> kinds)
    {
        // Preference persistence (including server acknowledgement metadata) must
        // not itself schedule another upload. Explicit consent actions own that.
        bool aggregateChanged = kinds == null || kinds.Contains(PlayerRecordKind.Profile) || kinds.Contains(PlayerRecordKind.History)
            || kinds.Contains(PlayerRecordKind.FocusSessions) || kinds.Contains(PlayerRecordKind.Equipment) || kinds.Contains(PlayerRecordKind.BossProgress);
        if (!aggregateChanged) { if(kinds.Contains(PlayerRecordKind.Account)) Notify(); return; }
        int request=++aggregateObservation; var source=service;
        if(source==null||!source.IsReady)return;
        string name=DisplayName(source.AccountSettings);
        if(!AccountAggregate.ValidName(name))name="Local";
        var result=await source.QueryAccountAggregateAsync("local-observation",name);
        if(!this||source!=service||request!=aggregateObservation||!result.Succeeded)return;
        bool differs=observedLocalAggregate==null||!observedLocalAggregate.SameAs(result.Value);
        observedLocalAggregate=result.Value;
        if(differs){dirty=true;nextSync=Time.unscaledTime+2;}
        Notify();
    }
    async Task<bool> SaveAsync(Action<AccountPreferences> patch)
    {
        int owner = generation;
        var currentService = service;
        if (currentService == null || !currentService.IsReady) return false;
        pendingLocalSaves++;
        try
        {
            var result = await currentService.PatchAccountSettingsAsync(patch);
            if (!this || owner != generation) return false;
            if (result.Succeeded) { Notify(); return true; }
            retryCount = 3; nextSync = Time.unscaledTime + 30;
            SetStatus(result.Message); return false;
        }
        catch (Exception) {
            if (this && owner == generation) SetStatus("The account settings could not be saved. Please retry.");
            return false;
        }
        finally { pendingLocalSaves--; if (this && owner == generation) Notify(); }
    }
    IEnumerator Save(Action<AccountPreferences> patch, Action<bool> complete)
    {
        var task = SaveAsync(patch);
        while (!task.IsCompleted) yield return null;
        complete(task.Status == TaskStatus.RanToCompletion && task.Result);
    }
    bool RequireReady()
    {
        if (Configured && service != null && service.IsReady) return true;
        Notify(); return false;
    }
    void StartOperation(IEnumerator action, bool login = false)
    {
        if (busy || saving) return;
        busy = true; signingIn = login; Notify();
        operation = StartCoroutine(Run(action, generation));
    }
    IEnumerator Run(IEnumerator action, int owner)
    {
        // Flatten nested routines so transport/parse failures always release the busy state.
        var stack = new Stack<IEnumerator>(); stack.Push(action);
        try
        {
            while (stack.Count > 0 && generation == owner)
            {
                bool moved = false; object yielded = null; bool failed = false;
                try { moved = stack.Peek().MoveNext(); if (moved) yielded = stack.Peek().Current; }
                catch (Exception) { failed = true; SetStatus("The account operation could not finish. Your local progress is safe. Please retry."); }
                if (failed) { retryCount++; nextSync = Time.unscaledTime + 30; break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
            }
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (generation == owner) { busy = false; signingIn = false; operation = null; Notify(); }
        }
    }
    void CancelOperations()
    {
        generation++; http.Cancel(); browser?.Dispose(); browser = null;
        if (operation != null) StopCoroutine(operation);
        operation = null; busy = false; signingIn = false;
    }

    public void SignIn()
    {
        if (!RequireReady() || busy || saving) return;
        StartOperation(Login(), true);
    }
    public void CancelSignIn()
    {
        if (!signingIn) return;
        CancelOperations(); SetStatus("Google sign-in cancelled.");
    }
    public void SignOut()
    {
        CancelOperations(); uid = idToken = refreshToken = email = null; expiresAt = default;
        conflict = false; conflictingRemote = null; comparison = ""; dirty = false;
        bool removed = string.IsNullOrEmpty(credentialTarget) || AccountCredentials.Delete(credentialTarget);
        var prefs = service.AccountSettings;
        SetStatus(!removed ? "Signed out for this session. Windows could not remove the remembered sign-in; remove CatQuest from Credential Manager." :
            prefs.pendingRemoval ? "Signed out. Sign in to the linked account to finish removing shared statistics." :
            "Signed out. Your tasks and progress remain on this device.");
    }
    public async void SetPlayerName(string value)
    {
        value = (value ?? "").Trim();
        if (!AccountAggregate.ValidName(value)) { SetStatus("Enter a display name of 1–20 characters without markup or control characters."); return; }
        if (service == null || !service.IsReady || saving) return;
        int owner = generation; pendingLocalSaves++;
        try {
            var result = await service.RenamePlayerAsync(value);
            if (!this || owner != generation) return;
            if (result.Succeeded) { dirty = true; nextSync = Time.unscaledTime + 2; SetStatus("Display name saved."); }
            else SetStatus(result.Message);
        } catch (Exception) { if (this && owner == generation) SetStatus("The display name could not be saved. Please retry."); }
        finally { pendingLocalSaves--; if (this && owner == generation) Notify(); }
    }
    public async void SetUploadEnabled(bool enabled)
    {
        if (service == null || !service.IsReady) return;
        var prefs = service.AccountSettings;
        if (enabled)
        {
            if (!RequireReady()) return;
            if (!SignedIn) { SetStatus("Sign in with Google before sharing statistics."); return; }
            if (WrongAccount(prefs)) { SetStatus("This local save belongs to another Google account. Sign in to that account to share its statistics."); return; }
            string name = DisplayName(prefs).Trim();
            if (!AccountAggregate.ValidName(name)) { SetStatus("Choose a display name before sharing statistics."); return; }
            if (busy || saving) return;
            prefs.boundUid = uid; prefs.playerName = name; prefs.uploadEnabled = true; prefs.pendingRemoval = false;
        }
        else
        {
            // A server write may outlive Abort(). Serialize removal after the publication result instead.
            if (signingIn) CancelOperations();
            prefs.uploadEnabled = false; prefs.pendingRemoval = !string.IsNullOrEmpty(prefs.boundUid);
        }
        requestedUploadConsent = enabled;
        string ownerUid = uid, playerName = prefs.playerName;
        if (!await SaveAsync(latest => {
            if (enabled) {
                if (!string.IsNullOrEmpty(latest.boundUid) && latest.boundUid != ownerUid) throw new PlayerCommandException("This local save belongs to another Google account.");
                latest.boundUid = ownerUid; latest.playerName = playerName; latest.uploadEnabled = true; latest.pendingRemoval = false;
            } else { latest.uploadEnabled = false; latest.pendingRemoval = !string.IsNullOrEmpty(latest.boundUid); }
        })) { if (this && requestedUploadConsent == enabled) requestedUploadConsent = null; return; }
        if (!this) return;
        prefs = service.AccountSettings;
        dirty = true; conflict = false; conflictingRemote = null; comparison = ""; retryCount = 0; nextSync = Time.unscaledTime;
        SetStatus(enabled ? "Sharing only your name and approved ranking statistics." :
            prefs.pendingRemoval ? "Sharing stopped. Removing the uploaded statistics when the linked account is online." : "Statistics sharing is off.");
    }
    public void Retry()
    {
        if (!RequireReady() || busy || saving) return;
        if (!SignedIn) { SignIn(); return; }
        retryCount = 0; dirty = true;
        StartOperation(Synchronize(false));
    }
    public void UseThisDeviceStatistics()
    {
        if (!Configured || !SignedIn || busy || saving || !conflict || conflictingRemote == null || WrongAccount(service.AccountSettings)) return;
        StartOperation(Synchronize(true));
    }

    IEnumerator Login()
    {
        SetStatus("Complete Google sign-in in your browser. You can cancel here.");
        browser = new DesktopGoogleSignIn(configuration.googleDesktopClientId);
        using var login = browser;
        Application.OpenURL(login.AuthorizationUrl);
        while (!login.Completion.IsCompleted) yield return null;
        browser = null;
        if (login.Completion.IsFaulted || login.Completion.IsCanceled) { SetStatus("Google sign-in could not finish. Please retry."); yield break; }
        var callback = login.Completion.Result;
        if (!string.IsNullOrEmpty(callback.error)) { SetStatus(callback.error); yield break; }
        AccountHttpResult response = null;
        string body = "client_id=" + AccountRest.Form(configuration.googleDesktopClientId) + "&code=" + AccountRest.Form(callback.code) +
            "&code_verifier=" + AccountRest.Form(login.Verifier) + "&redirect_uri=" + AccountRest.Form(login.RedirectUri) + "&grant_type=authorization_code";
        if (!string.IsNullOrEmpty(configuration.googleDesktopClientSecret)) body += "&client_secret=" + AccountRest.Form(configuration.googleDesktopClientSecret);
        yield return http.Send("https://oauth2.googleapis.com/token", "POST", body, "application/x-www-form-urlencoded", null, r => response = r);
        if (!response.Success) { SetStatus(response.Message); yield break; }
        var google = AccountRest.Parse<AccountRest.GoogleTokens>(response.body);
        if (google == null || !AccountRest.ValidGoogleToken(google.id_token, configuration.googleDesktopClientId, login.Nonce))
        { SetStatus("Google returned an invalid sign-in response. Please retry."); yield break; }
        yield return http.Send(AccountRest.IdentityUrl(configuration.firebaseApiKey), "POST",
            AccountRest.FirebaseSignInBody(google.id_token, configuration.firebaseProjectId), "application/json", null, r => response = r);
        if (!response.Success) { SetStatus(response.Message); yield break; }
        var firebase = AccountRest.Parse<AccountRest.FirebaseSignIn>(response.body);
        if (firebase == null || !AcceptSession(firebase.localId, firebase.idToken, firebase.refreshToken, firebase.expiresIn, firebase.email))
        { SetStatus("The sign-in response could not be verified. Please retry."); yield break; }
        OnSignedIn();
    }
    IEnumerator Restore(AccountCredential saved)
    {
        uid = saved.uid; refreshToken = saved.refreshToken; email = saved.email;
        bool restored = false;
        yield return EnsureToken(ok => restored = ok);
        if (restored) OnSignedIn();
    }
    void OnSignedIn()
    {
        conflict = false; comparison = ""; conflictingRemote = null; retryCount = 0; dirty = true; nextSync = Time.unscaledTime;
        var prefs = service.AccountSettings;
        SetStatus(WrongAccount(prefs) ? "This local save is linked to another Google account. Statistics will not upload from this account." :
            memoryOnly ? "Signed in for this session. Windows could not securely remember the sign-in." :
            prefs.pendingRemoval ? "Signed in. Finishing removal of shared statistics." : "Signed in. Your local progress stays on this device.");
    }
    bool AcceptSession(string userId, string token, string refresh, string lifetime, string accountEmail)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128 || userId.Contains("/") || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refresh) ||
            !int.TryParse(lifetime, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds < 60) return false;
        uid = userId; idToken = token; refreshToken = refresh; email = accountEmail;
        expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(seconds, 86400));
        memoryOnly = !AccountCredentials.Write(credentialTarget, new AccountCredential { uid = uid, refreshToken = refreshToken, email = email });
        return true;
    }
    IEnumerator EnsureToken(Action<bool> complete)
    {
        if (!string.IsNullOrEmpty(idToken) && DateTimeOffset.UtcNow < expiresAt.AddSeconds(-60)) { complete(true); yield break; }
        if (!SignedIn) { SetStatus("Sign in to continue."); complete(false); yield break; }
        string expectedUid = uid; AccountHttpResult response = null;
        yield return http.Send(AccountRest.RefreshUrl(configuration.firebaseApiKey), "POST", "grant_type=refresh_token&refresh_token=" + AccountRest.Form(refreshToken),
            "application/x-www-form-urlencoded", null, r => response = r);
        if (!response.Success)
        {
            if (response.code == 400 || response.code == 401)
            {
                AccountCredentials.Delete(credentialTarget); uid = idToken = refreshToken = email = null;
                SetStatus("Your saved sign-in expired. Sign in with Google again.");
            }
            else Failed(response);
            complete(false); yield break;
        }
        var refreshed = AccountRest.Parse<AccountRest.FirebaseRefresh>(response.body);
        bool valid = refreshed != null && refreshed.user_id == expectedUid &&
            AcceptSession(refreshed.user_id, refreshed.id_token, refreshed.refresh_token, refreshed.expires_in, email);
        if (!valid) { uid = idToken = refreshToken = email = null; SetStatus("The refreshed account identity did not match. Sign in again."); }
        complete(valid);
    }
    void Failed(AccountHttpResult result)
    {
        retryCount++; nextSync = Time.unscaledTime + (retryCount == 1 ? 10 : 30);
        SetStatus(result.Message + (service.AccountSettings.pendingRemoval ? " Removal is still pending." : ""));
    }
    IEnumerator ReadRemote(Action<AccountRemoteSnapshot> complete)
    {
        AccountHttpResult result = null;
        yield return http.Send(AccountRest.DocumentUrl(configuration.firebaseProjectId, uid), "GET", null, null, idToken, r => result = r);
        if (result.code == 404 && !result.transportError) { complete(new AccountRemoteSnapshot()); yield break; }
        if (!result.Success) { Failed(result); complete(null); yield break; }
        if (!AccountRest.TryRemote(result.body, uid, out var remote))
        { retryCount = 3; SetStatus("The uploaded statistics could not be read safely. No data was replaced."); complete(null); yield break; }
        complete(remote);
    }
    void ShowConflict(AccountRemoteSnapshot remote, AccountAggregate local)
    {
        conflict = true; conflictingRemote = remote;
        comparison = "This device: " + local.Summary + "\nUploaded: " + (remote.exists ? remote.aggregate.Summary : "No shared statistics");
        SetStatus("Shared statistics changed elsewhere. Keep them, or explicitly use this device's statistics. Local progress is unchanged.");
    }
    IEnumerator Acknowledge(string updateTime, Action<bool> complete)
    {
        if (string.IsNullOrEmpty(updateTime)) { complete(false); yield break; }
        var prefs = service.AccountSettings;
        if (prefs.boundUid != uid) { complete(false); yield break; }
        string expectedUid = uid;
        yield return Save(latest => {
            if (latest.boundUid != expectedUid) throw new PlayerCommandException("The linked account changed before the upload could be acknowledged.");
            latest.lastServerUpdateTime = updateTime; latest.pendingSnapshotJson = ""; latest.pendingWriteMayCreate = false;
        }, complete);
    }
    IEnumerator ReadLocalAggregate(string ownerUid, string playerName, Action<AccountAggregate> complete)
    {
        var task = service.QueryAccountAggregateAsync(ownerUid, playerName);
        while (!task.IsCompleted) yield return null;
        if (task.Status != TaskStatus.RanToCompletion || !task.Result.Succeeded) {
            SetStatus(task.Status == TaskStatus.RanToCompletion ? task.Result.Message : "Local ranking statistics could not be read. Please retry.");
            complete(null); yield break;
        }
        complete(task.Result.Value);
    }
    IEnumerator Synchronize(bool replace)
    {
        var prefs = service.AccountSettings;
        if (WrongAccount(prefs)) { SetStatus("Sign in to the Google account linked to this local save."); yield break; }
        if (!prefs.uploadEnabled && !prefs.pendingRemoval) { SetStatus("Statistics sharing is off."); yield break; }
        bool authenticated = false; yield return EnsureToken(ok => authenticated = ok); if (!authenticated) yield break;
        AccountRemoteSnapshot remote = null; yield return ReadRemote(value => remote = value); if (remote == null) yield break;
        prefs = service.AccountSettings;
        if (prefs.pendingRemoval) { yield return RemoveRemote(remote); yield break; }
        if (!prefs.uploadEnabled || prefs.boundUid != uid) yield break;
        AccountAggregate local = null;
        yield return ReadLocalAggregate(uid, prefs.playerName, value => local = value);
        if (local == null) yield break;
        prefs = service.AccountSettings;
        if (!prefs.uploadEnabled || prefs.pendingRemoval || prefs.boundUid != uid || requestedUploadConsent == false) yield break;
        var uncertainPending = AccountAggregate.ReadPending(prefs.pendingSnapshotJson);
        if (prefs.pendingWriteMayCreate && uncertainPending != null && (!remote.exists || !uncertainPending.SameAs(remote.aggregate)))
        {
            // Never create a second uncertain request that could later resurrect a deleted document.
            retryCount++; nextSync = Time.unscaledTime + (retryCount == 1 ? 10 : 30);
            SetStatus("An earlier upload has not been confirmed yet. Sharing is waiting; retry later. Your local progress is safe.");
            yield break;
        }
        if (replace && (conflictingRemote == null || remote.exists != conflictingRemote.exists || remote.updateTime != conflictingRemote.updateTime))
        { ShowConflict(remote, local); yield break; }
        var decision = AccountPublishPolicy.Decide(prefs, remote);
        if (decision == AccountPublishDecision.AcknowledgePending)
        {
            bool acknowledged = false; yield return Acknowledge(remote.updateTime, ok => acknowledged = ok); if (!acknowledged) yield break;
            prefs = service.AccountSettings; decision = AccountPublishDecision.Update;
        }
        if (!replace && decision == AccountPublishDecision.Conflict) { ShowConflict(remote, local); yield break; }
        if (remote.exists && local.SameAs(remote.aggregate))
        {
            bool acknowledged = false; yield return Acknowledge(remote.updateTime, ok => acknowledged = ok); if (!acknowledged) yield break;
            conflict = false; comparison = ""; conflictingRemote = null; dirty = false; retryCount = 0;
            SetStatus("Shared statistics are up to date."); yield break;
        }
        bool previouslyUncertainCreate = prefs.pendingWriteMayCreate && !string.IsNullOrEmpty(prefs.pendingSnapshotJson);
        string pendingJson = JsonUtility.ToJson(local), expectedUid = uid;
        bool mayCreate = !remote.exists || previouslyUncertainCreate, pendingSaved = false;
        yield return Save(latest => {
            if (latest.boundUid != expectedUid || !latest.uploadEnabled || latest.pendingRemoval) throw new PlayerCommandException("Statistics sharing is off.");
            latest.pendingSnapshotJson = pendingJson; latest.pendingWriteMayCreate = mayCreate;
        }, ok => pendingSaved = ok);
        if (!pendingSaved) yield break;
        if (!service.AccountSettings.uploadEnabled || requestedUploadConsent == false)
        {
            // No request was sent yet. Only a previous uncertain create still needs reconciliation.
            if (!previouslyUncertainCreate) {
                bool cleared = false;
                yield return Save(latest => { latest.pendingSnapshotJson = ""; latest.pendingWriteMayCreate = false; }, ok => cleared = ok);
                if (!cleared) yield break;
            }
            yield break;
        }
        string expectedVersion = remote.exists ? remote.updateTime : null;
        AccountHttpResult result = null;
        yield return http.Send(AccountRest.CommitUrl(configuration.firebaseProjectId), "POST",
            AccountRest.BuildAggregateCommit(configuration.firebaseProjectId, local, expectedVersion), "application/json", idToken, r => result = r);
        if (!result.Success)
        {
            // A terminal server rejection proves this request did not write; transport failures do not.
            if (!result.transportError && result.code >= 400 && result.code < 500 && !previouslyUncertainCreate)
            {
                bool cleared = false;
                yield return Save(latest => { latest.pendingSnapshotJson = ""; latest.pendingWriteMayCreate = false; }, ok => cleared = ok);
                if (!cleared) yield break;
            }
            if (result.code == 409 || result.code == 400)
            {
                AccountRemoteSnapshot latest = null; yield return ReadRemote(value => latest = value);
                if (latest != null && latest.exists && local.SameAs(latest.aggregate))
                {
                    bool acknowledged = false; yield return Acknowledge(latest.updateTime, ok => acknowledged = ok);
                    if (acknowledged) yield return FinishPublication(local);
                }
                else if (latest != null) ShowConflict(latest, local);
            }
            else Failed(result);
            yield break;
        }
        string version = AccountRest.CommitUpdateTime(result.body);
        if (string.IsNullOrEmpty(version)) { SetStatus("Upload response was incomplete. Retry to confirm the saved statistics."); retryCount = 3; yield break; }
        bool finalAcknowledged = false; yield return Acknowledge(version, ok => finalAcknowledged = ok);
        if (finalAcknowledged) yield return FinishPublication(local);
    }
    IEnumerator FinishPublication(AccountAggregate uploaded)
    {
        conflict = false; comparison = ""; conflictingRemote = null; retryCount = 0;
        var prefs = service.AccountSettings;
        AccountAggregate latest = null;
        yield return ReadLocalAggregate(uid, prefs.playerName, value => latest = value);
        if (latest == null) { dirty = true; yield break; }
        dirty = !uploaded.SameAs(latest); nextSync = Time.unscaledTime + 2;
        prefs = service.AccountSettings;
        if (prefs.pendingRemoval) { nextSync = Time.unscaledTime; SetStatus("Sharing is off. Removing the earlier uploaded statistics."); }
        else SetStatus("Ranking statistics shared. Tasks, tags and history stay on this device.");
    }
    IEnumerator RemoveRemote(AccountRemoteSnapshot remote)
    {
        var initial = service.AccountSettings;
        var pending = AccountAggregate.ReadPending(initial.pendingSnapshotJson);
        bool uncertainCreate = initial.pendingWriteMayCreate && pending != null;
        bool observedPending = remote.exists && pending != null && pending.SameAs(remote.aggregate);
        if (!remote.exists && uncertainCreate)
        {
            retryCount++; nextSync = Time.unscaledTime + (retryCount == 1 ? 10 : 30);
            SetStatus("Sharing is off. An earlier upload has not been confirmed yet, so removal is still pending. Retry later.");
            yield break;
        }
        if (remote.exists)
        {
            AccountHttpResult result = null;
            yield return http.Send(AccountRest.DocumentUrl(configuration.firebaseProjectId, uid) + "?currentDocument.updateTime=" + AccountRest.Form(remote.updateTime),
                "DELETE", null, null, idToken, r => result = r);
            if (!result.Success && result.code != 404) { Failed(result); yield break; }
        }
        if (uncertainCreate && !observedPending)
        {
            retryCount++; nextSync = Time.unscaledTime + 30;
            SetStatus("Shared statistics were removed, but an earlier upload still needs confirmation. Removal remains pending.");
            yield break;
        }
        bool saved = false;
        yield return Save(latest => { latest.pendingRemoval = false; latest.lastServerUpdateTime = ""; latest.pendingSnapshotJson = ""; latest.pendingWriteMayCreate = false; }, ok => saved = ok);
        if (!saved) yield break;
        dirty = false; conflict = false; comparison = ""; conflictingRemote = null; retryCount = 0;
        SetStatus("Shared statistics removed. Your local progress is unchanged.");
    }
    void OnDestroy()
    {
        if (Instance != this) return;
        CancelOperations(); if (service != null) service.RecordsChanged -= OnLocalChanged;
        uid = idToken = refreshToken = email = null; Instance = null;
    }
    void OnDisable()
    {
        if (Instance != this) return;
        CancelOperations();
        if (service != null) service.RecordsChanged -= OnLocalChanged;
        service = null; restoreAttempted = false;
        uid = idToken = refreshToken = email = null;
    }
}
