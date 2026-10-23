using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public sealed class SettingsSliderVisual : MonoBehaviour
{
    public Image handle, fill, icon;
    public Sprite enabledHandle, mutedHandle;
    public Sprite enabledIcon, mutedIcon;
    public Color iconColor = new Color32(113,132,168,255);
    public Color enabledHandleColor = Color.white, mutedHandleColor = Color.white;
    Slider slider;
    void Awake() => slider = GetComponent<Slider>();
    void OnEnable() { if (!slider) slider = GetComponent<Slider>(); slider.onValueChanged.AddListener(Refresh); Refresh(slider.value); }
    void OnDisable() { if (slider) slider.onValueChanged.RemoveListener(Refresh); }
    void Refresh(float volume)
    {
        bool audible = volume > 0;
        if (handle) { handle.sprite = audible ? enabledHandle : mutedHandle; handle.color = audible ? enabledHandleColor : mutedHandleColor; }
        if (fill) fill.enabled = audible;
        if (icon)
        {
            if (enabledIcon && mutedIcon) icon.sprite = audible ? enabledIcon : mutedIcon;
            var color = iconColor; if (!audible) color.a *= .45f; icon.color = color;
        }
    }
}
