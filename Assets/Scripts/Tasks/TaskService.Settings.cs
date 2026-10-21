using System;

public sealed partial class TaskService
{
    public AppSettings Settings => PlayerDataStore.Copy(store.Data.settings);
    public bool SaveSettings(AppSettings settings)
    {
        if (settings == null || float.IsNaN(settings.soundVolume) || float.IsInfinity(settings.soundVolume)
            || float.IsNaN(settings.musicVolume) || float.IsInfinity(settings.musicVolume))
        { LastError = "Choose valid sound and music volumes."; return false; }
        var copy = AppSettings.Normalize(PlayerDataStore.Copy(settings));
        return Commit(data => data.settings = copy);
    }
}
