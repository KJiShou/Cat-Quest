using UnityEngine;
using UnityEngine.SceneManagement;

// The application's sound manager. Configure clips on Resources/SettingsAudio.
// Presentation only: no audio callback changes gameplay or saved progress.
public sealed class SettingsAudio : MonoBehaviour
{
    [Header("Existing 2D audio sources")]
    public AudioSource soundSource, musicSource;
    [Header("BGM (looping; empty slots are silent)")]
    [Tooltip("Default / menu background music.")]
    public AudioClip musicClip;
    public AudioClip focusMusic, restMusic, battleMusic;
    [Header("UI and progression")]
    public AudioClip previewClip;
    public AudioClip buttonClick, taskComplete, levelUp;
    public AudioClip equipmentClaim;
    [Header("Boss battle (impact sounds)")]
    public AudioClip catAttack, bossAttack, battleVictory, battleDefeat;
    [Header("Timer notifications")]
    public AudioClip focusComplete, restComplete;
    AudioListener fallbackListener;
    TaskService service;
    FocusPhase focusPhase;
    bool battling;
    AudioSource dialogueSource;
    Object dialogueOwner;
    AudioClip dialogueClip;
    public static SettingsAudio Instance { get; private set; }
    public static SettingsAudio EnsureInstance()
    {
        if (Instance) return Instance;
        if (CompanionProtocol.HasArgument("--companion")) return null;
        var manager = GameDataManager.EnsureInstance();
        var existing = manager.GetComponentInChildren<SettingsAudio>(true);
        if (existing) return existing;
        var prefab = Resources.Load<SettingsAudio>("SettingsAudio");
        return prefab ? Instantiate(prefab, manager.transform) : null;
    }
    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    void OnEnable()
    {
        if (Instance != this) return;
        service = GameDataManager.EnsureInstance().Tasks;
        service.Changed += ApplySaved;
        service.LevelUp += OnLevelUp;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshListener();
        ApplySaved();
        RefreshMusic();
    }
    void OnDisable()
    {
        if (service != null) { service.Changed -= ApplySaved; service.LevelUp -= OnLevelUp; }
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (fallbackListener) fallbackListener.enabled = false;
        if (musicSource) musicSource.Stop();
        if (soundSource) soundSource.Stop();
        StopDialogue();
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshListener();
    void RefreshListener()
    {
        // Product scenes have no listener. Respect a scene's own active listener.
        if (!fallbackListener) fallbackListener = gameObject.AddComponent<AudioListener>();
        bool hasSceneListener = false;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener != fallbackListener && listener.isActiveAndEnabled) { hasSceneListener = true; break; }
        fallbackListener.enabled = !hasSceneListener;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
    public void ApplySaved() { if (service != null) Preview(service.Settings); }
    public void Preview(AppSettings settings)
    {
        if (settings == null) return;
        if (soundSource) soundSource.volume = Mathf.Clamp01(settings.soundVolume);
        if (musicSource) musicSource.volume = Mathf.Clamp01(settings.musicVolume);
        if (dialogueSource) dialogueSource.volume = Mathf.Clamp01(settings.soundVolume);
    }
    public float PlayDialogue(Object owner, string text, bool player)
    {
        StopDialogue();
        if (!isActiveAndEnabled || !owner) return 0;
        var line = DialogueAudioLibrary.Find(text, player);
        if (line == null) return 0; // New/changed text remains usable without a clip.
        dialogueClip = Resources.Load<AudioClip>(line.clip);
        if (!dialogueClip) return 0;
        if (!dialogueSource)
        {
            dialogueSource = gameObject.AddComponent<AudioSource>();
            dialogueSource.playOnAwake = false;
            dialogueSource.loop = false;
            dialogueSource.spatialBlend = 0;
        }
        dialogueOwner = owner;
        dialogueSource.volume = soundSource ? soundSource.volume : 0;
        dialogueSource.clip = dialogueClip;
        dialogueSource.Play();
        return dialogueClip.length;
    }
    public void StopDialogue(Object owner = null)
    {
        // A destroyed Unity owner is not an unscoped stop request.
        if (!ReferenceEquals(owner, null) && !ReferenceEquals(dialogueOwner, owner)) return;
        if (dialogueSource) { dialogueSource.Stop(); dialogueSource.clip = null; }
        dialogueOwner = null;
        if (dialogueClip) { Resources.UnloadAsset(dialogueClip); dialogueClip = null; }
    }
    void OnApplicationFocus(bool focused) { if (!focused) StopDialogue(); }
    public void PlaySound(AudioClip clip)
    {
        if (isActiveAndEnabled && soundSource && soundSource.isActiveAndEnabled && clip)
            soundSource.PlayOneShot(clip);
    }
    public void PlayPreview() => PlaySound(previewClip ? previewClip : buttonClick);
    public void PlayButtonClick() => PlaySound(buttonClick);
    public void PlayTaskComplete() => PlaySound(taskComplete);
    public void PlayEquipmentClaim() => PlaySound(equipmentClaim);
    public void PlayAttack(bool catAttacks) => PlaySound(catAttacks ? catAttack : bossAttack);
    public void PlayBattleResult(BattleOutcome outcome)
    {
        if (outcome == BattleOutcome.Victory) PlaySound(battleVictory);
        else if (outcome == BattleOutcome.Defeat) PlaySound(battleDefeat);
    }
    public void PlayFocusComplete() => PlaySound(focusComplete);
    public void PlayRestComplete() => PlaySound(restComplete);
    void OnLevelUp(TaskService.LevelUpReward reward) => PlaySound(levelUp);
    public void SetBattleMusic(bool active) { battling = active; RefreshMusic(); }
    public void SetFocusPhase(FocusPhase phase) { focusPhase = phase; RefreshMusic(); }
    void RefreshMusic()
    {
        if (!isActiveAndEnabled || !musicSource) return;
        AudioClip clip = battling ? battleMusic
            : focusPhase == FocusPhase.Focusing || focusPhase == FocusPhase.Saving ? focusMusic
            : focusPhase == FocusPhase.Resting ? restMusic : musicClip;
        if (musicSource.clip == clip && (!clip || musicSource.isPlaying)) return;
        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.loop = true;
        if (clip) musicSource.Play();
    }
}
