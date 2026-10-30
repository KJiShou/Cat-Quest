using System;
using System.Globalization;
using System.Linq;

public sealed partial class TaskService
{
    public const int TaskExperienceCap = 120, TaskFishCap = 60, FocusMinuteCap = 360, FocusTicketCap = 4;
    public const int FocusSessionMinuteCap = 180;
    public const int FocusDurationSeconds = 25 * 60, RestDurationSeconds = 5 * 60;
    public static readonly RewardAmounts GoalReward = new RewardAmounts(20, 10);
    public event Action<LevelUpReward> LevelUp;

    public readonly struct LevelUpReward
    {
        public readonly int fromLevel, toLevel, fishCoins, tickets;
        public LevelUpReward(int from, int to, int fish, int ticketCount)
        { fromLevel=from; toLevel=to; fishCoins=fish; tickets=ticketCount; }
    }
    private static LevelUpReward SettleLevelUp(PlayerData before, PlayerData after)
    {
        CatLevel(before.experience,out int from,out _,out _);
        CatLevel(after.experience,out int to,out _,out _);
        if(to<=from) return new LevelUpReward(from,from,0,0);
        int fish=checked((int)(5L*(to-from)*(from+to+1L)/2));
        int tickets=to/5-from/5;
        after.fishCoins=checked(after.fishCoins+fish);
        after.tickets=checked(after.tickets+tickets);
        return new LevelUpReward(from,to,fish,tickets);
    }

    private static RewardDay GetRewardDay(PlayerData data, string day)
    {
        var result = data.rewardDays.FirstOrDefault(d => d.day == day);
        if (result != null) return result;
        // Legacy receipts already paid; reopening a task never releases its allowance.
        var receipts = data.history.Where(h => h.rewardedLocalDay == day).ToArray();
        result = new RewardDay { day = day,
            taskExperience = (int)Math.Min(int.MaxValue, receipts.Sum(h => (long)h.experience)),
            taskFishCoins = (int)Math.Min(int.MaxValue, receipts.Sum(h => (long)h.fishCoins)) };
        data.rewardDays.Add(result);
        return result;
    }
    private static RewardAmounts TaskAllowance(TaskDifficulty difficulty, RewardDay day, int level)
    {
        var amount = BaseReward(difficulty);
        return new RewardAmounts(Math.Max(0, Math.Min(ScaleRewardAmount(amount.experience, level), ScaleRewardAmount(TaskExperienceCap, level) - day.taskExperience)),
            Math.Max(0, Math.Min(ScaleRewardAmount(amount.fishCoins, level), ScaleRewardAmount(TaskFishCap, level) - day.taskFishCoins)));
    }
    private static void AddRewards(PlayerData data, RewardAmounts amount)
    {
        data.experience = checked(data.experience + amount.experience);
        data.fishCoins = checked(data.fishCoins + amount.fishCoins);
        data.tickets = checked(data.tickets + amount.tickets);
    }
    public RewardDay TodayRewards => GetRewardDay(Snapshot, Today);
    public RewardAmounts PreviewReward(TaskView view)
    {
        if (view.IsHistory) return new RewardAmounts(view.history.experience, view.history.fishCoins);
        if (view.occurrence.rewardGranted)
        {
            var receipt = store.Data.history.FirstOrDefault(h => h.taskId == view.task.id && h.day == view.occurrence.day);
            return receipt == null ? new RewardAmounts(0, 0) : new RewardAmounts(receipt.experience, receipt.fishCoins);
        }
        return TaskAllowance(view.task.difficulty, TodayRewards, RewardLevel);
    }
    private int GoalProgress(PlayerData data, DailyRewardGoal goal)
    {
        if (goal == DailyRewardGoal.ThreeQuests)
            return Math.Min(3, data.history.Where(h => !h.revoked && h.completedLocalDay == Today)
                .Select(h => h.taskId + ":" + h.day).Distinct().Count());
        double seconds = data.focusSessions.Where(s => s.rewardDay == Today &&
            (s.status == FocusSessionStatus.Completed || s.status == FocusSessionStatus.EndedEarly))
            .Sum(s => s.eligibleSeconds > 0 ? s.eligibleSeconds : s.eligibleMinutes * 60d);
        return (int)Math.Min(25, Math.Floor(seconds / 60));
    }
    public int DailyGoalProgress(DailyRewardGoal goal) => GoalProgress(store.Data, goal);
    public bool DailyGoalClaimed(DailyRewardGoal goal)
    {
        var day = TodayRewards;
        return goal == DailyRewardGoal.ThreeQuests ? day.questsClaimed : day.focusClaimed;
    }
    public bool ClaimDailyGoal(DailyRewardGoal goal)
    {
        if (!Enum.IsDefined(typeof(DailyRewardGoal), goal)) { LastError = "Unknown daily goal."; return false; }
        if (DailyGoalClaimed(goal)) { LastError = null; return true; }
        return Commit(data => {
            if (GoalProgress(data, goal) < (goal == DailyRewardGoal.ThreeQuests ? 3 : 25))
                throw new ArgumentException("Complete this goal before claiming its reward.");
            var day = GetRewardDay(data, Today);
            CatLevel(data.experience,out int level,out _,out _);
            var reward=new RewardAmounts(ScaleRewardAmount(GoalReward.experience,level),ScaleRewardAmount(GoalReward.fishCoins,level));
            AddRewards(data, reward);
            if (goal == DailyRewardGoal.ThreeQuests) {
                day.questsClaimed=true; day.questsExperience=reward.experience; day.questsFishCoins=reward.fishCoins;
            } else {
                day.focusClaimed=true; day.focusGoalExperience=reward.experience; day.focusGoalFishCoins=reward.fishCoins;
            }
        });
    }
    public RewardAmounts PreviewDailyGoal(DailyRewardGoal goal)
    {
        var day=TodayRewards;
        bool quests=goal==DailyRewardGoal.ThreeQuests;
        if(quests?day.questsClaimed:day.focusClaimed) {
            int xp=quests?day.questsExperience:day.focusGoalExperience;
            return xp>0 ? new RewardAmounts(xp,quests?day.questsFishCoins:day.focusGoalFishCoins) : GoalReward;
        }
        return new RewardAmounts(ScaleRewardAmount(GoalReward.experience,RewardLevel),ScaleRewardAmount(GoalReward.fishCoins,RewardLevel));
    }
    public bool BeginFocus(out string sessionId)
        => BeginFocus(new FocusSessionOptions(), out sessionId);
    public bool BeginFocus(FocusSessionOptions options, out string sessionId)
    {
        string id = Guid.NewGuid().ToString("N"); sessionId = null;
        if (!CanAccess(ProgressionFeature.Focus)) { LastError = AccessMessage(ProgressionFeature.Focus); return false; }
        if (options == null || !options.IsValid) { LastError = "Choose a valid Focus and Rest duration."; return false; }
        options = options.Copy();
        if (!string.IsNullOrEmpty(options.questId))
        {
            var quest = Query(TaskTab.Daily).Concat(Query(TaskTab.Todo)).FirstOrDefault(v => v.task.id == options.questId && !v.Completed && Eligible(v.task));
            if (quest == null) { LastError = "This quest is no longer available. Choose another quest."; return false; }
            options.questTitle = quest.task.title; options.occurrenceDay = quest.occurrence.day;
        }
        if (options.tagIds.Distinct().Count() > 1)
        { LastError = "Choose one tag for this Focus session."; return false; }
        var tags = store.Data.tags.Where(t => options.tagIds.Contains(t.id)).ToList();
        if (options.tagIds.Any(id => !tags.Any(t => t.id == id)))
        { LastError = "A selected tag is no longer available. Choose tags again."; return false; }
        bool saved = Commit(data => {
            if (data.focusSessions.Any(s => s.status == FocusSessionStatus.Running))
                throw new ArgumentException("A Focus session is already running.");
            CatLevel(data.experience, out int rewardLevel, out _, out _);
            data.focusSessions.Add(new FocusSession { id = id, status = FocusSessionStatus.Running,
                mode = options.mode, rewardLevel = rewardLevel,
                focusDurationSeconds = options.mode == FocusTimerMode.Stopwatch ? 0 : options.focusMinutes * 60,
                restDurationSeconds = options.mode == FocusTimerMode.Pomodoro ? options.restMinutes * 60 : 0,
                questId = options.questId, questTitle = options.questTitle, occurrenceDay = options.occurrenceDay,
                tagIds = tags.Select(t => t.id).ToList(), tagNames = tags.Select(t => t.name).ToList(),
                startedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture) });
        });
        if (saved) sessionId = id;
        return saved;
    }
    // Integer tenths avoid floating-point rounding at reward boundaries.
    public static int ScaleRewardAmount(int amount, int level)
        => checked((int)((long)amount * (Math.Max(1, level) + 9L) / 10));
    public int RewardLevel { get { CatLevel(store.Data.experience, out int level, out _, out _); return level; } }
    public int TaskExperienceLimit => ScaleRewardAmount(TaskExperienceCap, RewardLevel);
    public int TaskFishLimit => ScaleRewardAmount(TaskFishCap, RewardLevel);
    public int FocusRewardLevel
    {
        get
        {
            var running = store.Data.focusSessions.FirstOrDefault(s => s.status == FocusSessionStatus.Running);
            if (running != null) return Math.Max(1, running.rewardLevel);
            CatLevel(store.Data.experience, out int level, out _, out _);
            return level;
        }
    }
    public int FocusDailyMinuteLimit => FocusMinuteCap;
    public int FocusSessionMinuteLimit => FocusSessionMinuteCap;
    public static RewardAmounts FocusRewardsForSeconds(double elapsedSeconds, int rewardedMinutes, int rewardedTickets, int level)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0) return new RewardAmounts();
        int minutes = (int)Math.Floor(Math.Min(elapsedSeconds, FocusSessionMinuteCap * 60d) / 60);
        int eligible = Math.Max(0, Math.Min(minutes, FocusMinuteCap - rewardedMinutes));
        return new RewardAmounts(ScaleRewardAmount(eligible, level), ScaleRewardAmount(eligible / 5 * 2, level),
            Math.Max(0, Math.Min(minutes / 25, FocusTicketCap - rewardedTickets)));
    }
    public static int FocusFishForSeconds(double elapsedSeconds, int alreadyRewardedMinutes = 0)
        => FocusRewardsForSeconds(elapsedSeconds, alreadyRewardedMinutes, 0, 1).fishCoins;
    public RewardAmounts PreviewFocusRewards(double elapsedSeconds)
    {
        var day = TodayRewards;
        return FocusRewardsForSeconds(elapsedSeconds, day.focusMinutes, day.focusTickets, FocusRewardLevel);
    }
    public int PreviewFocusFish(double elapsedSeconds) => PreviewFocusRewards(elapsedSeconds).fishCoins;

    public bool FinishFocus(string sessionId, double elapsedSeconds, bool endedEarly)
    {
        var existing = store.Data.focusSessions.FirstOrDefault(s => s.id == sessionId);
        if (existing != null && (existing.status == FocusSessionStatus.Completed || existing.status == FocusSessionStatus.EndedEarly))
        { LastError = null; return true; }
        return Commit(data => {
            var session = data.focusSessions.FirstOrDefault(s => s.id == sessionId);
            if (session == null || session.status != FocusSessionStatus.Running)
                throw new ArgumentException("This Focus session cannot be rewarded.");
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0 ||
                (session.mode != FocusTimerMode.Stopwatch && !endedEarly && elapsedSeconds < session.focusDurationSeconds))
                throw new ArgumentException("The Focus duration is invalid.");
            if (session.mode != FocusTimerMode.Stopwatch) elapsedSeconds = Math.Min(elapsedSeconds, session.focusDurationSeconds);
            session.elapsedSeconds = elapsedSeconds;
            int level = Math.Max(1, session.rewardLevel);
            elapsedSeconds = Math.Min(elapsedSeconds, FocusSessionMinuteCap * 60d);
            int minutes = (int)Math.Floor(elapsedSeconds / 60);
            var day = GetRewardDay(data, Today);
            int eligible = Math.Max(0, Math.Min(minutes, FocusMinuteCap - day.focusMinutes));
            int rawTickets = minutes / 25;
            int tickets = Math.Max(0, Math.Min(rawTickets, FocusTicketCap - day.focusTickets));
            var reward = FocusRewardsForSeconds(elapsedSeconds, day.focusMinutes, day.focusTickets, level);
            AddRewards(data, reward);
            day.focusMinutes += eligible; day.focusTickets += tickets;
            session.status = endedEarly && session.mode != FocusTimerMode.Stopwatch ? FocusSessionStatus.EndedEarly : FocusSessionStatus.Completed;
            session.completedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            session.rewardDay = Today; session.eligibleMinutes = minutes; session.rewardedMinutes = eligible;
            session.eligibleSeconds = elapsedSeconds;
            session.rawTickets = rawTickets; session.experience = reward.experience;
            session.fishCoins = reward.fishCoins; session.tickets = tickets;
        });
    }
    public bool CancelFocus(string sessionId, bool invalid = false) => Commit(data => {
        var session = data.focusSessions.FirstOrDefault(s => s.id == sessionId);
        if (session == null) throw new ArgumentException("Unknown Focus session.");
        if (session.status != FocusSessionStatus.Running) return;
        session.status = invalid ? FocusSessionStatus.Invalid : FocusSessionStatus.Cancelled;
        session.completedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    });

    public static void CatLevel(int totalExperience, out int level, out int currentExperience, out int requiredExperience)
    {
        level = 1; currentExperience = Math.Max(0, totalExperience); requiredExperience = 100;
        while (currentExperience >= requiredExperience)
        { currentExperience -= requiredExperience; level++; requiredExperience = 100 + 25 * (level - 1); }
    }
}
