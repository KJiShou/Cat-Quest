using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
internal sealed class MailLetter
{
    public string id, title, body, publishedUtc;
    public int fishCoins, experience, bossTickets;

    internal string Key => "reward:" + id;
    internal DateTimeOffset PublishedAt => DateTimeOffset.ParseExact(publishedUtc, MailCatalog.UtcFormat,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    internal RewardAmounts Reward => new RewardAmounts(experience, fishCoins, bossTickets);

    internal bool SameReward(MailLetter other) =>
        fishCoins == other.fishCoins && experience == other.experience && bossTickets == other.bossTickets;
}

[Serializable]
internal sealed class MailCatalog
{
    internal const string Feed = "https://raw.githubusercontent.com/KJiShou/Cat-Quest/main/docs/mail/catalog.json";
    internal const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    internal const int MaxBytes = 128 * 1024;
    internal const int MaxLetters = 100;
    internal const int MaxAttachment = 100000;
    public int format;
    public MailLetter[] letters;

    internal static MailCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("The mail list is missing or too large.");
        var catalog = JsonUtility.FromJson<MailCatalog>(json);
        if (catalog == null || catalog.format != 1 || catalog.letters == null || catalog.letters.Length > MaxLetters)
            throw new InvalidDataException("The mail list format is unsupported.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var letter in catalog.letters)
        {
            if (letter == null || !ValidId(letter.id) || !ids.Add(letter.id)
                || string.IsNullOrWhiteSpace(letter.title) || letter.title.Length > 80
                || string.IsNullOrWhiteSpace(letter.body) || letter.body.Length > 3000
                || !DateTimeOffset.TryParseExact(letter.publishedUtc, UtcFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out _)
                || !ValidReward(letter.Reward))
                throw new InvalidDataException("The mail list contains an invalid or duplicate letter.");
        }

        return catalog;
    }

    internal static bool ValidId(string id) => id != null && Regex.IsMatch(id, @"\A[a-z0-9][a-z0-9-]{0,63}\z");

    internal static bool ValidReward(RewardAmounts reward) =>
        reward.fishCoins >= 0 && reward.fishCoins <= MaxAttachment
        && reward.experience >= 0 && reward.experience <= MaxAttachment
        && reward.tickets >= 0 && reward.tickets <= MaxAttachment
        && (reward.fishCoins > 0 || reward.experience > 0 || reward.tickets > 0);
}
