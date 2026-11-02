using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public sealed class PetWidgetToggle : MonoBehaviour
{
    Toggle toggle; CompanionHost host;
    CanvasGroup visibility;
    bool displayedAccess, displayedOpen;
    public TMP_Text notice;
    public Image frame; public RectTransform handle; public Sprite onSprite,offSprite;
    public Color onColor = new Color32(38,112,216,255);
    void OnEnable() {
        toggle=GetComponent<Toggle>(); host=CompanionHost.Ensure();
        // Hide the complete authored header item, keeping its layout and this updater alive.
        visibility=transform.parent.GetComponent<CanvasGroup>();
        if(!visibility) visibility=transform.parent.gameObject.AddComponent<CanvasGroup>();
        RefreshAccess(host.IsUnlocked);
        RefreshState(host.IsOpen); toggle.onValueChanged.AddListener(Change);
    }
    void RefreshAccess(bool unlocked) {
        displayedAccess=unlocked;
        visibility.alpha=unlocked?1:0;
        visibility.interactable=unlocked;
        visibility.blocksRaycasts=unlocked;
        toggle.interactable=unlocked;
    }
    void Change(bool value) { host.SetEnabled(value); if(notice && !string.IsNullOrEmpty(host.Error)) notice.text=host.Error; }
    void Update() {
        if(!host) return;
        bool unlocked=host.IsUnlocked, open=host.IsOpen;
        if (unlocked!=displayedAccess) RefreshAccess(unlocked);
        if (open!=displayedOpen || toggle.isOn!=open) RefreshState(open);
    }
    void RefreshState(bool open) {
        displayedOpen=open;
        toggle.SetIsOnWithoutNotify(open);
        if(frame && onSprite && offSprite) { frame.sprite=open?onSprite:offSprite;frame.color=open?onColor:Color.white; }
        if(handle) { var pos=handle.anchoredPosition;pos.x=open?22:-22;handle.anchoredPosition=pos; }
    }
    void OnDisable() { if(toggle) toggle.onValueChanged.RemoveListener(Change); }
}
