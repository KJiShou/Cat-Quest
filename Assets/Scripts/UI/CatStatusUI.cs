using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CatStatusUI : MonoBehaviour
{
    public TMP_Text level, experience, playerName;
    public Slider experienceBar;
    private TaskService service;
    private void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += Refresh; Refresh();
    }
    private void OnDisable() { if (service != null) service.Changed -= Refresh; }
    private void Refresh()
    {
        TaskService.CatLevel(service.Experience, out int catLevel, out int xp, out int required);
        if (playerName) playerName.text = string.IsNullOrWhiteSpace(service.PlayerName) ? "Player" : service.PlayerName;
        level.text = "LV " + catLevel;
        experience.text = $"{xp} / {required}";
        experienceBar.maxValue = required;
        experienceBar.SetValueWithoutNotify(xp);
    }
}
