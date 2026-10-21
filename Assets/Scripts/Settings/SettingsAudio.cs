using UnityEngine;

// Presentation only. Clips and independent sources are editable in the Resources prefab.
public sealed class SettingsAudio : MonoBehaviour
{
    public AudioSource soundSource, musicSource;
    public AudioClip musicClip, previewClip;
    TaskService service;
    public static SettingsAudio EnsureInstance()
    {
        var manager = GameDataManager.EnsureInstance();
        var existing = manager.GetComponentInChildren<SettingsAudio>(true);
        if (existing) return existing;
        if (CompanionProtocol.HasArgument("--companion")) return null;
        var prefab = Resources.Load<SettingsAudio>("SettingsAudio");
        return prefab ? Instantiate(prefab, manager.transform) : null;
    }
    void OnEnable()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += ApplySaved;
        ApplySaved();
        if (musicSource && musicClip) { musicSource.clip = musicClip; musicSource.loop = true; musicSource.Play(); }
    }
    void OnDisable() { if (service != null) service.Changed -= ApplySaved; }
    public void ApplySaved() { if (service != null) Preview(service.Settings); }
    public void Preview(AppSettings settings)
    {
        if (soundSource) soundSource.volume = settings.soundVolume;
        if (musicSource) musicSource.volume = settings.musicVolume;
    }
    public void PlaySound(AudioClip clip) { if (soundSource && clip) soundSource.PlayOneShot(clip); }
    public void PlayPreview() => PlaySound(previewClip);
}
