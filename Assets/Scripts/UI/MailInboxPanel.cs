using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using CatQuest.Updates;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MailInboxPanel : MonoBehaviour
{
    [SerializeField] Button entry, close, backdrop, action, back, delete;
    [SerializeField] GameObject badge, modal, entrySlot, detail, empty, listFade;
    [SerializeField] TMP_Text title, body, status, actionLabel, deleteLabel;
    [SerializeField] MailRewardIcons attachments;
    [SerializeField] RectTransform rows;
    [SerializeField] ScrollRect detailScroll;
    [SerializeField] MailRowView rowPrefab;

    sealed class Entry
    {
        internal readonly MailLetter Letter;
        internal readonly string Key, Title, Body, BonusVersion;
        internal bool IsUpdate => Letter == null && BonusVersion == null;
        internal RewardAmounts Reward => Letter?.Reward ?? SqlitePlayerService.VersionGift;
        internal Entry(Version version)
        {
            BonusVersion = version.ToString();
            Key = SqlitePlayerService.BonusReceiptPrefix + BonusVersion;
            Title = "Update gift · Cat Quest " + version.ToString(3);
            Body = "Thanks for updating Cat Quest! Claim your upgrade gift below.";
        }
        internal Entry(MailLetter letter)
        { Letter = letter; Key = letter.Key; Title = letter.title; Body = letter.body; }
        internal Entry(ReleaseManifest update)
        {
            Key = "update:" + UpdateRules.VersionOf(update.version);
            Title = "Cat Quest " + update.version + " is available";
            Body = string.IsNullOrWhiteSpace(update.notes) ? "A new version is ready to download." : update.notes;
            Body += "\n\nYour upgrade gift will be available after installing the update.";
        }
    }

    readonly Dictionary<string, MailRowView> views = new Dictionary<string, MailRowView>(StringComparer.Ordinal);
    Dictionary<string, int> states = new Dictionary<string, int>(StringComparer.Ordinal);
    Entry[] letters = Array.Empty<Entry>();
    MailInbox inbox;
    SqlitePlayerService service;
    AppUpdateController updates;
    ReleaseManifest observedRelease;
    SettingsLauncher settings;
    ScrollRect listScroll;
    GameObject lastFocus;
    Sprite actionSprite, deleteSprite;
    Color actionColor, deleteColor;
    string selected, notice;
    bool refreshing, claiming, confirmingDelete, deleting;
    int loadVersion;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static Func<Task<string>> VerificationFeed;
#endif

    void Awake()
    {
        modal.SetActive(false);
        badge.SetActive(false);
        entrySlot.SetActive(AppPlatform.WindowsDesktop);
        if (!AppPlatform.WindowsDesktop) { enabled = false; return; }
        listScroll = rows.GetComponentInParent<ScrollRect>(true);
        actionSprite = action.image.sprite;
        deleteSprite = delete.image.sprite;
        actionColor = actionLabel.color;
        deleteColor = deleteLabel.color;
        entry.onClick.AddListener(Open);
        close.onClick.AddListener(Close);
        backdrop.onClick.AddListener(Close);
        back.onClick.AddListener(ShowList);
        action.onClick.AddListener(() => {
            if (confirmingDelete) DeleteSelected();
            else Activate(selected);
        });
        delete.onClick.AddListener(ToggleDelete);
    }

    async void Start()
    {
        try
        {
            var manager = GameDataManager.EnsureInstance();
            var opened = await manager.Ready;
            if (!this) return;
            if (!opened.Succeeded)
            {
                notice = "Your save could not be opened.";
                Render();
                return;
            }

            service = manager.Service;
            settings = transform.root.GetComponentInChildren<SettingsLauncher>(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var feed = VerificationFeed;
            if (feed == null && CompanionProtocol.HasArgument("--focus-smoke"))
                feed = () => Task.Run(() => File.ReadAllText(manager.SavePath + ".mail-test.json"));
            feed ??= () => Task.FromException<string>(new IOException("Mail verification feed is not configured."));
            inbox = new MailInbox(service, manager.SavePath, feed);
#else
            inbox = new MailInbox(service, manager.SavePath);
#endif
            await inbox.LoadCacheAsync();
            var observed = await service.ObserveMailVersionAsync(Application.version);
            if (!observed.Succeeded) notice = observed.Message;
            if (!this) return;
            service.RecordsChanged += OnRecordsChanged;
            updates = AppUpdateController.Ensure();
            if (updates) updates.Changed += OnUpdateChanged;
            OnUpdateChanged();
            await ReloadAsync();
            if (this) await RefreshAsync();
        }
        catch (Exception error)
        {
            if (!this) return;
            Debug.LogException(error, this);
            notice = "Could not open the mailbox. Try reopening it.";
            Render();
        }
    }

    void OnDestroy()
    {
        if (service != null) service.RecordsChanged -= OnRecordsChanged;
        if (updates) updates.Changed -= OnUpdateChanged;
    }

    void OnDisable()
    {
        confirmingDelete = false;
        if (modal) modal.SetActive(false);
    }

    async void OnUpdateChanged()
    {
        if (inbox == null || !updates) return;
        var release = updates.Release;
        if (release == null || !UpdateRules.IsNewer(release.version, Application.version)
            || ReferenceEquals(observedRelease, release)) return;
        observedRelease = release;

        await inbox.StoreUpdateAsync(release);
        if (this) await ReloadAsync();
    }

    async void OnRecordsChanged(IReadOnlyCollection<PlayerRecordKind> kinds)
    {
        if (kinds == null || kinds.Contains(PlayerRecordKind.Mail)) await ReloadAsync();
    }

    void Open()
    {
        selected = null;
        confirmingDelete = false;
        modal.SetActive(true);
        transform.SetAsLastSibling();
        notice = null;
        Render();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(close.gameObject);
        _ = RefreshAsync();
    }

    void Close()
    {
        confirmingDelete = false;
        modal.SetActive(false);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(entry.gameObject);
    }

    async Task RefreshAsync()
    {
        if (inbox == null || refreshing || claiming) return;
        refreshing = true;
        Render();
        try
        {
            bool refreshed = await inbox.RefreshAsync();
            if (!this) return;
            notice = refreshed ? null : inbox.Letters().Length > 0
                ? "Mail could not refresh. Saved letters are available offline."
                : "Could not refresh mail. Check your connection.";
            await ReloadAsync();
        }
        catch (Exception error)
        {
            if (this) { Debug.LogException(error, this); notice = "Could not refresh mail."; }
        }
        finally
        {
            refreshing = false;
            if (this) Render();
        }
    }

    async Task ReloadAsync()
    {
        if (!this || inbox == null) return;
        int generation = ++loadVersion;
        var current = inbox.Letters().Select(letter => new Entry(letter)).ToList();
        var gifts = await service.ReadVersionGiftsAsync();
        if (!this || generation != loadVersion) return;
        if (gifts.Succeeded) current.InsertRange(0, gifts.Value.Select(version => new Entry(version)));
        if (inbox.Update != null && UpdateRules.IsNewer(inbox.Update.version, Application.version))
            current.Insert(0, new Entry(inbox.Update));
        var saved = await service.ReadMailStatesAsync(current.Select(letter => letter.Key));
        if (!this || generation != loadVersion) return;
        if (saved.Succeeded) states = saved.Value;

        letters = current.Where(letter => State(letter.Key) != SqlitePlayerService.DeletedMailState).ToArray();
        if (selected != null && !letters.Any(letter => letter.Key == selected))
        {
            selected = null;
            confirmingDelete = false;
        }
        Render();
    }

    async void Select(string key)
    {
        if (!letters.Any(letter => letter.Key == key)) return;
        selected = key;
        confirmingDelete = false;
        notice = null;
        detailScroll.verticalNormalizedPosition = 1;
        Render();
        if (State(key) != 0) return;
        var read = await service.ReadLetterAsync(key);
        if (!this || read.Succeeded || key != selected) return;
        notice = read.Message;
        Render();
    }

    void ShowList()
    {
        if (confirmingDelete)
        {
            ToggleDelete();
            return;
        }
        selected = null;
        Render();
    }

    void ToggleDelete()
    {
        if (selected == null || claiming || deleting) return;
        confirmingDelete = !confirmingDelete;
        notice = null;
        Render();
    }

    async void DeleteSelected()
    {
        string key = selected;
        if (!confirmingDelete || key == null || claiming || deleting) return;
        confirmingDelete = false;
        deleting = true;
        Render();
        try
        {
            var result = await service.DeleteLetterAsync(key);
            if (!this) return;
            notice = result.Succeeded ? "Mail deleted." : result.Message;
            if (result.Succeeded) await ReloadAsync();
        }
        catch (Exception error)
        {
            if (this) { Debug.LogException(error, this); notice = "Could not delete this letter. Try again."; }
        }
        finally
        {
            deleting = false;
            if (this) Render();
        }
    }

    async void Activate(string key)
    {
        var letter = letters.FirstOrDefault(item => item.Key == key);
        if (letter == null || claiming || deleting || confirmingDelete) return;
        if (letter.IsUpdate)
        {
            if (settings)
            {
                Close();
                settings.OpenUpdates(entry.gameObject);
            }
            return;
        }

        claiming = true;
        notice = null;
        Render();
        try
        {
            var result = letter.BonusVersion == null ? await inbox.ClaimAsync(letter.Letter)
                : await service.ClaimVersionGiftAsync(letter.BonusVersion);
            if (!this) return;
            notice = result.Succeeded
                ? result.Message ?? (result.Value ? "Reward added to your save." : "This reward has already been claimed.")
                : result.Message;
            await ReloadAsync();
        }
        catch (Exception error)
        {
            if (this) { Debug.LogException(error, this); notice = "Could not claim this reward."; }
        }
        finally
        {
            claiming = false;
            if (this) Render();
        }
    }

    int State(string key) => states.TryGetValue(key, out var value) ? value : 0;

    void Render()
    {
        badge.SetActive(letters.Any(letter => State(letter.Key) == 0 || !letter.IsUpdate && State(letter.Key) < 2));
        if (!modal.activeSelf) return;
        foreach (var removed in views.Keys.Except(letters.Select(letter => letter.Key)).ToArray())
        {
            views[removed].gameObject.SetActive(false);
            Destroy(views[removed].gameObject);
            views.Remove(removed);
        }

        for (int i = 0; i < letters.Length; i++)
        {
            var letter = letters[i];
            if (!views.TryGetValue(letter.Key, out var row))
            {
                row = Instantiate(rowPrefab, rows, false);
                views.Add(letter.Key, row);
                row.Control.onClick.AddListener(() => Select(letter.Key));
                row.Primary.onClick.AddListener(() => Activate(letter.Key));
            }
            row.transform.SetSiblingIndex(i);
            int state = State(letter.Key);
            row.Display(letter.Title, letter.Body, letter.Reward,
                letter.IsUpdate ? "Update" : state == 2 ? "Claimed" : "Claim", state == 0,
                !claiming && !deleting && (letter.IsUpdate || state < 2 && (letter.BonusVersion != null || !refreshing)), letter.IsUpdate);
        }

        var shown = letters.FirstOrDefault(letter => letter.Key == selected);
        listScroll.gameObject.SetActive(shown == null);
        detail.SetActive(shown != null);
        back.gameObject.SetActive(shown != null);
        empty.SetActive(shown == null && letters.Length == 0);
        title.text = shown?.Title ?? (letters.Length > 0 ? "Select a letter" : "No mail yet");
        body.text = shown?.Body ?? "";
        int mailState = shown == null ? 0 : State(shown.Key);
        attachments.Display(shown?.Reward ?? default);
        action.gameObject.SetActive(shown != null);
        actionLabel.text = confirmingDelete ? "Confirm delete" : shown == null || shown.IsUpdate ? "Go to update" : mailState == 2 ? "Claimed" : "Claim";
        action.image.sprite = confirmingDelete ? deleteSprite : actionSprite;
        actionLabel.color = confirmingDelete ? deleteColor : actionColor;
        action.interactable = shown != null && !claiming && !deleting
            && (confirmingDelete || shown.IsUpdate || mailState < 2 && (shown.BonusVersion != null || !refreshing));
        delete.gameObject.SetActive(shown != null);
        delete.interactable = shown != null && !claiming && !deleting;
        deleteLabel.text = confirmingDelete ? "Keep mail" : "Delete";
        delete.image.sprite = confirmingDelete ? actionSprite : deleteSprite;
        deleteLabel.color = confirmingDelete ? actionColor : deleteColor;
        status.text = deleting ? "Deleting mail..." : confirmingDelete
            ? shown != null && !shown.IsUpdate && mailState < 2 ? "Delete this letter and its unclaimed reward?" : "Delete this letter?"
            : claiming ? "Verifying reward..." : refreshing ? "Checking mail..." : notice ?? (inbox == null ? "Opening your save..." : "");
        BindNavigation();
    }

    List<Button> Controls()
    {
        var buttons = new List<Button> { close };
        if (detail.activeSelf) buttons.Add(back);
        else foreach (var letter in letters)
        {
            var row = views[letter.Key];
            buttons.Add(row.Control);
            if (row.Primary.IsInteractable()) buttons.Add(row.Primary);
        }
        if (delete.IsActive() && delete.IsInteractable()) buttons.Add(delete);
        if (action.IsActive() && action.IsInteractable()) buttons.Add(action);
        return buttons;
    }

    void BindNavigation()
    {
        var controls = Controls();
        for (int i = 0; i < controls.Count; i++)
        {
            var previous = controls[(i + controls.Count - 1) % controls.Count];
            var next = controls[(i + 1) % controls.Count];
            controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
        }
        if (EventSystem.current && !controls.Exists(button => button.gameObject == EventSystem.current.currentSelectedGameObject))
            EventSystem.current.SetSelectedGameObject(close.gameObject);
    }

    void LateUpdate()
    {
        if (!modal.activeSelf || !EventSystem.current) return;
        bool showFade = listScroll.isActiveAndEnabled && rows.rect.height > listScroll.viewport.rect.height + 1
            && listScroll.verticalNormalizedPosition > .01f;
        if (listFade && listFade.activeSelf != showFade) listFade.SetActive(showFade);
        var focus = EventSystem.current.currentSelectedGameObject;
        if (focus == lastFocus) return;
        lastFocus = focus;
        if (!focus || !listScroll.isActiveAndEnabled) return;
        var row = focus.GetComponentInParent<MailRowView>();
        if (!row || row.transform.parent != rows) return;
        Canvas.ForceUpdateCanvases();
        var item = RectTransformUtility.CalculateRelativeRectTransformBounds(listScroll.viewport, (RectTransform)row.transform);
        Rect visible = listScroll.viewport.rect;
        float hiddenHeight = rows.rect.height - visible.height;
        if (hiddenHeight <= 0) return;
        float correction = item.max.y > visible.yMax ? visible.yMax - item.max.y
            : item.min.y < visible.yMin ? visible.yMin - item.min.y : 0;
        listScroll.verticalNormalizedPosition = Mathf.Clamp01(listScroll.verticalNormalizedPosition - correction / hiddenHeight);
    }

    void Update()
    {
        if (!modal.activeSelf) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (confirmingDelete) ToggleDelete();
            else Close();
            return;
        }
        if (Input.GetKeyDown(KeyCode.PageDown)) detailScroll.verticalNormalizedPosition -= .25f;
        if (Input.GetKeyDown(KeyCode.PageUp)) detailScroll.verticalNormalizedPosition += .25f;
        if (!Input.GetKeyDown(KeyCode.Tab) || !EventSystem.current) return;
        var controls = Controls();
        int index = controls.FindIndex(button => button.gameObject == EventSystem.current.currentSelectedGameObject);
        bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        int next = index < 0 ? 0 : (index + (back ? -1 : 1) + controls.Count) % controls.Count;
        EventSystem.current.SetSelectedGameObject(controls[next].gameObject);
    }
}
