using TMPro;
using UnityEngine;
using UnityEngine.UI;
public sealed class AiChatRow : MonoBehaviour
{
    public TMP_Text text, speaker, stoppedLabel;
    public RectTransform bubble;
    public GameObject avatar, userAvatar;
    public Sprite assistantSprite, userSprite;
    bool user, streaming, cursorVisible;
    float priorWidth, cursorStart;

    void OnEnable() => text.OnPreRenderText += ApplyCursor;
    void OnDisable() => text.OnPreRenderText -= ApplyCursor;

    public void ShowPreview(string value)
    {
        if (!streaming) {
            cursorStart = Time.unscaledTime;
            cursorVisible = true;
            Bind(new AiChatMessage { role = "assistant", text = value });
            streaming = true;
        }
        text.text = value + "|";
        Resize();
    }
    public void Bind(AiChatMessage message)
    {
        streaming = false;
        user = message.role == "user"; text.richText = false; text.text = message.text;
        if (stoppedLabel) stoppedLabel.gameObject.SetActive(message.stopped);
        speaker.text = user ? "You" : "Sensei";
        avatar.SetActive(!user); userAvatar.SetActive(user);
        bubble.GetComponent<Image>().sprite = user ? userSprite : assistantSprite;
        bubble.GetComponent<Image>().color = user ? new Color32(219,231,250,255) : Color.white;
        Resize();
    }
    void LateUpdate()
    {
        if (Mathf.Abs(((RectTransform)transform).rect.width - priorWidth) > 1) Resize();
        if (!streaming) return;
        bool visible = Mathf.FloorToInt((Time.unscaledTime - cursorStart) * 2f) % 2 == 0;
        if (cursorVisible == visible) return;
        cursorVisible = visible;
        ApplyCursor(text.textInfo);
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    void ApplyCursor(TMP_TextInfo info)
    {
        if (!streaming || info.characterCount == 0) return;
        var glyph = info.characterInfo[info.characterCount - 1];
        if (!glyph.isVisible || glyph.index != text.text.Length - 1) return;
        var colors = info.meshInfo[glyph.materialReferenceIndex].colors32;
        for (int i = glyph.vertexIndex; i < glyph.vertexIndex + 4; i++)
            colors[i].a = cursorVisible ? (byte)Mathf.RoundToInt(text.color.a * 255) : (byte)0;
    }
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
        if (stoppedLabel && stoppedLabel.gameObject.activeSelf) {
            stoppedLabel.rectTransform.anchoredPosition = new Vector2(76, -height - 2);
            height += stoppedLabel.rectTransform.rect.height + 2;
        }
        GetComponent<LayoutElement>().preferredHeight = Mathf.Max(height,64);
    }
}
