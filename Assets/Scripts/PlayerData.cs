using System;
using System.Collections.Generic;

[Serializable]
public class PlayerData
{
    public string username;
    public int version = 5;
    public bool equipmentUnlocked;
    public List<EquipmentItem> equipment = new List<EquipmentItem>();
    public List<EquipmentUpgradeReceipt> equipmentUpgrades = new List<EquipmentUpgradeReceipt>();
    public string equippedWeaponId, equippedBodyOutfitId, equippedHeadAccessoryId;
    public int masterStage = 1;
    public List<BossProgress> bossProgress = new List<BossProgress>();
    public List<BattleAttempt> battles = new List<BattleAttempt>();
    public int fishCoins;
    public int tickets;
    public int experience;
    public bool tagsInitialized;
    public List<QuestTask> tasks = new List<QuestTask>();
    public List<QuestTag> tags = new List<QuestTag>();
    public List<TaskOccurrence> occurrences = new List<TaskOccurrence>();
    public List<TaskCompletion> history = new List<TaskCompletion>();
    public List<RewardDay> rewardDays = new List<RewardDay>();
    public List<FocusSession> focusSessions = new List<FocusSession>();
}

public enum TaskDifficulty { Easy, Normal, Hard }
public enum TaskTab { Today, Daily, Todo, Tags, History }
[Serializable] public class QuestTag { public string id; public string name; }
[Serializable] public class QuestSubtask { public string id; public string title; }
[Serializable] public class QuestTask
{
    public string id;
    public string title;
    public string description;
    public TaskDifficulty difficulty = TaskDifficulty.Normal;
    public bool daily;
    public bool scheduled;
    public string date; // Local yyyy-MM-dd; start date for daily tasks.
    public int minuteOfDay;
    public List<string> tagIds = new List<string>();
    public List<QuestSubtask> subtasks = new List<QuestSubtask>();
}
[Serializable] public class TaskOccurrence
{
    public string taskId;
    public string day; // Local date for daily tasks; "once" for ordinary tasks.
    public bool completed;
    public bool rewardGranted;
    public List<string> completedSubtaskIds = new List<string>();
}
[Serializable] public class TaskCompletion
{
    public string id;
    public string taskId;
    public string day;
    public string completedUtc;
    public string completedLocalDay;
    public string rewardedLocalDay;
    public QuestTask snapshot;
    public List<string> tagNames = new List<string>();
    public int fishCoins;
    public int experience;
    public bool revoked;
}

[Serializable] public class RewardDay
{
    public string day;
    public int taskExperience;
    public int taskFishCoins;
    public int focusMinutes;
    public int focusTickets;
    public bool questsClaimed;
    public bool focusClaimed;
}

public enum DailyRewardGoal { ThreeQuests, Focus25Minutes }
public enum FocusSessionStatus { Running, Completed, EndedEarly, Cancelled, Invalid }
[Serializable] public class FocusSession
{
    public int focusDurationSeconds = 1500;
    public int restDurationSeconds = 300;
    public string questId, questTitle, occurrenceDay;
    public string id;
    public FocusSessionStatus status;
    public string startedUtc;
    public string completedUtc;
    public string rewardDay;
    public int eligibleMinutes;
    public double eligibleSeconds;
    public int rewardedMinutes;
    public int rawTickets;
    public int experience;
    public int fishCoins;
    public int tickets;
}

[Serializable] public sealed class FocusSessionOptions
{
    public int focusMinutes = 25, restMinutes = 5;
    public string questId, questTitle, occurrenceDay;
    public bool IsValid => focusMinutes >= 1 && focusMinutes <= 60 && restMinutes >= 1 && restMinutes <= 60;
    public FocusSessionOptions Copy() => (FocusSessionOptions)MemberwiseClone();
}

public readonly struct RewardAmounts
{
    public readonly int experience, fishCoins, tickets;
    public RewardAmounts(int experience, int fishCoins, int tickets = 0)
    { this.experience = experience; this.fishCoins = fishCoins; this.tickets = tickets; }
}
