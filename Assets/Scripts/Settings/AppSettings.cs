using System;

[Serializable]
public sealed class AppSettings
{
    public bool notificationsEnabled = true;
    public float soundVolume = .594f;
    public float musicVolume;

    public static AppSettings Normalize(AppSettings value)
    {
        value ??= new AppSettings();
        value.soundVolume = Volume(value.soundVolume, .594f);
        value.musicVolume = Volume(value.musicVolume, 0);
        return value;
    }
    static float Volume(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value)
        ? fallback : Math.Max(0, Math.Min(1, value));
}
