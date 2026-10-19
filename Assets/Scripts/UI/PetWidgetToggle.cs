using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public sealed class PetWidgetToggle : MonoBehaviour
{
    Toggle toggle; CompanionHost host;
    public TMP_Text notice;
    public Image frame; public RectTransform handle; public Sprite onSprite,offSprite;
    public Color onColor = new Color32(38,112,216,255);
    void OnEnable() { toggle=GetComponent<Toggle>(); host=CompanionHost.Ensure(); toggle.SetIsOnWithoutNotify(host.IsOpen); toggle.onValueChanged.AddListener(Change); }
    void Change(bool value) { host.SetEnabled(value); if(notice && !string.IsNullOrEmpty(host.Error)) notice.text=host.Error; }
    void Update() {
        if(!host) return;
        toggle.SetIsOnWithoutNotify(host.IsOpen);
        if(frame && onSprite && offSprite) { frame.sprite=host.IsOpen?onSprite:offSprite;frame.color=host.IsOpen?onColor:Color.white; }
        if(handle) { var pos=handle.anchoredPosition;pos.x=host.IsOpen?22:-22;handle.anchoredPosition=pos; }
    }
    void OnDisable() { if(toggle) toggle.onValueChanged.RemoveListener(Change); }
}
