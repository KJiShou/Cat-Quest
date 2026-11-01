using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Serialized, project-owned Settings extension. Closing the panel never cancels a download.
public sealed class AppUpdatePanel : MonoBehaviour
{
    public Button entry, primary, later, close, backdrop;
    public GameObject popup;
    public TMP_Text entryLabel, heading, details, status, primaryLabel, laterLabel;
    public CanvasGroup settingsContent;
    AppUpdateController controller;
    string lastBlock;
    int dismissedFrame = -1;
    public bool IsOpen => popup && popup.activeSelf;
    public bool OwnsInput => IsOpen || dismissedFrame == Time.frameCount;
    void Awake()
    {
        controller = AppUpdateController.Ensure();
        entry.onClick.AddListener(Open);
        primary.onClick.AddListener(Activate);
        later.onClick.AddListener(Dismiss); close.onClick.AddListener(Dismiss); backdrop.onClick.AddListener(Dismiss);
    }
    void OnEnable() { if (!controller) controller = AppUpdateController.Ensure(); if (controller) controller.Changed += Refresh; Refresh(); }
    void OnDisable() { if (controller) controller.Changed -= Refresh; if (popup) popup.SetActive(false); if (settingsContent) settingsContent.interactable = settingsContent.blocksRaycasts = true; }
    public void Open()
    {
        popup.SetActive(true); popup.transform.SetAsLastSibling(); settingsContent.interactable = settingsContent.blocksRaycasts = false;
        Refresh(); if (EventSystem.current) EventSystem.current.SetSelectedGameObject(later.gameObject);
        if (controller.State == AppUpdateState.Idle) controller.Check();
    }
    public void Dismiss()
    {
        if (controller.State == AppUpdateState.Installing) return;
        dismissedFrame = Time.frameCount;
        popup.SetActive(false); settingsContent.interactable = settingsContent.blocksRaycasts = true;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(entry.gameObject);
    }
    void Activate()
    {
        switch (controller.State)
        {
            case AppUpdateState.Available: controller.Download(); break;
            case AppUpdateState.Downloading: controller.CancelDownload(); break;
            case AppUpdateState.Ready: controller.Install(); break;
            default: controller.Check(); break;
        }
    }
    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) Dismiss();
        if (controller.State == AppUpdateState.Ready && lastBlock != AppUpdateController.RestartBlockReason()) Refresh();
        if (Input.GetKeyDown(KeyCode.Tab) && EventSystem.current)
        {
            var controls = new[] { primary, later, close }.Where(b => b.IsInteractable()).ToArray();
            if (controls.Length == 0) return;
            int index = System.Array.FindIndex(controls, b => b.gameObject == EventSystem.current.currentSelectedGameObject);
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            EventSystem.current.SetSelectedGameObject(controls[(index + direction + controls.Length) % controls.Length].gameObject);
        }
    }
    public void Refresh()
    {
        if (!controller || !entryLabel) return;
        var state = controller.State;
        entryLabel.text = state == AppUpdateState.Available || state == AppUpdateState.Ready ? "Update available" : "Updates  ·  v" + Application.version;
        heading.text = "Cat Quest Updates";
        details.text = "Installed version " + Application.version + (controller.Release == null ? "" : "\nLatest release " + controller.Release.version + "  ·  " + (controller.Release.size / 1048576f).ToString("0.0") + " MB\n\n" + controller.Release.notes);
        status.text = controller.Message + (state == AppUpdateState.Downloading ? "  " + (controller.Progress * 100).ToString("0") + "%" : "");
        string blocked = state == AppUpdateState.Ready ? AppUpdateController.RestartBlockReason() : null;
        lastBlock = blocked;
        if (blocked != null) status.text = blocked;
        primaryLabel.text = state == AppUpdateState.Available ? "Download" : state == AppUpdateState.Downloading ? "Cancel download" : state == AppUpdateState.Verifying ? "Verifying..." : state == AppUpdateState.Ready ? "Restart & update" : "Check again";
        primary.interactable = state != AppUpdateState.Checking && state != AppUpdateState.Verifying && state != AppUpdateState.Installing && blocked == null;
        later.interactable = close.interactable = backdrop.interactable = state != AppUpdateState.Installing;
        laterLabel.text = state == AppUpdateState.Downloading ? "In background" : "Later";
    }
}
