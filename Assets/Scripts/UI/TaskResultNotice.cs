using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// A serialized SimpleCasual message frame. Only the notice itself receives clicks.
public sealed class TaskResultNotice : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text message;
    public CanvasGroup fade;
    public NotificationReveal reveal;
    float elapsed;
    public void Show(string text)
    {
        gameObject.SetActive(true);
        message.text = text; elapsed = 0;
        if(reveal)reveal.Play();
        fade.alpha = UiMotion.Reduced?1:0; transform.SetAsLastSibling();
    }
    public void Dismiss() { gameObject.SetActive(false); }
    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Dismiss(); }
    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= 4f) { Dismiss(); return; }
        float t = Mathf.Clamp01(elapsed / .28f);
        fade.alpha = Mathf.Clamp01(t * 3) * Mathf.Clamp01((4f - elapsed) / .3f);
    }
    void OnApplicationFocus(bool focused) { if (!focused) Dismiss(); }
    void OnDisable() { elapsed = 0; fade.alpha = 0; }
}
