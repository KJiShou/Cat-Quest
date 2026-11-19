using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

public sealed partial class SqlitePlayerService
{
    // Shared by all SQL domain commands. Profile is a bounded scalar record;
    // changing rewards never loads or copies historical collections.
    internal static void ApplyLevelUp(PlayerData profile, int previousExperience)
    {
        TaskService.CatLevel(previousExperience, out int from, out _, out _);
        TaskService.CatLevel(profile.experience, out int to, out _, out _);
        if (to <= from) return;
        int fish = checked((int)(5L * (to - from) * (from + to + 1L) / 2));
        profile.fishCoins = checked(profile.fishCoins + fish);
        profile.tickets = checked(profile.tickets + to / 5 - from / 5);
    }
    static void SqlAddRewards(PlayerData profile, RewardAmounts amount)
    {
        int previous = profile.experience;
        profile.experience = checked(profile.experience + amount.experience);
        profile.fishCoins = checked(profile.fishCoins + amount.fishCoins);
        profile.tickets = checked(profile.tickets + amount.tickets);
        ApplyLevelUp(profile, previous);
    }
    static RewardDay SqlRewardDay(IPlayerTransaction tx, string day)
    {
        var record = tx.Read<RewardDay>(PlayerRecordKind.RewardDays, day);
        if (record != null && record.rewardRulesInitialized) return record;
        var totals = tx.ReadDayTotals(day);
        record ??= new RewardDay { day = day,
            taskExperience = (int)Math.Min(int.MaxValue, totals.TaskExperience),
            taskFishCoins = (int)Math.Min(int.MaxValue, totals.TaskFishCoins),
            taskTickets = checked((int)totals.TaskTickets) };
        bool hadTask = record.taskExperience > 0 || record.taskFishCoins > 0 || record.questsClaimed || totals.RewardReceiptCount > 0;
        record.firstTaskSettled |= hadTask; record.firstQuestClaimed |= hadTask; record.rewardRulesInitialized = true;
        return record;
    }
    internal static RewardAmounts SqlTaskAllowance(TaskDifficulty difficulty, RewardDay day, int level)
    {
        var basis = TaskService.BaseReward(difficulty);
        return new RewardAmounts(
            Math.Max(0, Math.Min(TaskService.ScaleRewardAmount(basis.experience, level), TaskService.ScaleRewardAmount(TaskService.TaskExperienceCap, level) - day.taskExperience)),
            Math.Max(0, Math.Min(TaskService.ScaleRewardAmount(basis.fishCoins, level), TaskService.ScaleRewardAmount(TaskService.TaskFishCap, level) - day.taskFishCoins)));
    }
    static bool SqlGoalClaimed(RewardDay day, DailyRewardGoal goal)
        => goal == DailyRewardGoal.ThreeQuests ? day.questsClaimed : goal == DailyRewardGoal.FirstQuest ? day.firstQuestClaimed : day.focusClaimed;

    public Task<OperationResult<bool>> ClaimDailyGoalAsync(DailyRewardGoal goal)
    {
        string today = TaskService.DateKey(clock.LocalNow);
        return ExecuteCommandAsync(tx => {
            if (!Enum.IsDefined(typeof(DailyRewardGoal), goal)) throw new PlayerCommandException("Unknown daily goal.");
            var day = SqlRewardDay(tx, today);
            if (SqlGoalClaimed(day, goal)) return true;
            var totals = tx.ReadDayTotals(today);
            double progress = goal == DailyRewardGoal.Focus25Minutes ? Math.Floor(totals.FocusSeconds / 60d) : totals.CompletedTasks;
            if (progress < TaskService.DailyGoalTarget(goal)) throw new PlayerCommandException("Complete this goal before claiming its reward.");
            var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
            TaskService.CatLevel(profile.experience, out int level, out _, out _);
            var reward = TaskService.DailyGoalReward(goal, day, level);
            SqlAddRewards(profile, reward); profile.equipmentUnlocked = true;
            if (goal == DailyRewardGoal.ThreeQuests) {
                day.questsClaimed = true; day.questsExperience = reward.experience; day.questsFishCoins = reward.fishCoins;
                day.questsTickets = reward.tickets;
            } else if (goal == DailyRewardGoal.FirstQuest) {
                day.firstQuestClaimed = true; day.firstQuestExperience = reward.experience; day.firstQuestFishCoins = reward.fishCoins;
                day.taskTickets += reward.tickets;
            }
            else { day.focusClaimed = true; day.focusGoalExperience = reward.experience; day.focusGoalFishCoins = reward.fishCoins; }
            tx.Put(PlayerRecordKind.RewardDays, day); tx.Put(PlayerRecordKind.Profile, profile); return true;
        });
    }

    public Task<OperationResult<string>> BeginFocusAsync() => BeginFocusAsync(new FocusSessionOptions());
    public Task<OperationResult<string>> BeginFocusAsync(FocusSessionOptions options)
    {
        var frozen = options?.Copy();
        DateTime local = clock.LocalNow; string today = TaskService.DateKey(local);
        string started = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture), id = Guid.NewGuid().ToString("N");
        return ExecuteCommandAsync(tx => {
            var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
            TaskService.CatLevel(profile.experience, out int level, out _, out _);
            if (level < TaskService.RequiredLevel(ProgressionFeature.Focus)) throw new PlayerCommandException("Unlocks at Lv." + TaskService.RequiredLevel(ProgressionFeature.Focus));
            if (frozen == null || !frozen.IsValid) throw new PlayerCommandException("Choose a valid Focus and Rest duration.");
            if (tx.Query<FocusSession>(new PlayerRecordQuery { Kind = PlayerRecordKind.FocusSessions, State = (int)FocusSessionStatus.Running, Limit = 1 }).Count != 0)
                throw new PlayerCommandException("A Focus session is already running.");
            if (!string.IsNullOrEmpty(frozen.questId)) {
                var task = tx.Read<QuestTask>(PlayerRecordKind.Tasks, frozen.questId);
                if (task == null || !SqlTaskEligible(task, local)) throw new PlayerCommandException("This quest is no longer available. Choose another quest.");
                var occurrence = SqlOccurrence(tx, task, today);
                if (occurrence.completed) throw new PlayerCommandException("This quest is no longer available. Choose another quest.");
                frozen.questTitle = task.title; frozen.occurrenceDay = occurrence.day;
            }
            if (frozen.tagIds.Distinct().Count() > 1) throw new PlayerCommandException("Choose one tag for this Focus session.");
            var tags = tx.Tags().Where(t => frozen.tagIds.Contains(t.id)).ToArray();
            if (frozen.tagIds.Any(tag => !tags.Any(t => t.id == tag))) throw new PlayerCommandException("A selected tag is no longer available. Choose tags again.");
            var session = new FocusSession { id = id, status = FocusSessionStatus.Running, mode = frozen.mode, rewardLevel = level,
                focusDurationSeconds = frozen.mode == FocusTimerMode.Stopwatch ? 0 : frozen.focusMinutes * 60,
                restDurationSeconds = frozen.mode == FocusTimerMode.Pomodoro ? frozen.restMinutes * 60 : 0,
                questId = frozen.questId, questTitle = frozen.questTitle, occurrenceDay = frozen.occurrenceDay,
                tagIds = tags.Select(t => t.id).ToList(), tagNames = tags.Select(t => t.name).ToList(), startedUtc = started };
            tx.Put(PlayerRecordKind.FocusSessions, session); return id;
        });
    }

    public Task<OperationResult<FocusSession>> FinishFocusAsync(string sessionId, double elapsedSeconds, bool endedEarly)
    {
        string today = TaskService.DateKey(clock.LocalNow), completed = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return ExecuteCommandAsync(tx => {
            var session = tx.Read<FocusSession>(PlayerRecordKind.FocusSessions, sessionId);
            if (session != null && (session.status == FocusSessionStatus.Completed || session.status == FocusSessionStatus.EndedEarly)) return session;
            if (session == null || session.status != FocusSessionStatus.Running) throw new PlayerCommandException("This Focus session cannot be rewarded.");
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0 ||
                (session.mode != FocusTimerMode.Stopwatch && !endedEarly && elapsedSeconds < session.focusDurationSeconds))
                throw new PlayerCommandException("The Focus duration is invalid.");
            double duration = session.mode == FocusTimerMode.Stopwatch ? elapsedSeconds : Math.Min(elapsedSeconds, session.focusDurationSeconds);
            session.elapsedSeconds = duration;
            duration = Math.Min(duration, TaskService.FocusSessionMinuteCap * 60d);
            int minutes = (int)Math.Floor(duration / 60), level = Math.Max(1, session.rewardLevel);
            var day = SqlRewardDay(tx, today);
            int eligible = Math.Max(0, Math.Min(minutes, TaskService.FocusMinuteCap - day.focusMinutes));
            int tickets = Math.Max(0, Math.Min(minutes / 25, TaskService.FocusTicketCap - day.focusTickets));
            var reward = TaskService.FocusRewardsForSeconds(duration, day.focusMinutes, day.focusTickets, level);
            var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile"); SqlAddRewards(profile, reward);
            day.focusMinutes = checked(day.focusMinutes + eligible); day.focusTickets = checked(day.focusTickets + tickets);
            session.status = endedEarly && session.mode != FocusTimerMode.Stopwatch ? FocusSessionStatus.EndedEarly : FocusSessionStatus.Completed;
            session.completedUtc = completed; session.rewardDay = today; session.eligibleMinutes = minutes; session.rewardedMinutes = eligible;
            session.eligibleSeconds = duration; session.rawTickets = minutes / 25; session.experience = reward.experience; session.fishCoins = reward.fishCoins; session.tickets = tickets;
            tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.RewardDays, day); tx.Put(PlayerRecordKind.FocusSessions, session);
            return session;
        });
    }
    public Task<OperationResult<FocusSession>> CancelFocusAsync(string sessionId, bool invalid = false)
    {
        string completed = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return ExecuteCommandAsync(tx => {
            var session = tx.Read<FocusSession>(PlayerRecordKind.FocusSessions, sessionId);
            if (session == null) throw new PlayerCommandException("Unknown Focus session.");
            if (session.status != FocusSessionStatus.Running) return session;
            session.status = invalid ? FocusSessionStatus.Invalid : FocusSessionStatus.Cancelled; session.completedUtc = completed;
            tx.Put(PlayerRecordKind.FocusSessions, session); return session;
        });
    }

    // Startup recovery is an explicit command and runs once before controls are
    // enabled. Invalid Focus never receives rewards, including migrated sessions.
    public Task<OperationResult<bool>> InitializeTaskStateAsync()
    {
        DateTime local = clock.LocalNow; string today = TaskService.DateKey(local);
        return ExecuteCommandAsync(tx => {
            var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
            var accountName = string.IsNullOrWhiteSpace(profile.username)
                ? tx.Read<AccountPreferences>(PlayerRecordKind.Account, "account")?.playerName
                : null;
            bool restoreName = !string.IsNullOrWhiteSpace(accountName);
            bool seedTags = !profile.tagsInitialized;
            if (restoreName) profile.username = accountName;
            if (seedTags) {
                if (tx.Tags().Count == 0) foreach (string name in TaskService.DefaultTags)
                    tx.Put(PlayerRecordKind.Tags, new QuestTag { id = Guid.NewGuid().ToString("N"), name = name });
                profile.tagsInitialized = true;
            }
            if (restoreName || seedTags) tx.Put(PlayerRecordKind.Profile, profile);
            while (true) {
                var running = tx.Query<FocusSession>(new PlayerRecordQuery { Kind = PlayerRecordKind.FocusSessions, State = (int)FocusSessionStatus.Running });
                if (running.Count == 0) break;
                foreach (var session in running) { session.status = FocusSessionStatus.Invalid; tx.Put(PlayerRecordKind.FocusSessions, session); }
            }
            foreach (var task in tx.ActiveTasks().Where(t => t.daily && SqlTaskEligible(t, local))) {
                var existing = tx.Read<TaskOccurrence>(PlayerRecordKind.Occurrences, SqlOccurrenceId(task.id, today));
                if (existing == null) tx.Put(PlayerRecordKind.Occurrences, new TaskOccurrence { taskId = task.id, day = today });
            }
            return true;
        });
    }
}
