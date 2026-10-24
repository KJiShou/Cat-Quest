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
}
