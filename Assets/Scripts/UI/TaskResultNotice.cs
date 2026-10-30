using TMPro;
using UnityEngine;

// A serialized SimpleCasual message frame. Never takes focus or blocks input.
public sealed class TaskResultNotice : MonoBehaviour
{
    public TMP_Text message;
    public CanvasGroup fade;
    float remaining;
    public void Show(string text)
    {
        message.text = text; remaining = 4;
        fade.alpha = 1; gameObject.SetActive(true); transform.SetAsLastSibling();
    }
    public void Dismiss() { gameObject.SetActive(false); }
    void Update()
    {
        remaining -= Time.unscaledDeltaTime;
        fade.alpha = Mathf.Clamp01(remaining / .2f);
        if (remaining <= 0) Dismiss();
    }
    void OnApplicationFocus(bool focused) { if (!focused) Dismiss(); }
    void OnDisable() { remaining = 0; fade.alpha = 0; }
}
