using System;

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#endif

public static class DemoProfile
{
    public static bool IsActive
    {
        get
        {
#if UNITY_EDITOR
            return IsDemoSave(Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE"));
#else
            return false;
#endif
        }
    }

#if UNITY_EDITOR
    static readonly string Root = Path.GetFullPath("Library/ManualTestAccount/Demo");
    const string FileName = "demo.json";
    internal const string LevelUpTask = "demo-level-up";
    internal const string OwnRankId = "demo-local";

    public static string SavePath(string run)
    {
        if (run != "default" && !Guid.TryParseExact(run, "N", out _))
            throw new ArgumentException("Invalid demo run.");
        return Path.Combine(Root, run, FileName);
    }

    internal static bool IsDemoSave(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        var runDirectory = Path.GetDirectoryName(path);
        if (!string.Equals(Path.GetDirectoryName(runDirectory), Root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase)) return false;
        string run = Path.GetFileName(runDirectory);
        return run == "default" || Guid.TryParseExact(run, "N", out _);
    }

    public static PlayerData CreateSave(DateTime now)
    {
        const int level = 24;
        int nextLevelExperience = 0;
        for (int i = 0; i < level; i++) nextLevelExperience += 100 + 25 * i;
        var data = new PlayerData {
            username = "Cat Quest Demo", experience = nextLevelExperience - 10,
            fishCoins = 2500, tickets = 12, equipmentUnlocked = true,
            tutorial = new TutorialProgress { version = 1, dismissed = true, announcedLevel = level }
        };
        data.bossProgress.Add(new BossProgress { bossId = 1, wins = 5 });
        data.bossProgress.Add(new BossProgress { bossId = 2, wins = 1 });
        data.bossProgress.Add(new BossProgress { bossId = 3, wins = 15 });
        foreach (var item in EquipmentRules.Catalog)
        {
            if (item.Tier != 3) continue;
            data.equipment.Add(new EquipmentItem { id = item.Id, level = item.Slot == EquipmentSlot.Weapon ? level - 1 : level });
            EquipmentRules.SetEquipped(data, item.Slot, item.Id);
        }
        data.equipment.Add(new EquipmentItem { id = LootRules.Catalog[0].Id, level = 5 });

        string today = TaskService.DateKey(now);
        const string tag = "demo-project";
        data.tagsInitialized = true;
        data.tags.Add(new QuestTag { id = tag, name = "Project", colorHex = "#5C97D6" });
        data.tasks.Add(new QuestTask { id = LevelUpTask, title = "Review the presentation slides", scheduled = true, date = today, tagIds = { tag } });
        data.tasks.Add(new QuestTask { id = "demo-daily", title = "Review today's priorities", daily = true, date = today, repeatInterval = 1, difficulty = TaskDifficulty.Easy });
        data.tasks.Add(new QuestTask {
            id = "demo-todo", title = "Prepare the project demo", tagIds = { tag },
            subtasks = { new QuestSubtask { id = "demo-rehearse", title = "Rehearse the walkthrough" } }
        });
        data.tasks.Add(new QuestTask { id = "demo-calendar", title = "Submit the project report", scheduled = true, date = TaskService.DateKey(now.AddDays(1)), tagIds = { tag } });
        for (int i = 0; i < 7; i++)
        {
            var day = now.AddDays(-i);
            AddCompletion(data, "demo-notes-" + i, "Organize project notes", day, tag);
            if (i == 0) AddCompletion(data, "demo-outline", "Draft the presentation outline", day, tag);
            data.focusSessions.Add(new FocusSession {
                id = "demo-focus-" + i, mode = FocusTimerMode.Pomodoro, status = FocusSessionStatus.Completed,
                startedUtc = day.AddMinutes(-25).ToUniversalTime().ToString("o"),
                completedUtc = day.ToUniversalTime().ToString("o"), rewardDay = TaskService.DateKey(day),
                elapsedSeconds = 1500, eligibleSeconds = 1500, eligibleMinutes = 25,
                questTitle = "Project research", tagIds = { tag }, tagNames = { "Project" }, rewardLevel = level
            });
        }
        return data;
    }

    static void AddCompletion(PlayerData data, string id, string title, DateTime day, string tag)
    {
        string date = TaskService.DateKey(day);
        var task = new QuestTask { id = id, title = title, scheduled = true, date = date, tagIds = { tag } };
        data.history.Add(new TaskCompletion {
            id = id + "-receipt", taskId = id, day = "once", snapshot = task,
            completedUtc = day.ToUniversalTime().ToString("o"), completedLocalDay = date,
            rewardedLocalDay = date, tagNames = { "Project" }, tagColors = { "#5C97D6" }
        });
    }

    internal static Task<string> MailFeed()
    {
        const string published = "2026-01-01T00:00:00Z";
        var catalog = new MailCatalog { format = 1, letters = new[] {
            new MailLetter {
                id = "demo-welcome", title = "Welcome to the Cat Quest demo",
                body = "Supplies for your next upgrade.",
                publishedUtc = published,
                fishCoins = 300, bossTickets = 3
            },
            new MailLetter {
                id = "demo-focus", title = "A gift for your next Focus session",
                body = "Try Focus and check your stats.",
                publishedUtc = "2025-12-31T00:00:00Z",
                fishCoins = 50, bossTickets = 1
            }
        } };
        return Task.FromResult(JsonUtility.ToJson(catalog));
    }

    internal static LeaderboardSnapshot Ranking(LeaderboardKind kind, AccountAggregate own)
    {
        var snapshot = new LeaderboardSnapshot { kind = kind, own = own, success = true };
        snapshot.leaders.AddRange(new[] {
            Peer("demo-mochi", "Demo Mochi", 35, 9, 21, 260, 2110),
            Peer("demo-sesame", "Demo Sesame", 28, 6, 5, 380, 1530),
            Peer("demo-nori", "Demo Nori", 22, 3, 12, 135, 1050),
            Peer("demo-pixel", "Demo Pixel", 14, 2, 2, 80, 590),
            own
        });
        snapshot.leaders.Sort((a, b) => LeaderboardQuery.Compare(a, b, kind));
        snapshot.ownRank = snapshot.leaders.IndexOf(own) + 1;
        return snapshot;
    }

    static AccountAggregate Peer(string id, string name, int level, int stage, long tasks, long focus, int power) =>
        new AccountAggregate {
            playerId = id, playerName = name, catLevel = level, bossStage = stage,
            totalTasksCompleted = tasks, totalFocusMinutes = focus, power = power,
            weaponId = "boss1-weapon", weaponLevel = 6, bodyId = "boss1-body", bodyLevel = 5,
            headId = "", headLevel = 0
        };

    internal sealed class ChatStorage : IChatGptStorage
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string> {
            ["connection.dpapi"] = "{\"client_id\":\"demo\",\"refresh_token\":\"demo\",\"access_token\":\"demo\",\"email\":\"Offline demo\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":9999999999}",
            ["preferences.json"] = "{\"model\":\"demo\"}"
        };

        public string Read(string name, bool encrypted)
        {
            lock (values) return values.TryGetValue(name, out string value) ? value : null;
        }

        public void Write(string name, string json, bool encrypted)
        {
            lock (values) values[name] = json;
        }
    }

    internal sealed class ChatHttp : IChatGptHttp
    {
        public Task<string> Send(string url, string method, string body, string type, string bearer, bool stream, CancellationToken token, Action<string> onDelta = null)
        {
            token.ThrowIfCancellationRequested();
            if (url.EndsWith("/models", StringComparison.Ordinal))
                return Task.FromResult("{\"models\":[{\"visibility\":\"list\",\"slug\":\"demo\",\"display_name\":\"Offline demo\"}]}");
            if (url.EndsWith("/responses", StringComparison.Ordinal))
            {
                string instructions = ChatGptJson.Text(ChatGptJson.Object(body), "instructions");
                bool chinese = instructions.EndsWith(ChatGptConnection.InstructionForLanguage("zh"), StringComparison.Ordinal);
                return Task.FromResult(chinese
                    ? "{\"message\":\"这是离线演示回复。请先检查并编辑这项任务，再保存。\",\"drafts\":[{\"title\":\"彩排项目演示\",\"description\":\"练习展示任务、专注计时和首领战。\"}]}"
                    : "{\"message\":\"Offline demo reply. Review and edit this task before saving it.\",\"drafts\":[{\"title\":\"Rehearse the Cat Quest walkthrough\",\"description\":\"Practice showing Tasks, Focus and the Boss battle.\"}]}");
            }
            return Task.FromException<string>(new InvalidOperationException("This request is unavailable in the offline demo."));
        }
    }
#endif
}
