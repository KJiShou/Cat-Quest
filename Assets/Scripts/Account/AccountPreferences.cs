using System;

// Local metadata only. Authentication tokens never enter the player save.
[Serializable]
public sealed class AccountPreferences
{
    public string boundUid;
    public string playerName;
    public bool uploadEnabled;
    public bool pendingRemoval;
    public string lastServerUpdateTime;
    public string pendingSnapshotJson;
    public bool pendingWriteMayCreate;

    public static AccountPreferences Normalize(AccountPreferences value)
    {
        value ??= new AccountPreferences();
        value.boundUid ??= "";
        value.playerName ??= "";
        value.lastServerUpdateTime ??= "";
        value.pendingSnapshotJson ??= "";
        if (string.IsNullOrEmpty(value.boundUid))
        {
            value.uploadEnabled = false;
            value.pendingRemoval = false;
            value.lastServerUpdateTime = "";
            value.pendingSnapshotJson = "";
            value.pendingWriteMayCreate = false;
        }
        if (value.pendingRemoval) value.uploadEnabled = false;
        return value;
    }
}

public sealed class AccountViewState
{
    public bool configured, busy, signedIn, uploadEnabled, pendingRemoval, conflict, wrongAccount;
    public string playerName, accountLabel, status, comparison;
}
