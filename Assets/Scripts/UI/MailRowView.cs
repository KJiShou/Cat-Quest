using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MailRowView : MonoBehaviour
{
    [SerializeField] Button button, primary, delete;
    [SerializeField] TMP_Text title, label, primaryLabel;
    [SerializeField] MailRewardIcons rewards;
    [SerializeField] Image letterIcon;
    [SerializeField] Sprite mailIcon, updateIcon;
    [SerializeField] Image surface;

    void Awake()
    {
        primaryLabel.textWrappingMode = TextWrappingModes.NoWrap;
        primaryLabel.enableAutoSizing = true;
        primaryLabel.fontSizeMax = primaryLabel.fontSize;
        primaryLabel.fontSizeMin = 30;
    }

    internal Button Control => button;

    internal Button Primary => primary;

    internal Button Delete => delete;

    internal void Display(string heading, string preview, RewardAmounts attachments, string action, bool unread, bool available, bool update)
    {
        title.text = heading;
        label.text = preview;
        rewards.Display(attachments);
        letterIcon.sprite = update ? updateIcon : mailIcon;
        primaryLabel.text = action;
        primary.interactable = available;
        surface.color = unread ? new Color32(196, 240, 250, 255) : Color.white;
    }
}
