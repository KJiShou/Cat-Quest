using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A compact view of the selected published ranking used by the full page.
public sealed class DashboardLeaderboardUI : MonoBehaviour
{
    public MainMenuNavigation navigation;
    public LeaderboardPanelUI fullPanel;
    public Button viewAll;
    public TMP_Text message;
    public TMP_Text title;
    public LeaderboardRowUI[] rows;
    AccountController account;
    ILeaderboardSource source;
    Coroutine running;
    string context;
    int generation;
    TaskService service;
    public LeaderboardKind CurrentKind { get; private set; }

    void Awake() { viewAll.onClick.AddListener(OpenLeaderboard); }
    void OnEnable()
    {
        account = AccountController.EnsureInstance();
        source = source ?? new LeaderboardReader(account);
        context = account.RankingContext;
        account.Changed += AccountChanged;
        service = GameDataManager.EnsureInstance().Tasks;
        CurrentKind = service.Settings.DashboardRanking;
        service.Changed += SettingsChanged;
        Reload();
    }
    void OnDisable()
    {
        if (account) account.Changed -= AccountChanged;
        if (service != null) service.Changed -= SettingsChanged;
        Stop();
    }
    void OnDestroy() { if (viewAll) viewAll.onClick.RemoveListener(OpenLeaderboard); }
    void AccountChanged()
    {
        if (context == account.RankingContext) return;
        context = account.RankingContext;
        Reload();
    }
    void SettingsChanged()
    {
        if (!isActiveAndEnabled) return;
        var kind = service.Settings.DashboardRanking;
        if (kind == CurrentKind) return;
        CurrentKind = kind;
        Reload();
    }
    void Stop()
    {
        generation++;
        source?.Cancel();
        if (running != null) StopCoroutine(running);
        running = null;
    }
    public void SetSource(ILeaderboardSource value)
    {
        Stop();
        source = value ?? new LeaderboardReader(account);
        if (isActiveAndEnabled) Reload();
    }
    void Reload()
    {
        Stop();
        title.text = CurrentKind == LeaderboardKind.FocusMinutes ? "Focus Leaderboard" : LeaderboardQuery.Title(CurrentKind) + " Leaderboard";
        foreach (var row in rows) row.gameObject.SetActive(false);
        message.gameObject.SetActive(true);
        message.text = "Loading rankings…";
        int owner = generation;
        running = StartCoroutine(Load(owner));
    }
    IEnumerator Load(int owner)
    {
        var kind = CurrentKind;
        yield return source.Load(kind, result =>
        {
            if (owner != generation || !isActiveAndEnabled || result.kind != kind) return;
            int count = result.success ? Mathf.Min(rows.Length, result.leaders.Count) : 0;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].gameObject.SetActive(i < count);
                if (i < count) rows[i].Show(result.leaders[i], i + 1, kind,
                    result.own != null && result.own.playerId == result.leaders[i].playerId);
            }
            message.gameObject.SetActive(count == 0);
            message.text = result.message ?? "No rankings available. Open View All to retry.";
        });
        if (owner == generation) running = null;
    }
    void OpenLeaderboard()
    {
        if (navigation.ShowPage(MainMenuPage.Leaderboard)) fullPanel.Select(CurrentKind);
    }
}
