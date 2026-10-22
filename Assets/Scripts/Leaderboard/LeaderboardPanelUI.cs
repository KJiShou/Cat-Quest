using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class LeaderboardPanelUI : MonoBehaviour
{
    public Button[] categories;
    public TMP_Text[] categoryLabels;
    public Image[] categoryImages;
    public Image powerCategoryIcon;
    public Sprite normalTab, selectedTab;
    public Button refresh, accountButton;
    public TMP_Text categoryTitle, empty, ownMessage, updated;
    public RectTransform content;
    public ScrollRect scroll;
    public LeaderboardRowUI template, ownRow;
    public LeaderboardKind CurrentKind { get; private set; }
    public bool Loading { get; private set; }
    public LeaderboardSnapshot Snapshot { get; private set; }
    readonly List<LeaderboardRowUI> rows = new List<LeaderboardRowUI>();
    AccountController account;
    ILeaderboardSource source;
    Coroutine running;
    int generation;
    string context;
    bool initialized;
    void Awake()
    {
        if (initialized) return; initialized = true;
        account = AccountController.EnsureInstance(); source = new LeaderboardReader(account);
        for (int i = 0; i < categories.Length; i++) { int index = i; categories[i].onClick.AddListener(() => Select((LeaderboardKind)index)); }
        refresh.onClick.AddListener(Reload); accountButton.onClick.AddListener(OpenAccount);
        template.gameObject.SetActive(false);
    }
    void OnEnable()
    {
        Awake(); account.Changed += AccountChanged; context = account.RankingContext; Reload();
    }
    void OnDisable()
    {
        if (account) account.Changed -= AccountChanged;
        Stop();
    }
    void AccountChanged()
    {
        string current = account.RankingContext;
        if (current == context) return;
        context = current; Reload();
    }
    public void SetSource(ILeaderboardSource value)
    {
        Stop(); source = value ?? new LeaderboardReader(account);
        if (isActiveAndEnabled) Reload();
    }
    public void Select(LeaderboardKind kind) { CurrentKind = kind; Reload(); }
    void Stop()
    {
        generation++; source?.Cancel();
        if (running != null) StopCoroutine(running);
        running = null; Loading = false;
    }
    public void Reload()
    {
        // Unity can preserve the component while discarding interface fields on a
        // script reload in Play Mode. Recreate only the transient read transport.
        if (source == null)
        {
            if (!account) account = AccountController.EnsureInstance();
            source = new LeaderboardReader(account);
        }
        Stop(); Snapshot = null;
        foreach (var row in rows) row.gameObject.SetActive(false);
        ownRow.gameObject.SetActive(false); ownMessage.gameObject.SetActive(true); ownMessage.text = "Loading your rank…";
        empty.gameObject.SetActive(true); empty.text = "Loading rankings…";
        updated.text = "";
        categoryTitle.text = LeaderboardQuery.Title(CurrentKind);
        for (int i = 0; i < categories.Length; i++)
        {
            bool selected = i == (int)CurrentKind; categoryImages[i].sprite = selected ? selectedTab : normalTab;
            categoryLabels[i].color = selected ? Color.white : new Color(.31f, .37f, .49f);
        }
        powerCategoryIcon.color = categoryLabels[(int)LeaderboardKind.Power].color;
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1;
        Loading = true; refresh.interactable = false;
        int owner = generation;
        running = StartCoroutine(Load(owner));
    }
    IEnumerator Load(int owner)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(source.Load(CurrentKind, result => { if (owner == generation && isActiveAndEnabled) Render(result); }));
        try
        {
            while (stack.Count > 0 && owner == generation)
            {
                bool moved = false; object value = null; bool failed = false;
                try { moved = stack.Peek().MoveNext(); if (moved) value = stack.Peek().Current; }
                catch (Exception) { failed = true; }
                if (failed) { Render(new LeaderboardSnapshot { kind = CurrentKind, message = "Could not load rankings. Please retry.", ownMessage = "Your rank is unavailable." }); break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (value is IEnumerator nested) stack.Push(nested); else yield return value;
            }
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (owner == generation) { Loading = false; refresh.interactable = true; }
        }
    }
    public void Render(LeaderboardSnapshot result)
    {
        if (result.kind != CurrentKind) return;
        Snapshot = result;
        int count = result.success ? result.leaders.Count : 0;
        while (rows.Count < count) rows.Add(Instantiate(template, content));
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].gameObject.SetActive(i < count);
            if (i < count) rows[i].Show(result.leaders[i], i + 1, CurrentKind, result.own != null && result.leaders[i].playerId == result.own.playerId);
        }
        empty.gameObject.SetActive(count == 0); empty.text = result.message ?? "No rankings available.";
        bool ranked = result.success && result.own != null && result.ownRank > 0;
        ownRow.gameObject.SetActive(ranked); ownMessage.gameObject.SetActive(!ranked);
        if (ranked) ownRow.Show(result.own, result.ownRank, CurrentKind, true);
        else ownMessage.text = result.ownMessage ?? "Your rank is unavailable. Refresh to retry.";
        updated.text = result.success && DateTimeOffset.TryParse(result.readTime, out var read) ? "Updated " + read.ToLocalTime().ToString("HH:mm") : "";
    }
    void OpenAccount()
    {
        var launcher = GetComponentInParent<Canvas>().rootCanvas.GetComponentInChildren<SettingsLauncher>(true);
        launcher.Open();
        launcher.GetComponentInParent<Canvas>().rootCanvas.GetComponentInChildren<SettingsPanelUI>(true).OpenAccount();
    }
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Tab) || EventSystem.current == null) return;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected && selected.GetComponentInParent<SettingsPanelUI>()) return;
        var controls = new List<Button>(categories); controls.Add(refresh); controls.Add(accountButton);
        controls.RemoveAll(button => !button.IsInteractable());
        int index = controls.FindIndex(button => button.gameObject == selected);
        int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
        if (index < 0) index = direction < 0 ? 0 : -1;
        controls[(index + direction + controls.Count) % controls.Count].Select();
    }
}
