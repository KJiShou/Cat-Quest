using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

// Explicit development-player integration test. Real OAuth stays in the system browser.
// Commands/results never contain tokens, email, or private player data.
public sealed class AccountBuildSmoke : MonoBehaviour
{
    public static bool Enabled => Debug.isDebugBuild && CompanionProtocol.HasArgument("--account-smoke") && !CompanionProtocol.HasArgument("--companion");
    public static string IsolatedSavePath
    {
        get
        {
            string directory = CompanionProtocol.Argument("--account-test-dir");
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory)) throw new InvalidOperationException("Account test needs an absolute isolated directory.");
            directory = Path.GetFullPath(directory);
            if (!File.Exists(Path.Combine(directory, "account-test.marker"))) throw new InvalidOperationException("Account test directory marker missing.");
            string path = Path.Combine(directory, "account-test-save.json");
            if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), Application.persistentDataPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Account testing cannot use the live save directory.");
            return path;
        }
    }
    [Serializable] sealed class Command { public string action, name; public bool enabled; }
    [Serializable] sealed class Result
    {
        public bool ready, configured, busy, signedIn, uploadEnabled, pendingRemoval, conflict, wrongAccount, bound, acknowledged;
        public string status, remoteCheck;
        public int remoteHttp;
        public bool remoteExists, remoteMatches, remoteFieldsValid;
        public bool developmentConsoleVisible;
        public int presentationWarnings;
    }
    string directory;
    float next;
    SettingsPanelUI panel;
    bool panelOpened, checkingRanks;
    readonly Result result = new Result();
    void Start() { directory = Path.GetDirectoryName(IsolatedSavePath); Application.logMessageReceived += ObserveLog; }
    void OnDestroy() { Application.logMessageReceived -= ObserveLog; }
    void ObserveLog(string message, string stack, LogType type)
    {
        if (message.Contains("PresentFrame") || message.Contains("WaitForLastPresentation")) result.presentationWarnings++;
    }
    void Update()
    {
        if (!Enabled || Time.unscaledTime < next) return;
        next = Time.unscaledTime + .25f;
        try
        {
            if (!panel && !panelOpened)
            {
                var launcher = FindFirstObjectByType<SettingsLauncher>();
                if (!launcher || !GameDataManager.Instance) return;
                launcher.Open(); panel = FindFirstObjectByType<SettingsPanelUI>(); panel.OpenAccount(); panelOpened = true;
            }
            var account = AccountController.Instance;
            string commandPath = Path.Combine(directory, "account-command.json");
            var command = CompanionProtocol.Read<Command>(commandPath);
            if (command != null)
            {
                File.Delete(commandPath);
                switch (command.action)
                {
                    case "signin": account.SignIn(); break;
                    case "name": account.SetPlayerName(command.name); break;
                    case "sharing": account.SetUploadEnabled(command.enabled); break;
                    case "retry": account.Retry(); break;
                    case "signout": account.SignOut(); break;
                    case "verify": StartCoroutine(VerifyRemote(account)); break;
                    case "rankings": if (!checkingRanks) StartCoroutine(VerifyRankings(account)); break;
                    case "preview": ShowPreview(command.name); break;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.Combine(directory, "presentation-capture.png")); break;
                    case "quit": Application.Quit(); break;
                }
            }
            var state = account.State;
            var prefs = GameDataManager.Instance.Tasks.AccountSettings;
            result.ready = true; result.configured = state.configured; result.busy = state.busy;
            result.signedIn = state.signedIn; result.uploadEnabled = state.uploadEnabled;
            result.pendingRemoval = state.pendingRemoval; result.conflict = state.conflict;
            result.wrongAccount = state.wrongAccount; result.status = state.status;
            result.bound = !string.IsNullOrEmpty(prefs.boundUid); result.acknowledged = !string.IsNullOrEmpty(prefs.lastServerUpdateTime);
            result.developmentConsoleVisible = Debug.developerConsoleVisible;
            CompanionProtocol.Write(Path.Combine(directory, "account-result.json"), result);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(directory, "account-error.txt"), e.GetType().Name); }
    }
    // Explicit isolated development preview only; these rows never enter player data or Firebase.
    sealed class PreviewSource : ILeaderboardSource
    {
        public void Cancel() { }
        public IEnumerator Load(LeaderboardKind kind, Action<LeaderboardSnapshot> complete)
        {
            var snapshot = new LeaderboardSnapshot { kind = kind, success = true, message = "Local UI test fixtures", readTime = DateTime.UtcNow.ToString("O") };
            for (int i = 0; i < 4; i++) snapshot.leaders.Add(new AccountAggregate {
                playerId = "preview-" + i, playerName = i == 1 ? "Preview Player" : "Preview Cat " + (i + 1),
                bossStage = 7 - i, totalTasksCompleted = 120 - i * 10, totalFocusMinutes = 900 - i * 30, power = 600 - i * 20, catLevel = 5 - i });
            snapshot.own = snapshot.leaders[1]; snapshot.ownRank = 2;
            complete(snapshot); yield break;
        }
    }
    void ShowPreview(string category)
    {
        if (!Enum.TryParse(category, out LeaderboardKind kind) || !Enum.IsDefined(typeof(LeaderboardKind), kind)) return;
        if (panel) panel.Close();
        var navigation = FindFirstObjectByType<MainMenuNavigation>(); navigation.ShowPage(MainMenuPage.Leaderboard);
        var ui = navigation.pages[5].GetComponentInChildren<LeaderboardPanelUI>();
        ui.SetSource(new PreviewSource()); ui.categories[(int)kind].onClick.Invoke();
    }
    [Serializable] sealed class RankingCheck
    {
        public string category, message, readTime;
        public bool success, ordered, ownMatches, ownVisible;
        public int entries;
        public long ownRank, metric;
    }
    [Serializable] sealed class RankingReport
    {
        public string startedAt, finishedAt;
        public bool passed;
        public RankingCheck[] categories;
    }
    IEnumerator VerifyRankings(AccountController account)
    {
        if (!account.State.signedIn || account.State.busy) yield break;
        checkingRanks = true;
        var report = new RankingReport { startedAt = DateTime.UtcNow.ToString("O"), categories = new RankingCheck[4] };
        string path = Path.Combine(directory, "rankings-result.json");
        CompanionProtocol.Write(path, report);
        try
        {
            if (panel) panel.Close();
            var navigation = FindFirstObjectByType<MainMenuNavigation>();
            navigation.ShowPage(MainMenuPage.Leaderboard);
            var ui = navigation.pages[5].GetComponentInChildren<LeaderboardPanelUI>();
            ui.SetSource(null);
            for (int i = 0; i < 4; i++)
            {
                ui.categories[i].onClick.Invoke();
                float deadline = Time.realtimeSinceStartup + 80;
                while (ui.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                var snapshot = ui.Snapshot;
                var check = new RankingCheck { category = ((LeaderboardKind)i).ToString(), message = snapshot?.message ?? "No response" };
                report.categories[i] = check;
                if (!ui.Loading && snapshot != null && snapshot.kind == (LeaderboardKind)i)
                {
                    check.success = snapshot.success; check.entries = snapshot.leaders.Count;
                    check.readTime = snapshot.readTime; check.ownRank = snapshot.ownRank;
                    check.ordered = true;
                    for (int j = 1; j < snapshot.leaders.Count; j++)
                        check.ordered &= LeaderboardQuery.Compare(snapshot.leaders[j - 1], snapshot.leaders[j], snapshot.kind) < 0;
                    if (snapshot.own != null)
                    {
                        var expected = AccountAggregate.From(GameDataManager.Instance.PlayerData, snapshot.own.playerId, GameDataManager.Instance.Tasks.AccountSettings.playerName);
                        check.ownMatches = snapshot.own.SameAs(expected);
                        check.metric = LeaderboardQuery.Metric(snapshot.own, snapshot.kind);
                        check.ownVisible = ui.ownRow.gameObject.activeInHierarchy && snapshot.ownRank > 0;
                        int topIndex = snapshot.leaders.FindIndex(row => row.playerId == snapshot.own.playerId);
                        if (topIndex >= 0) check.ownMatches &= snapshot.ownRank == topIndex + 1;
                        else check.ownMatches &= snapshot.ownRank > snapshot.leaders.Count;
                    }
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "ranking-" + check.category + ".png"));
                yield return new WaitForSecondsRealtime(.5f);
                CompanionProtocol.Write(path, report);
            }
            report.passed = report.categories.All(c => c != null && c.success && c.ordered && c.ownMatches && c.ownVisible);
        }
        finally
        {
            checkingRanks = false;
            report.finishedAt = DateTime.UtcNow.ToString("O");
            CompanionProtocol.Write(path, report);
        }
    }
    IEnumerator VerifyRemote(AccountController account)
    {
        if (!account.State.signedIn || account.State.busy) yield break;
        // Keep the authenticated read in-process; never export credentials to the harness.
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        string token = (string)typeof(AccountController).GetField("idToken", flags).GetValue(account);
        string uid = (string)typeof(AccountController).GetField("uid", flags).GetValue(account);
        var http = new AccountHttp(); AccountHttpResult response = null;
        result.remoteCheck = "Reading";
        yield return http.Send(AccountRest.DocumentUrl(account.configuration.firebaseProjectId, uid), "GET", null, null, token, r => response = r);
        token = null;
        result.remoteHttp = (int)response.code; result.remoteExists = response.Success;
        result.remoteMatches = result.remoteFieldsValid = false;
        if (response.Success && AccountRest.TryRemote(response.body, uid, out var remote))
        {
            var prefs = GameDataManager.Instance.Tasks.AccountSettings;
            result.remoteMatches = AccountAggregate.ValidName(prefs.playerName) &&
                remote.aggregate.SameAs(AccountAggregate.From(GameDataManager.Instance.PlayerData, uid, prefs.playerName));
            // Firestore Value fields are JSON objects. Include the enclosing fields
            // object and reject unexpected values; do not export the response body.
            var names = Regex.Matches(response.body, "\"([^\"]+)\"\\s*:\\s*\\{")
                .Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(v => v).ToArray();
            result.remoteFieldsValid = names.SequenceEqual(new[] { "fields", "playerId", "playerName", "bossStage", "totalTasksCompleted", "totalFocusMinutes", "power", "catLevel", "updatedAt" }.OrderBy(v => v));
        }
        result.remoteCheck = "Complete";
    }
}
