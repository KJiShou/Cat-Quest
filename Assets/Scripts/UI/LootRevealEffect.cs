using UnityEngine;

// Presentation of committed Legendary results; the shared particle graphic owns drawing.
public sealed class LootRevealEffect : MonoBehaviour
{
    public ChestRewardParticles view;
    bool requested, focused = true;
    public bool IsPlaying => view && view.IsLegendaryActive;
    public void Play() { requested = true; Refresh(); }
    void Refresh()
    {
        if (!requested || !focused || UiMotion.Reduced || !isActiveAndEnabled) { Release(); return; }
        if (view && !view.IsLegendaryActive) view.PlayLegendary((RectTransform)transform);
    }
    void Update() { if (requested) Refresh(); }
    void OnApplicationFocus(bool value) { focused = value; Refresh(); }
    void OnDisable() { Stop(); }
    void OnDestroy() { Stop(); }
    public void Stop() { requested = false; Release(); }
    void Release() { if (view) view.StopLegendary(); }
}
