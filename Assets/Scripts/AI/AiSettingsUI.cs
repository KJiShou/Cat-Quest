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
    void Awake()
    {
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
        privacy.text = "Chat sends your messages and relevant tasks requested in the conversation to OpenAI using your ChatGPT plan. Task changes require your confirmation. Chat history is saved locally; only recent messages are included in requests.";
    }
    void OnEnable()
    {
        client = ChatGptClient.EnsureInstance(); client.Changed += Refresh;
        if (client.Ready && client.Connection.Connected && client.Connection.Models.Count == 0) client.LoadModels();
        Refresh();
    }
    void OnDisable() { if (client) { client.Changed -= Refresh; client.CancelSignIn(); } }
    public void Refresh()
    {
        if (!client) return;
        var connection = client.Connection;
        identity.text = connection.Connected ? connection.Account : "Connect your ChatGPT plan";
        status.text = client.Status;
        modelLabel.text = connection.Connected ? "Model · using your ChatGPT plan" : "Model";
        connect.gameObject.SetActive(!connection.Connected); connect.interactable = client.Ready && !client.Busy;
        disconnect.gameObject.SetActive(connection.Connected); disconnect.interactable = !client.Busy;
        cancel.gameObject.SetActive(client.SigningIn);
        language.SetValueWithoutNotify(connection.Language == "en" ? 1 : connection.Language == "zh" ? 2 : 0);
        language.interactable = client.Ready && !client.Busy;
        model.ClearOptions(); model.AddOptions(connection.Models.Count > 0 ? connection.Models.Select(m => m.name).ToList() :
            new System.Collections.Generic.List<string> { "Connect ChatGPT to load models" });
        model.SetValueWithoutNotify(Mathf.Max(0, connection.Models.FindIndex(m => m.slug == connection.Model)));
        model.interactable = client.Ready && connection.Connected && !client.Busy && connection.Models.Count > 0;
        reload.interactable = client.Ready && connection.Connected && !client.Busy;
        GetComponentInParent<SettingsPanelUI>().RefreshAiNavigation();
    }
}
