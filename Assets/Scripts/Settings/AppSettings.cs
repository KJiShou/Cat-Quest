using System;

[Serializable]
public sealed class AppSettings
{
    public bool notificationsEnabled = true;
    public float soundVolume = .594f;
    public float musicVolume;
    public string dashboardRanking = nameof(LeaderboardKind.FocusMinutes);
    // Missing fields in older saves are false, so every existing card stays visible.
    public bool hideDashboardRanking, hideDashboardTasks, hideDashboardDailyRewards, hideDashboardFocus, hideDashboardCharacter;

    // A missing or unknown value in an older save preserves the original preview.
    public LeaderboardKind DashboardRanking => Enum.TryParse(dashboardRanking, out LeaderboardKind kind)
        && Enum.IsDefined(typeof(LeaderboardKind), kind) ? kind : LeaderboardKind.FocusMinutes;

    public static AppSettings Normalize(AppSettings value)
    {
        value ??= new AppSettings();
        value.soundVolume = Volume(value.soundVolume, .594f);
        value.musicVolume = Volume(value.musicVolume, 0);
        value.dashboardRanking = value.DashboardRanking.ToString();
        return value;
    }
    static float Volume(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value)
        ? fallback : Math.Max(0, Math.Min(1, value));
}
