using UnityEngine;

public sealed class SettingsLauncher : MonoBehaviour
{
    public SettingsPanelUI settingsPrefab;
    [SerializeField] SettingsPanelUI instance;
    public void Open()
    {
        if (!instance) return;
        instance.Open(gameObject);
    }
    public void OpenAIFromChat(GameObject opener)
    {
        if (instance) instance.OpenAIFromChat(opener);
    }
    public void OpenAccountFromPage(GameObject opener)
    {
        if (instance) instance.OpenAccountFromPage(opener);
    }
}
