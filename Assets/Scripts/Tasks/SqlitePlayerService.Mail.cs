using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CatQuest.Updates;

public sealed partial class SqlitePlayerService
{
    const string InstalledVersionPrefix = "mail-version:";
    const string BonusEligibilityPrefix = "mail-bonus:";
    internal const string BonusReceiptPrefix = "version-bonus:";
    internal static readonly RewardAmounts VersionGift = new RewardAmounts(888, 888, 8);

    internal Task<OperationResult<bool>> ObserveMailVersionAsync(string version)
    {
        var installed = UpdateRules.VersionOf(version);
        string id = installed.ToString();
        return ExecuteCommandAsync(tx => {
            if (tx.ReadMailState(InstalledVersionPrefix + id) != 0) return true;
            var observed = tx.ReadMailKeys(InstalledVersionPrefix)
                .Select(prior => Version.TryParse(prior, out var value) ? value : null)
                .Where(value => value != null).ToArray();
            if (observed.Length > 0 && observed.All(prior => prior < installed))
                tx.SetMailState(BonusEligibilityPrefix + id, 1);
            tx.SetMailState(InstalledVersionPrefix + id, 1);
            return true;
        });
    }

    internal Task<OperationResult<Version[]>> ReadVersionGiftsAsync() =>
        repository.ReadAsync(tx => tx.ReadMailKeys(BonusEligibilityPrefix)
            .Select(id => Version.TryParse(id, out var value) ? value : null)
            .Where(value => value != null).OrderByDescending(value => value).ToArray());

    internal Task<OperationResult<bool>> ClaimVersionGiftAsync(string version)
    {
        string id = UpdateRules.VersionOf(version).ToString();
        return ExecuteCommandAsync(tx => {
            if (tx.ReadMailState(BonusEligibilityPrefix + id) == 0)
                throw new PlayerCommandException("Install this update before claiming its gift.");
            return GrantMail(tx, BonusReceiptPrefix + id, VersionGift);
        });
    }

    internal Task<OperationResult<Dictionary<string, int>>> ReadMailStatesAsync(IEnumerable<string> keys)
    {
        var ids = keys.Distinct(StringComparer.Ordinal).ToArray();
        return repository.ReadAsync(tx => ids.ToDictionary(id => id, tx.ReadMailState, StringComparer.Ordinal));
    }

    internal Task<OperationResult<bool>> ReadLetterAsync(string key) =>
        ExecuteCommandAsync(tx => {
            if (tx.ReadMailState(key) == 0) tx.SetMailState(key, 1);
            return true;
        });

    // Only MailInbox calls this command, after obtaining and validating a fresh catalogue.
    // The transaction also guards duplicate delivery after reload or an uncertain commit.
    internal Task<OperationResult<bool>> ClaimLetterAsync(MailLetter letter)
    {
        string id = letter.id;
        var reward = letter.Reward;
        return ExecuteCommandAsync(tx => {
            if (!MailCatalog.ValidId(id) || !MailCatalog.ValidReward(reward))
                throw new PlayerCommandException("This letter's reward is invalid.");
            return GrantMail(tx, "reward:" + id, reward);
        });
    }

    static bool GrantMail(IPlayerTransaction tx, string key, RewardAmounts reward)
    {
        if (tx.ReadMailState(key) == 2) return false;
        var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
        SqlAddRewards(profile, reward);
        tx.Put(PlayerRecordKind.Profile, profile);
        tx.SetMailState(key, 2);
        return true;
    }
}
