using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// A serialized SimpleCasual message frame. Only the notice itself receives clicks.
public sealed class TaskResultNotice : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text message;
    public CanvasGroup fade;
    float elapsed;
    Vector3 restingScale;
    void Awake() { restingScale = transform.localScale; }
    public void Show(string text)
    {
        gameObject.SetActive(true);
        message.text = text; elapsed = 0;
        transform.localScale = restingScale * .12f;
        fade.alpha = 0; transform.SetAsLastSibling();
    }
    public void Dismiss() { gameObject.SetActive(false); }
    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Dismiss(); }
    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / .28f);
        float ease = 1 - Mathf.Pow(1 - t, 3);
        transform.localScale = restingScale * Mathf.Lerp(.12f, 1, ease);
        fade.alpha = Mathf.Clamp01(t * 3);
    }
    void OnApplicationFocus(bool focused) { if (!focused) Dismiss(); }
    void OnDisable() { elapsed = 0; fade.alpha = 0; transform.localScale = restingScale; }
}
