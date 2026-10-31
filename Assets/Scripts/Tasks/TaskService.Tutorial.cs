using System;
using System.Collections.Generic;

public enum ProgressionFeature { Focus, Boss, Leaderboard }

[Serializable]
public sealed class TutorialProgress
{
    public int version;
    public bool dismissed;
    public int taskStep;
    public int focusStep;
    public int taskPageStep;
    public int bossStep;
    public int leaderboardStep;
    public int equipmentStep;
    public string taskId;
    public int announcedLevel = 1;
    public List<string> read = new List<string>();
}

public sealed partial class TaskService
{
    public static int RequiredLevel(ProgressionFeature feature) => feature == ProgressionFeature.Focus ? 2 : 5;
    public bool CanAccess(ProgressionFeature feature) => RewardLevel >= RequiredLevel(feature);
    public string AccessMessage(ProgressionFeature feature) => "Unlocks at Lv." + RequiredLevel(feature);
    public TutorialProgress Tutorial => PlayerDataStore.Copy(store.Data.tutorial);
    // Existing saves opt into the guide through Help; new saves are initialized by the store.
    public bool InitializeTutorial() => store.Data.tutorial != null && store.Data.tutorial.version == 1 || Commit(d => d.tutorial = new TutorialProgress {
        version = 1, taskStep = 4, announcedLevel = RewardLevel,
        read = new List<string> { "tasks", "reward", "daily", "equipment", RewardLevel >= 2 ? "focus" : "", RewardLevel >= 5 ? "boss" : "", RewardLevel >= 5 ? "leaderboard" : "" }
    });
    public bool SaveTutorial(TutorialProgress progress)
    {
        if (progress == null) { LastError = "Tutorial progress is missing."; return false; }
        return Commit(d => { d.tutorial = PlayerDataStore.Copy(progress); d.tutorial.version = 1; });
    }
}
