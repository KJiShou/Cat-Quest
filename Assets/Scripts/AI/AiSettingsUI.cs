using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AiSettingsUI : MonoBehaviour
{
    public TMP_Text identity, status, privacy, modelLabel;
    public Button connect, disconnect, cancel, usage, reload;
    public TMP_Dropdown model, language;
    ChatGptClient client;
    bool checkModelsOnOpen;
    RectTransform popup;
    float compactHeight;
    void Awake()
    {
        popup = (RectTransform)identity.transform.parent;
        compactHeight = popup.rect.height;
        connect.onClick.AddListener(() => client.Connect());
        disconnect.onClick.AddListener(() => client.Disconnect());
        cancel.onClick.AddListener(() => client.CancelSignIn());
        reload.onClick.AddListener(() => client.LoadModels());
        usage.onClick.AddListener(() => Application.OpenURL("https://chatgpt.com/settings/usage"));
        model.onValueChanged.AddListener(value => {
            var models = client.Connection.Models;
            if (value >= 0 && value < models.Count) client.Preferences(models[value].slug);
        });
        language.onValueChanged.AddListener(value => client.Preferences(client.Connection.Model, new[] { "auto", "en", "zh" }[value]));
        privacy.text = "Messages and tasks you ask about are sent to OpenAI using your ChatGPT plan. Task changes need your confirmation. History is saved locally; requests include recent messages.";
    }
    void OnEnable()
    {
        client = ChatGptClient.EnsureInstance(); client.Changed += Refresh;
        checkModelsOnOpen = true;
        Refresh();
    }
    void OnDisable() { if (client) { client.Changed -= Refresh; client.CancelSignIn(); } }
    public void Refresh()
    {
        if (!client) return;
        if (!AppPlatform.WindowsDesktop) {
            identity.text = "AI Chat";
            status.text = AppPlatform.DesktopMessage;
            status.gameObject.SetActive(true); privacy.gameObject.SetActive(false);
            connect.interactable = model.interactable = language.interactable = reload.interactable = usage.interactable = false;
            disconnect.gameObject.SetActive(false); cancel.gameObject.SetActive(false);
            popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, compactHeight + MessageHeight(status));
            GetComponentInParent<SettingsPanelUI>().RefreshAiNavigation();
            return;
        }
        var connection = client.Connection;
        if (checkModelsOnOpen && client.Ready)
        {
            checkModelsOnOpen = false;
            if (connection.Connected && !client.Busy && connection.Models.Count == 0)
            {
                client.LoadModels();
                return;
            }
        }
        privacy.gameObject.SetActive(client.Ready && !connection.Connected);
        identity.text = connection.Connected ? connection.Account : "Connect your ChatGPT plan";
        status.text = client.Status;
        status.gameObject.SetActive(!string.IsNullOrEmpty(client.Status));
        // The heading is top-anchored and the controls are bottom-anchored.
        // Expand only for messages currently displayed between them.
        float height = compactHeight + MessageHeight(privacy) + MessageHeight(status);
        popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        modelLabel.text = "Model";
        connect.gameObject.SetActive(!connection.Connected); connect.interactable = client.Ready && !client.Busy;
        disconnect.gameObject.SetActive(connection.Connected); disconnect.interactable = !client.Busy;
        cancel.gameObject.SetActive(client.SigningIn);
        language.SetValueWithoutNotify(connection.Language == "en" ? 1 : connection.Language == "zh" ? 2 : 0);
        language.interactable = client.Ready && !client.Busy;
        model.ClearOptions(); model.AddOptions(connection.Models.Count > 0 ? connection.Models.Select(m => m.name).ToList() :
            new System.Collections.Generic.List<string> { connection.Connected ? (client.Busy ? "Loading models..." : "No models available") : "Connect ChatGPT to load models" });
        model.SetValueWithoutNotify(Mathf.Max(0, connection.Models.FindIndex(m => m.slug == connection.Model)));
        model.interactable = client.Ready && connection.Connected && !client.Busy && connection.Models.Count > 0;
        reload.interactable = client.Ready && connection.Connected && !client.Busy;
        GetComponentInParent<SettingsPanelUI>().RefreshAiNavigation();
    }
    static float MessageHeight(TMP_Text message)
    {
        if (!message.gameObject.activeSelf) return 0;
        float height = Mathf.Ceil(message.GetPreferredValues(message.text, message.rectTransform.rect.width, Mathf.Infinity).y);
        message.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        return height + 8f;
    }
}
