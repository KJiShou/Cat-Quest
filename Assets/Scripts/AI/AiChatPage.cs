using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AiChatPage : MonoBehaviour
{
    public TMP_InputField input;
    public TMP_Text status, empty;
    public TMP_Text reviewStatus;
    public AiThinkingWave thinking;
    public Button send, stop, retry, clear, settings, today, plan;
    public RectTransform content;
    public ScrollRect scroll;
    public AiChatRow messageTemplate;
    public AiChatDraftCard draftTemplate;
    public EditTaskDetailsUI editor;
    public Button saveDraft, cancelDraft;
    public GameObject editorOverlay, clearDialog;
    public Button confirmClear, cancelClear;
    ChatGptClient client;
    AiChatSession session;
    AiTaskDraft editing;
    int rendered = -1;
    bool requestedModels;
    void Awake()
    {
        var canvas = GetComponentInParent<Canvas>().rootCanvas.transform;
        editorOverlay.transform.SetParent(canvas, false); TaskUIBuilder.Stretch((RectTransform)editorOverlay.transform);
        send.onClick.AddListener(Send);
        stop.onClick.AddListener(() => session?.Stop());
        retry.onClick.AddListener(() => session?.Retry());
        clear.onClick.AddListener(() => { clearDialog.SetActive(true); Focus(cancelClear.gameObject); });
        cancelClear.onClick.AddListener(() => clearDialog.SetActive(false));
        confirmClear.onClick.AddListener(() => { CloseDraft(); session?.Clear(); clearDialog.SetActive(false); });
        today.onClick.AddListener(() => SendText("What tasks do I have today?"));
        plan.onClick.AddListener(() => { input.text = "Help me plan tasks for "; input.ActivateInputField(); });
        settings.onClick.AddListener(() => {
            FindFirstObjectByType<SettingsLauncher>().OpenAIFromChat(settings.gameObject);
        });
        saveDraft.onClick.AddListener(() => {
            if (editing == null) return;
            var draft = editor.ReadDraft(out _);
            if (session.Confirm(editing, draft)) CloseDraft();
            else { reviewStatus.text = session.Status; reviewStatus.gameObject.SetActive(true); }
        });
        cancelDraft.onClick.AddListener(CloseDraft);
        editor.transform.Find("Header/Close").GetComponent<Button>().onClick.AddListener(CloseDraft);
        input.onSubmit.AddListener(_ => { if (string.IsNullOrEmpty(Input.compositionString)) Send(); });
        input.lineType = TMP_InputField.LineType.MultiLineSubmit;
        input.characterLimit = 2000;
        input.onValueChanged.AddListener(_ => Refresh());
    }
    void OnEnable() { client = ChatGptClient.EnsureInstance(); client.Changed += Refresh; Refresh(); }
    void OnDisable()
    {
        if (client) client.Changed -= Refresh;
        if (session != null) session.Changed -= Refresh;
        session = null; CloseDraft(); clearDialog.SetActive(false);
    }
    public void SendText(string text)
    {
        if (!client.Enabled || session == null || session.Busy) return;
        session.Send(text); Refresh();
    }
    void Send()
    {
        if (!send.interactable) return;
        int count = session.History.messages.Count;
        SendText(input.text);
        if (session.History.messages.Count > count) input.text = "";
    }
    void Refresh()
    {
        if (!client) return;
        if (client.Ready && !client.Busy && client.Connection.Connected && string.IsNullOrEmpty(client.Connection.Model) && !requestedModels) {
            requestedModels=true; client.LoadModels(); return;
        }
        if (session != client.Chat) {
            if (session != null) session.Changed -= Refresh;
            session = client.Chat; if (session != null) session.Changed += Refresh; rendered = -1;
        }
        bool ready = client.Enabled && session != null && session.Loaded;
        bool busy = session != null && session.Busy;
        status.text = !client.Ready ? "Loading AI connection…" : !client.Connection.Connected ? "Connect ChatGPT in AI Settings to start chatting." : session?.Status;
        status.gameObject.SetActive(!string.IsNullOrEmpty(status.text));
        thinking.enabled = busy;
        send.interactable = ready && !busy && !string.IsNullOrWhiteSpace(input.text);
        today.interactable = plan.interactable = ready && !busy;
        stop.gameObject.SetActive(busy); retry.gameObject.SetActive(!busy && session != null && session.CanRetry);
        retry.interactable = ready; clear.interactable = session != null;
        if (session == null) return;
        // Changes to draft states redraw cards, while text entry never rebuilds the conversation.
        int signature = session.History.messages.Count;
        foreach (var m in session.History.messages) foreach (var d in m.drafts) signature = unchecked(signature * 31 + d.state.GetHashCode());
        if (rendered == signature) return; rendered = signature;
        foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        empty.gameObject.SetActive(session.History.messages.Count == 0);
        foreach (var message in session.History.messages) {
            var row = Instantiate(messageTemplate, content); row.text.font=status.font; row.gameObject.SetActive(true); row.Bind(message);
            foreach (var draft in message.drafts) {
                var card = Instantiate(draftTemplate, content); card.summary.font=status.font; card.gameObject.SetActive(true);
                card.Bind(draft, () => OpenDraft(draft), () => session.Dismiss(draft));
            }
        }
        Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0;
    }
    public void OpenDraft(AiTaskDraft draft)
    {
        if (draft.state != "pending") return;
        editing = draft; editorOverlay.transform.SetAsLastSibling(); editorOverlay.SetActive(true); editor.gameObject.SetActive(true);
        editor.LoadDraft(PlayerDataStore.Copy(draft.task), GameDataManager.EnsureInstance().Tasks.Snapshot.tags, draft.completed);
        editor.transform.Find("Header/Heading").GetComponent<TMP_Text>().text = string.IsNullOrEmpty(draft.targetId) ? "Review New Task" : "Review Task Changes";
        saveDraft.GetComponentInChildren<TMP_Text>().text = "Confirm & Save";
        if(reviewStatus) { reviewStatus.text=""; reviewStatus.gameObject.SetActive(false); }
        foreach(var toggle in editor.subtaskContent.GetComponentsInChildren<Toggle>(true)) toggle.interactable=false;
        Focus(editor.taskTitle.gameObject);
    }
    static void Focus(GameObject target) { if(UnityEngine.EventSystems.EventSystem.current) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(target); }
    void Update()
    {
        if(!Input.GetKeyDown(KeyCode.Tab) && !Input.GetKeyDown(KeyCode.Escape)) return;
        if(FindFirstObjectByType<SettingsPanelUI>()) return;
        if(Input.GetKeyDown(KeyCode.Escape)) {
            if(clearDialog.activeSelf) { clearDialog.SetActive(false); Focus(clear.gameObject); }
            else if(editorOverlay.activeSelf) {
                if(editor.datePicker.overlay.activeSelf) editor.datePicker.overlay.SetActive(false);
                else if(editor.timePicker.overlay.activeSelf) editor.timePicker.overlay.SetActive(false);
                else { CloseDraft(); Focus(input.gameObject); }
            }
            return;
        }
        var root=clearDialog.activeSelf?clearDialog:editorOverlay.activeSelf?editorOverlay:gameObject;
        var controls=root.GetComponentsInChildren<Selectable>().Where(s=>s.IsInteractable()).ToArray();
        if(controls.Length==0 || !UnityEngine.EventSystems.EventSystem.current)return;
        int current=Array.FindIndex(controls,s=>s.gameObject==UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject);
        int direction=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?-1:1;
        Focus(controls[(current+direction+controls.Length)%controls.Length].gameObject);
    }
    void OnDestroy() { if(editorOverlay) Destroy(editorOverlay); }
    void CloseDraft()
    {
        editing = null;
        if (editor) { if(editor.datePicker.overlay) editor.datePicker.overlay.SetActive(false); if(editor.timePicker.overlay) editor.timePicker.overlay.SetActive(false); }
        if (editorOverlay) editorOverlay.SetActive(false);
    }
}
