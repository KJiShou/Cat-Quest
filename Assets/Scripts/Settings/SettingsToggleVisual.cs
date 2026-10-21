using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public sealed class SettingsToggleVisual : MonoBehaviour
{
    public RectTransform handle;
    public Image track;
    public Sprite onSprite, offSprite;
    public float travel = 23;
    Toggle toggle;
    void Awake() => toggle = GetComponent<Toggle>();
    void OnEnable() { if (!toggle) toggle = GetComponent<Toggle>(); Refresh(true); }
    void Update() => Refresh(false);
    void Refresh(bool immediate)
    {
        if (!handle || !toggle) return;
        float x = toggle.isOn ? travel : -travel;
        handle.anchoredPosition = new Vector2(immediate ? x : Mathf.Lerp(handle.anchoredPosition.x, x, 1 - Mathf.Exp(-24 * Time.unscaledDeltaTime)), 0);
        if (track) track.sprite = toggle.isOn ? onSprite : offSprite;
    }
    void OnDisable() => Refresh(true);
}
