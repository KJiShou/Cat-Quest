using UnityEngine;

public sealed class SettingsLauncher : MonoBehaviour
{
    public SettingsPanelUI settingsPrefab;
    SettingsPanelUI instance;
    public void Open()
    {
        if (!settingsPrefab) return;
        if (!instance) instance = Instantiate(settingsPrefab, GetComponentInParent<Canvas>().rootCanvas.transform);
        instance.Open(gameObject);
    }
    void OnDestroy() { if (instance) Destroy(instance.gameObject); }
}
