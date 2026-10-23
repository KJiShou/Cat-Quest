using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Serialized views own display and input only; TaskService owns local commits.
public sealed class SettingsPanelUI : MonoBehaviour
{
    public GameObject settingsPage, tagsPage, tagEditorPage, accountPage, confirmPage;
    public Toggle notifications, upload;
    public Slider sound, music;
    public SettingsSliderCommit soundCommit, musicCommit;
    public TMP_Text settingsStatus, soundValue, musicValue;
    public Button close, accountButton, tagsButton, tagsBack, accountBack, tagCancel;
    public Button addTag, saveTag, confirmDelete, cancelDelete;
    public TMP_InputField tagName, playerName;
    public TMP_Text tagEditorTitle, tagError, emptyTags, confirmMessage, accountIdentity, accountStatus, comparison;
    public GameObject tagPreview;
    public Button[] colorButtons;
    public GameObject[] colorSelected;
    public Sprite tagTintSprite;
    public RectTransform tagList;
    public SettingsTagRow tagRowTemplate;
    public Button googleSignIn, signOut, savePlayerName, retryUpload, replaceStatistics;
    public Button[] backdropButtons;
    public Button[] dashboardRankings;
    public Sprite rankingNormal, rankingSelected;
    public Toggle showDashboardRanking, showDashboardTasks, showDashboardDailyRewards, showDashboardFocus, showDashboardCharacter;
    public Color settingsInk = new Color32(35, 78, 125, 255);
    TaskService service;
    AccountController account;
    SettingsAudio audioPlayer;
    GameObject returnFocus;
    string editingTagId, deletingTagId, selectedColor = TagColors.DefaultHex;
    [NonSerialized] bool initialized, subscribed;
    bool refreshing, nameDirty;
    LeaderboardKind selectedRanking;
    GameObject lastSelection;
    readonly List<SettingsTagRow> rows = new List<SettingsTagRow>();
    public string CurrentView { get; private set; }

    void Awake() => Initialize();
    void Initialize()
    {
        if (initialized) return;
        initialized = true;
        service = GameDataManager.EnsureInstance().Tasks;
        account = AccountController.EnsureInstance();
        audioPlayer = SettingsAudio.EnsureInstance();
        close.onClick.AddListener(Close);
        accountButton.onClick.AddListener(OpenAccount);
        tagsButton.onClick.AddListener(OpenTags);
        tagsBack.onClick.AddListener(BackToSettings);
        accountBack.onClick.AddListener(BackToSettings);
        tagCancel.onClick.AddListener(OpenTags);
        addTag.onClick.AddListener(() => EditTag(null));
        saveTag.onClick.AddListener(SaveTag);
        confirmDelete.onClick.AddListener(DeleteTag);
        cancelDelete.onClick.AddListener(OpenTags);
        notifications.onValueChanged.AddListener(_ => { if (!refreshing) SaveSettings(); });
        sound.onValueChanged.AddListener(_ => PreviewVolumes(soundCommit));
        music.onValueChanged.AddListener(_ => PreviewVolumes(musicCommit));
        soundCommit.Commit += SaveSettings;
        musicCommit.Commit += SaveSettings;
        foreach (var toggle in new[] { showDashboardRanking, showDashboardTasks, showDashboardDailyRewards, showDashboardFocus, showDashboardCharacter })
            toggle.onValueChanged.AddListener(_ => { if (!refreshing) SaveSettings(); });
        for (int i = 0; i < dashboardRankings.Length; i++)
        {
            var kind = (LeaderboardKind)i;
            dashboardRankings[i].onClick.AddListener(() => {
                if (selectedRanking == kind) return;
                selectedRanking = kind;
                SaveSettings();
                RefreshRankingSelection();
            });
        }
        tagName.onValueChanged.AddListener(_ => UpdateTagPreview());
        playerName.onValueChanged.AddListener(_ => { if (!refreshing) nameDirty = true; });
        savePlayerName.onClick.AddListener(() => {
            string draft = playerName.text;
            account.SetPlayerName(draft);
            nameDirty = account.State.playerName != draft.Trim();
            RefreshAccount();
        });
        googleSignIn.onClick.AddListener(account.SignIn);
        signOut.onClick.AddListener(account.SignOut);
        retryUpload.onClick.AddListener(account.Retry);
        replaceStatistics.onClick.AddListener(account.UseThisDeviceStatistics);
        upload.onValueChanged.AddListener(enabled => { if (!refreshing) account.SetUploadEnabled(enabled); });
        for (int i = 0; i < colorButtons.Length; i++)
        {
            string color = TagColors.Palette[i];
            colorButtons[i].onClick.AddListener(() => { selectedColor = color; UpdateTagPreview(); });
        }
        foreach (var backdrop in backdropButtons) backdrop.onClick.AddListener(Back);
        foreach (var input in GetComponentsInChildren<TMP_InputField>(true)) TaskUIBuilder.BlueCaret(input);
    }
    void OnEnable()
    {
        Initialize();
        if (!subscribed) { service.Changed += OnDataChanged; account.Changed += RefreshAccount; subscribed = true; }
    }
    void OnDisable()
    {
        if (!initialized) return;
        soundCommit.Flush(); musicCommit.Flush();
        if (audioPlayer) audioPlayer.ApplySaved();
        if (subscribed) { service.Changed -= OnDataChanged; account.Changed -= RefreshAccount; subscribed = false; }
    }
    public void Open(GameObject opener)
    {
        Initialize(); returnFocus = opener;
        gameObject.SetActive(true); transform.SetAsLastSibling();
        RefreshSettings(); Show(settingsPage, "Settings", notifications.gameObject);
    }
    public void Close()
    {
        account.CancelSignIn();
        soundCommit.Flush(); musicCommit.Flush();
        gameObject.SetActive(false);
        if (returnFocus && EventSystem.current) EventSystem.current.SetSelectedGameObject(returnFocus);
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }
        // Keep keyboard focus within the visible modal, including after dynamic rows refresh.
        if (Input.GetKeyDown(KeyCode.Tab) && EventSystem.current)
        {
            var page = ActivePage();
            var controls = Controls(page);
            if (controls.Length == 0) return;
            int current = Array.FindIndex(controls, s => s.gameObject == EventSystem.current.currentSelectedGameObject);
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            int index = (current + direction + controls.Length) % controls.Length;
            EventSystem.current.SetSelectedGameObject(controls[index].gameObject);
        }
    }
    void LateUpdate()
    {
        if (!EventSystem.current || !tagsPage.activeSelf) return;
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == lastSelection) return;
        lastSelection = selected;
        if (!selected || !selected.transform.IsChildOf(tagList)) return;
        var scroll = tagList.GetComponentInParent<ScrollRect>();
        Canvas.ForceUpdateCanvases();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, selected.transform);
        float adjustment = bounds.min.y < scroll.viewport.rect.yMin ? scroll.viewport.rect.yMin - bounds.min.y
            : bounds.max.y > scroll.viewport.rect.yMax ? scroll.viewport.rect.yMax - bounds.max.y : 0;
        if (Mathf.Abs(adjustment) < .1f) return;
        scroll.StopMovement();
        var position = tagList.anchoredPosition;
        position.y = Mathf.Clamp(position.y + adjustment, 0, Mathf.Max(0, tagList.rect.height - scroll.viewport.rect.height));
        tagList.anchoredPosition = position;
    }
    GameObject ActivePage() => CurrentView == "Tags" ? tagsPage : CurrentView == "TagEditor" ? tagEditorPage
        : CurrentView == "Account" ? accountPage : CurrentView == "Confirm" ? confirmPage : settingsPage;
    Selectable[] Controls(GameObject page) => page.GetComponentsInChildren<Selectable>()
        .Where(s => s.IsInteractable() && !backdropButtons.Contains(s as Button)).ToArray();
    void BindNavigation(GameObject page)
    {
        var controls = Controls(page);
        for (int i = 0; i < controls.Length; i++)
        {
            var previous = controls[(i + controls.Length - 1) % controls.Length];
            var next = controls[(i + 1) % controls.Length];
            var navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous, selectOnDown = next };
            if (!(controls[i] is Slider) && !(controls[i] is TMP_InputField))
            { navigation.selectOnLeft = previous; navigation.selectOnRight = next; }
            controls[i].navigation = navigation;
        }
    }
    void Show(GameObject page, string view, GameObject focus)
    {
        soundCommit.Flush(); musicCommit.Flush();
        foreach (var item in new[] { settingsPage, tagsPage, tagEditorPage, accountPage, confirmPage }) item.SetActive(item == page);
        CurrentView = view;
        BindNavigation(page);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(focus);
    }
    void Back()
    {
        if (CurrentView == "Settings") Close();
        else if (CurrentView == "TagEditor" || CurrentView == "Confirm") OpenTags();
        else BackToSettings();
    }
    void BackToSettings()
    {
        if (CurrentView == "Account") account.CancelSignIn();
        RefreshSettings(); Show(settingsPage, "Settings", CurrentView == "Account" ? accountButton.gameObject : tagsButton.gameObject);
    }
    AppSettings ReadSettings()
    {
        var draft = service.Settings;
        draft.notificationsEnabled = notifications.isOn;
        draft.soundVolume = sound.value;
        draft.musicVolume = music.value;
        draft.dashboardRanking = selectedRanking.ToString();
        draft.hideDashboardRanking = !showDashboardRanking.isOn;
        draft.hideDashboardTasks = !showDashboardTasks.isOn;
        draft.hideDashboardDailyRewards = !showDashboardDailyRewards.isOn;
        draft.hideDashboardFocus = !showDashboardFocus.isOn;
        draft.hideDashboardCharacter = !showDashboardCharacter.isOn;
        return draft;
    }
    void RefreshRankingSelection()
    {
        for (int i = 0; i < dashboardRankings.Length; i++)
        {
            bool selected = i == (int)selectedRanking;
            ((Image)dashboardRankings[i].targetGraphic).sprite = selected ? rankingSelected : rankingNormal;
            dashboardRankings[i].GetComponentInChildren<TMP_Text>().color = selected ? Color.white : settingsInk;
        }
    }
    void PreviewVolumes(SettingsSliderCommit committer)
    {
        if (refreshing) return;
        committer.MarkChanged();
        if (audioPlayer) audioPlayer.Preview(ReadSettings());
        RefreshVolumeLabels();
    }
    void RefreshVolumeLabels()
    {
        soundValue.text = sound.value <= 0 ? "Muted" : Mathf.RoundToInt(sound.value * 100) + "%";
        musicValue.text = music.value <= 0 ? "Muted" : Mathf.RoundToInt(music.value * 100) + "%";
    }
    void SaveSettings()
    {
        soundCommit.Clear(); musicCommit.Clear();
        bool saved = service.SaveSettings(ReadSettings());
        settingsStatus.text = saved ? "Settings saved" : service.LastError;
        settingsStatus.color = saved ? settingsInk : TaskUIBuilder.Overdue;
        if (!saved) RefreshSettings(false);
        if (audioPlayer) { audioPlayer.ApplySaved(); if (saved) audioPlayer.PlayPreview(); }
    }
    void RefreshSettings(bool clearStatus = true)
    {
        var saved = service.Settings;
        selectedRanking = saved.DashboardRanking;
        RefreshRankingSelection();
        refreshing = true;
        showDashboardRanking.SetIsOnWithoutNotify(!saved.hideDashboardRanking);
        showDashboardTasks.SetIsOnWithoutNotify(!saved.hideDashboardTasks);
        showDashboardDailyRewards.SetIsOnWithoutNotify(!saved.hideDashboardDailyRewards);
        showDashboardFocus.SetIsOnWithoutNotify(!saved.hideDashboardFocus);
        showDashboardCharacter.SetIsOnWithoutNotify(!saved.hideDashboardCharacter);
        notifications.SetIsOnWithoutNotify(saved.notificationsEnabled);
        sound.SetValueWithoutNotify(saved.soundVolume); music.SetValueWithoutNotify(saved.musicVolume);
        // Native sliders do not notify visual adapters during programmatic initialization.
        sound.onValueChanged.Invoke(sound.value); music.onValueChanged.Invoke(music.value);
        refreshing = false;
        soundCommit.Clear(); musicCommit.Clear(); RefreshVolumeLabels();
        if (clearStatus) settingsStatus.text = "";
    }
    void OnDataChanged() { if (tagsPage.activeSelf) RefreshTags(); }
    public void OpenTags() { RefreshTags(); Show(tagsPage, "Tags", addTag.gameObject); }
    void RefreshTags()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        var selectedRow = selected ? selected.GetComponentInParent<SettingsTagRow>() : null;
        string selectedId = selectedRow ? selectedRow.name : null;
        bool selectedDelete = selectedRow && selected == selectedRow.delete.gameObject;
        foreach (var row in rows) if (row) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        rows.Clear();
        var snapshot = service.Snapshot;
        emptyTags.gameObject.SetActive(snapshot.tags.Count == 0);
        foreach (var tag in snapshot.tags)
        {
            var row = Instantiate(tagRowTemplate, tagList); row.gameObject.SetActive(true);
            row.name = "Tag_" + tag.id;
            TaskUIBuilder.LayoutTag(row.chip, tag.name, 22, 42);
            FitChip(row.chip, 380);
            TagColors.Apply(row.chip, tag.colorHex, tagTintSprite);
            int count = snapshot.tasks.Count(t => t.tagIds.Contains(tag.id));
            row.count.text = count + (count == 1 ? " task" : " tasks");
            string id = tag.id;
            row.edit.onClick.AddListener(() => EditTag(id));
            row.delete.onClick.AddListener(() => ConfirmTagDelete(id));
            rows.Add(row);
        }
        if (tagsPage.activeInHierarchy)
        {
            BindNavigation(tagsPage);
            if (selectedId != null && EventSystem.current)
            {
                var replacement = rows.FirstOrDefault(r => r.name == selectedId);
                EventSystem.current.SetSelectedGameObject(replacement ? (selectedDelete ? replacement.delete.gameObject : replacement.edit.gameObject) : addTag.gameObject);
            }
        }
    }
    public void EditTag(string id)
    {
        var tag = service.Snapshot.tags.FirstOrDefault(t => t.id == id);
        editingTagId = tag?.id; selectedColor = TagColors.Normalize(tag?.colorHex);
        tagName.SetTextWithoutNotify(tag?.name ?? "");
        tagEditorTitle.text = tag == null ? "New tag" : "Edit tag";
        tagError.text = ""; UpdateTagPreview();
        Show(tagEditorPage, "TagEditor", tagName.gameObject); tagName.ActivateInputField();
    }
    void UpdateTagPreview()
    {
        TaskUIBuilder.LayoutTag(tagPreview, string.IsNullOrWhiteSpace(tagName.text) ? "Tag preview" : tagName.text.Trim(), 24, 48);
        FitChip(tagPreview, 700);
        TagColors.Apply(tagPreview, selectedColor, tagTintSprite);
        for (int i = 0; i < colorSelected.Length; i++) colorSelected[i].SetActive(TagColors.Palette[i] == selectedColor);
    }
    void SaveTag()
    {
        if (service.SaveTag(editingTagId, tagName.text, selectedColor, out _)) OpenTags();
        else tagError.text = service.LastError;
    }
    static void FitChip(GameObject chip, float maximum)
    {
        var rect = (RectTransform)chip.transform;
        // Editors are prepared while their page is inactive; LayoutUtility ignores
        // inactive LayoutElements and would collapse the first preview to zero.
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(maximum, chip.GetComponent<LayoutElement>().preferredWidth));
        chip.GetComponentInChildren<TMP_Text>(true).overflowMode = TextOverflowModes.Ellipsis;
    }
    void ConfirmTagDelete(string id)
    {
        var snapshot = service.Snapshot; var tag = snapshot.tags.FirstOrDefault(t => t.id == id);
        if (tag == null) { RefreshTags(); return; }
        deletingTagId = id;
        int count = snapshot.tasks.Count(t => t.tagIds.Contains(id));
        confirmMessage.text = "Delete \"" + tag.name + "\"?\n\nThis removes the tag from " + count + (count == 1 ? " task." : " tasks.") + " Your tasks and completion history will stay.";
        Show(confirmPage, "Confirm", cancelDelete.gameObject);
    }
    void DeleteTag()
    {
        if (service.DeleteTag(deletingTagId)) { deletingTagId = null; OpenTags(); }
        else confirmMessage.text = service.LastError;
    }
    public void OpenAccount()
    {
        nameDirty = false; RefreshAccount(); Show(accountPage, "Account", playerName.gameObject);
    }
    void RefreshAccount()
    {
        if (!account) return;
        var state = account.State;
        refreshing = true;
        if (!nameDirty && !playerName.isFocused) playerName.SetTextWithoutNotify(state.playerName ?? "");
        upload.SetIsOnWithoutNotify(state.uploadEnabled);
        refreshing = false;
        accountIdentity.text = state.signedIn ? state.accountLabel : "Play locally. Connect when you are ready.";
        accountStatus.text = state.status;
        comparison.text = state.comparison ?? "";
        comparison.gameObject.SetActive(state.conflict);
        googleSignIn.gameObject.SetActive(!state.signedIn);
        googleSignIn.interactable = state.configured && !state.busy;
        signOut.gameObject.SetActive(state.signedIn); signOut.interactable = true;
        // Turning sharing off is always local, including while signed out or on another account.
        upload.interactable = state.uploadEnabled || (state.signedIn && !state.busy && !state.wrongAccount);
        savePlayerName.interactable = !state.busy;
        retryUpload.gameObject.SetActive(state.signedIn); retryUpload.interactable = !state.busy;
        replaceStatistics.gameObject.SetActive(state.conflict); replaceStatistics.interactable = !state.busy;
        if (accountPage.activeSelf)
        {
            BindNavigation(accountPage);
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected && selected.transform.IsChildOf(accountPage.transform)
                && (!selected.activeInHierarchy || (selected.GetComponent<Selectable>() is Selectable control && !control.IsInteractable())))
                EventSystem.current.SetSelectedGameObject(playerName.gameObject);
        }
    }
}
