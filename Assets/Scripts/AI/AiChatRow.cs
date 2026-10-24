using TMPro;
using UnityEngine;
using UnityEngine.UI;
public sealed class AiChatRow : MonoBehaviour
{
    public TMP_Text text, speaker;
    public RectTransform bubble;
    public GameObject avatar, userAvatar;
    public Sprite assistantSprite, userSprite;
    bool user; float priorWidth;
    public void Bind(AiChatMessage message)
    {
        user = message.role == "user"; text.richText = false; text.text = message.text;
        speaker.text = user ? "You" : "Sensei";
        avatar.SetActive(!user); userAvatar.SetActive(user);
        bubble.GetComponent<Image>().sprite = user ? userSprite : assistantSprite;
        bubble.GetComponent<Image>().color = user ? new Color32(219,231,250,255) : Color.white;
        Resize();
    }
    void LateUpdate() { if (Mathf.Abs(((RectTransform)transform).rect.width - priorWidth) > 1) Resize(); }
    void Resize()
    {
        priorWidth = ((RectTransform)transform).rect.width;
        // Keep wrapping and bubble height aligned with the authored text insets.
        float horizontalPadding = -text.rectTransform.sizeDelta.x;
        float verticalPadding = -text.rectTransform.sizeDelta.y;
        float width = Mathf.Min(Mathf.Max(180, text.GetPreferredValues(text.text).x + horizontalPadding), Mathf.Max(120, Mathf.Min(priorWidth * .84f, priorWidth - 76)));
        float height = text.GetPreferredValues(text.text, Mathf.Max(1, width - horizontalPadding), 0).y + verticalPadding;
        bubble.anchorMin = bubble.anchorMax = bubble.pivot = new Vector2(user ? 1 : 0, 1);
        bubble.anchoredPosition = new Vector2(user ? -76 : 76, 0); bubble.sizeDelta = new Vector2(width, height);
        GetComponent<LayoutElement>().preferredHeight = Mathf.Max(height,64);
    }
}
