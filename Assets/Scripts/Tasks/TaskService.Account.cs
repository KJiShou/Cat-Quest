using System;

public sealed partial class TaskService
{
    public AccountPreferences AccountSettings => PlayerDataStore.Copy(AccountPreferences.Normalize(store.Data.account));

    public bool SaveAccountSettings(AccountPreferences draft)
    {
        if (draft == null) { LastError = "Account settings are missing."; return false; }
        var value = AccountPreferences.Normalize(PlayerDataStore.Copy(draft));
        return Commit(data =>
        {
            var previous = AccountPreferences.Normalize(data.account);
            if (!string.IsNullOrEmpty(previous.boundUid) && previous.boundUid != value.boundUid)
                throw new ArgumentException("This local save is linked to another Google account.");
            value.playerName = (value.playerName ?? "").Trim();
            if (!string.IsNullOrEmpty(value.playerName) && !AccountAggregate.ValidName(value.playerName))
                throw new ArgumentException("Enter a display name of 1–20 characters without markup or control characters.");
            if (value.uploadEnabled && !AccountAggregate.ValidName(value.playerName))
                throw new ArgumentException("Choose a display name before sharing statistics.");
            data.account = value;
        });
    }
}
