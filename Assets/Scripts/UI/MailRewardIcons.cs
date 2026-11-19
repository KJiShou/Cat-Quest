using TMPro;
using UnityEngine;

public sealed class MailRewardIcons : MonoBehaviour
{
    [SerializeField] TMP_Text fish, experience, tickets;

    internal void Display(RewardAmounts reward)
    {
        SetAmount(fish, reward.fishCoins);
        SetAmount(experience, reward.experience);
        SetAmount(tickets, reward.tickets);
    }

    static void SetAmount(TMP_Text label, int amount)
    {
        label.text = amount.ToString();
        label.transform.parent.gameObject.SetActive(amount > 0);
    }
}
