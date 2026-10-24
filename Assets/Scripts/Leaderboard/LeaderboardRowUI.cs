using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LeaderboardRowUI : MonoBehaviour
{
    public TMP_Text rank, playerName, level, score;
    public Image background, medal;
    public Sprite[] rankFrames, medals;
    public Sprite ownFrame;
    public Image metricIcon;
    public Sprite[] metricIcons;
    public Vector2 regularRankPosition = new Vector2(56, 0);
    public Vector2 medalRankPosition = new Vector2(56, -5.2f);
    TMP_Text[] labels;
    Color[] normalColors;
    FontStyles[] normalStyles;
    public void Show(AccountAggregate row, long position, LeaderboardKind kind, bool self)
    {
        if (labels == null)
        {
            labels = new[] { rank, playerName, level, score };
            normalColors = new Color[labels.Length];
            normalStyles = new FontStyles[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                normalColors[i] = labels[i].color;
                normalStyles[i] = labels[i].fontStyle;
            }
        }
        // Rows are reused across categories and account changes.
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i].color = self ? Color.black : normalColors[i];
            labels[i].fontStyle = self ? normalStyles[i] | FontStyles.Bold : normalStyles[i];
        }
        rank.text = position.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        playerName.text = row.playerName + (self ? "  (You)" : "");
        level.text = "Cat Level " + row.catLevel;
        score.text = LeaderboardQuery.Display(row, kind);
        bool podium = position >= 1 && position <= 3;
        rank.rectTransform.anchoredPosition = podium ? medalRankPosition : regularRankPosition;
        metricIcon.sprite = metricIcons[(int)kind];
        metricIcon.color = kind == LeaderboardKind.Power ? score.color : Color.white;
        background.sprite = self ? ownFrame : rankFrames[podium ? (int)position - 1 : 3];
        medal.enabled = podium;
        if (podium) medal.sprite = medals[(int)position - 1];
        rank.color = self ? Color.black : podium ? new Color(.32f, .38f, .51f) : new Color(.06f, .67f, .91f);
        gameObject.SetActive(true);
    }
}
