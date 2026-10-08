using System;
using System.Collections.Generic;

[Serializable]
public class PlayerData
{
    public string username;
    public int version = 2;
    public int fishCoins;
    public int tickets;
    public int experience;
    public bool tagsInitialized;
    public List<QuestTask> tasks = new List<QuestTask>();
    public List<QuestTag> tags = new List<QuestTag>();
    public List<TaskOccurrence> occurrences = new List<TaskOccurrence>();
    public List<TaskCompletion> history = new List<TaskCompletion>();
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
    public QuestTask snapshot;
    public List<string> tagNames = new List<string>();
    public int fishCoins;
    public int experience;
    public bool revoked;
}
