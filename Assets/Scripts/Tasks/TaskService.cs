using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public interface ITaskClock { DateTime LocalNow { get; } DateTime UtcNow { get; } }
public sealed class SystemTaskClock : ITaskClock
{
    public DateTime LocalNow => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class TaskView
{
    public QuestTask task;
    public TaskOccurrence occurrence;
    public List<string> tagNames;
    public TaskCompletion history;
    public bool IsHistory => history != null;
    public bool Completed => occurrence.completed;
    public string Key => IsHistory ? history.id : task.id;
}

public sealed partial class TaskService
{
    public event Action Changed;
    public string LastError { get; private set; }
    public DateTime LocalNow => clock.LocalNow;
    public PlayerData Snapshot => PlayerDataStore.Copy(store.Data);
    private readonly IPlayerDataStore store;
    private readonly ITaskClock clock;
    private string observedDay;
    public static readonly string[] DefaultTags = { "FYP", "UI", "Study", "Personal", "Assignment", "Research", "Development", "Design", "University", "Project", "Health", "Home" };

    public TaskService(IPlayerDataStore store, ITaskClock clock)
    {
        this.store = store; this.clock = clock;
        PlayerDataStore.Normalize(store.Data);
        RecoverInterruptedBattles();
        if (store.Data.focusSessions.Any(s => s.status == FocusSessionStatus.Running))
            Commit(data => { foreach (var session in data.focusSessions.Where(s => s.status == FocusSessionStatus.Running)) session.status = FocusSessionStatus.Invalid; });
        if (!store.Data.tagsInitialized)
            Commit(data =>
            {
                if (data.tags.Count == 0)
                    foreach (string name in DefaultTags) data.tags.Add(new QuestTag { id = Guid.NewGuid().ToString("N"), name = name });
                data.tagsInitialized = true;
            });
        RefreshDate();
    }
    public static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static bool TryDate(string value, out DateTime date) => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public static int Reward(TaskDifficulty difficulty) => BaseReward(difficulty).fishCoins;
    public static RewardAmounts BaseReward(TaskDifficulty difficulty) => difficulty == TaskDifficulty.Easy
        ? new RewardAmounts(10, 5) : difficulty == TaskDifficulty.Hard ? new RewardAmounts(30, 15) : new RewardAmounts(20, 10);
    public static string DifficultyName(TaskDifficulty difficulty) => difficulty == TaskDifficulty.Normal ? "Medium" : difficulty.ToString();
    private string Today => DateKey(clock.LocalNow);
    private static string OccurrenceDay(QuestTask task, string today) => task.daily ? today : "once";

    private bool Commit(Action<PlayerData> mutate)
    {
        LastError = null;
        try
        {
            var candidate = PlayerDataStore.Copy(store.Data);
            mutate(candidate);
            if (!store.TrySave(candidate, out var error)) { LastError = error; return false; }
            Changed?.Invoke();
            return true;
        }
        catch (ArgumentException e) { LastError = e.Message; return false; }
        catch (OverflowException) { LastError = "The reward balance has reached its maximum."; return false; }
    }
    public bool RefreshDate()
    {
        string today = Today;
        if (observedDay == today) return true;
        bool missing = store.Data.tasks.Any(t => t.daily && Eligible(t) && !store.Data.occurrences.Any(o => o.taskId == t.id && o.day == today));
        if (missing && !Commit(data =>
        {
            foreach (var task in data.tasks.Where(t => t.daily && Eligible(t))) GetOccurrence(data, task, today);
        })) return false;
        bool changed = observedDay != null && observedDay != today;
        observedDay = today;
        if (changed) Changed?.Invoke();
        return true;
    }
    public bool Eligible(QuestTask task) => !task.daily || (TryDate(task.date, out var start) && start.Date <= clock.LocalNow.Date);
    public bool IsOverdue(TaskView view)
    {
        if (view.IsHistory || view.Completed || !view.task.scheduled || !Eligible(view.task)) return false;
        string day = view.task.daily ? Today : view.task.date;
        return TryDate(day, out var date) && date.AddMinutes(view.task.minuteOfDay) < clock.LocalNow;
    }

    private bool CanUndo(PlayerData data, TaskCompletion completion)
    {
        if (completion == null || completion.revoked) return false;
        var task = data.tasks.FirstOrDefault(t => t.id == completion.taskId);
        if (task == null || completion.day != OccurrenceDay(task, Today)) return false;
        return data.occurrences.Any(o => o.taskId == task.id && o.day == completion.day && o.completed);
    }

    public bool CanUndoCompletion(string historyId) => CanUndo(store.Data, store.Data.history.FirstOrDefault(h => h.id == historyId));

    public bool UndoCompletion(string historyId)
    {
        var existing = store.Data.history.FirstOrDefault(h => h.id == historyId);
        if (existing != null && existing.revoked) { LastError = null; return true; }
        return Commit(data =>
        {
            var completion = data.history.FirstOrDefault(h => h.id == historyId);
            if (!CanUndo(data, completion)) throw new ArgumentException("This completed task can no longer be restored.");
            var occurrence = data.occurrences.Single(o => o.taskId == completion.taskId && o.day == completion.day);
            occurrence.completed = false;
            completion.revoked = true;
            // The receipt and subtask progress survive reopening; this occurrence cannot pay twice.
        });
    }
    private static TaskOccurrence GetOccurrence(PlayerData data, QuestTask task, string today)
    {
        string day = OccurrenceDay(task, today);
        var occurrence = data.occurrences.FirstOrDefault(o => o.taskId == task.id && o.day == day);
        if (occurrence == null)
        {
            occurrence = new TaskOccurrence { taskId = task.id, day = day };
            data.occurrences.Add(occurrence);
        }
        return occurrence;
    }
    public bool Upsert(QuestTask draft, IEnumerable<string> completedSubtasks, out string savedId)
    {
        var task = PlayerDataStore.Copy(draft);
        task.title = (task.title ?? "").Trim();
        task.description ??= "";
        task.tagIds ??= new List<string>(); task.subtasks ??= new List<QuestSubtask>();
        task.subtasks = task.subtasks.Where(s => s != null && !string.IsNullOrWhiteSpace(s.title)).ToList();
        foreach (var subtask in task.subtasks)
        {
            subtask.title = subtask.title.Trim();
            if (string.IsNullOrEmpty(subtask.id)) subtask.id = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrEmpty(task.id)) task.id = Guid.NewGuid().ToString("N");
        string id = task.id; savedId = null;
        bool result = Commit(data =>
        {
            if (task.title.Length == 0 || task.title.Length > 100) throw new ArgumentException("Enter a task title (1–100 characters).");
            if (task.description.Length > 500) throw new ArgumentException("Description must be 500 characters or fewer.");
            if (task.subtasks.Any(s => s.title.Length > 150)) throw new ArgumentException("Subtask titles must be 150 characters or fewer.");
            if (!Enum.IsDefined(typeof(TaskDifficulty), task.difficulty)) throw new ArgumentException("Choose a valid difficulty.");
            if (task.daily) task.scheduled = true;
            if (task.scheduled && (!TryDate(task.date, out _) || task.minuteOfDay < 0 || task.minuteOfDay > 1439)) throw new ArgumentException("Choose a valid schedule.");
            task.tagIds = task.tagIds.Distinct().Where(tagId => data.tags.Any(t => t.id == tagId)).ToList();
            var old = data.tasks.FirstOrDefault(t => t.id == id);
            if (old != null)
            {
                var previous = GetOccurrence(data, old, Today);
                if (!old.daily && previous.completed) throw new ArgumentException("Completed tasks are read-only in History.");
                // A type conversion on the same day carries its progress and reward state.
                if (old.daily != task.daily)
                {
                    var converted = GetOccurrence(data, task, Today);
                    converted.completed = previous.completed; converted.rewardGranted = previous.rewardGranted;
                    converted.completedSubtaskIds = new List<string>(previous.completedSubtaskIds);
                }
                data.tasks.Remove(old);
            }
            data.tasks.Add(task);
            if (Eligible(task))
            {
                var occurrence = GetOccurrence(data, task, Today);
                if (!occurrence.completed)
                    occurrence.completedSubtaskIds = (completedSubtasks ?? Array.Empty<string>()).Distinct().Where(s => task.subtasks.Any(t => t.id == s)).ToList();
            }
        });
        if (result) savedId = id;
        return result;
    }
    public bool Delete(string taskId) => Commit(data =>
    {
        if (data.tasks.RemoveAll(t => t.id == taskId) == 0) throw new ArgumentException("Task no longer exists.");
        // Completed snapshots and reward receipts remain intact.
    });
    public bool SetSubtask(string taskId, string subtaskId, bool isDone) => Commit(data =>
    {
        var task = RequireTask(data, taskId);
        if (!Eligible(task)) throw new ArgumentException("This daily task has not started yet.");
        if (!task.subtasks.Any(s => s.id == subtaskId)) throw new ArgumentException("Subtask no longer exists.");
        var occurrence = GetOccurrence(data, task, Today);
        if (occurrence.completed) throw new ArgumentException("Completed tasks are read-only.");
        occurrence.completedSubtaskIds.Remove(subtaskId);
        if (isDone) occurrence.completedSubtaskIds.Add(subtaskId);
    });
    public bool HasIncompleteSubtasks(string taskId)
    {
        var view = Query(TaskTab.Daily).Concat(Query(TaskTab.Todo)).FirstOrDefault(v => v.task.id == taskId);
        return view != null && view.task.subtasks.Any(s => !view.occurrence.completedSubtaskIds.Contains(s.id));
    }
    public bool Complete(string taskId, bool confirmIncomplete = false)
    {
        var task = store.Data.tasks.FirstOrDefault(t => t.id == taskId);
        var already = task == null ? null : store.Data.occurrences.FirstOrDefault(o => o.taskId == task.id && o.day == OccurrenceDay(task, Today));
        if (already != null && already.completed) { LastError = null; return true; }
        return Commit(data =>
        {
            var current = RequireTask(data, taskId);
            if (!Eligible(current)) throw new ArgumentException("This daily task has not started yet.");
            var occurrence = GetOccurrence(data, current, Today);
            if (!confirmIncomplete && current.subtasks.Any(s => !occurrence.completedSubtaskIds.Contains(s.id))) throw new ArgumentException("Confirm completing the remaining subtasks first.");
            occurrence.completed = true;
            occurrence.completedSubtaskIds = current.subtasks.Select(s => s.id).ToList();
            var receipt = data.history.FirstOrDefault(h => h.taskId == taskId && h.day == occurrence.day);
            if (!occurrence.rewardGranted)
            {
                var rewardDay = GetRewardDay(data, Today);
                var amount = TaskAllowance(current.difficulty, rewardDay);
                AddRewards(data, amount);
                rewardDay.taskExperience += amount.experience;
                rewardDay.taskFishCoins += amount.fishCoins;
                occurrence.rewardGranted = true;
                data.history.Add(new TaskCompletion
                {
                    id = Guid.NewGuid().ToString("N"), taskId = taskId, day = occurrence.day,
                    completedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture), completedLocalDay = Today, rewardedLocalDay = Today,
                    snapshot = PlayerDataStore.Copy(current),
                    tagNames = data.tags.Where(t => current.tagIds.Contains(t.id)).Select(t => t.name).ToList(), fishCoins = amount.fishCoins, experience = amount.experience
                });
            }
            else if (receipt != null && receipt.revoked)
            {
                receipt.revoked = false;
                receipt.completedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                receipt.completedLocalDay = Today;
                receipt.snapshot = PlayerDataStore.Copy(current);
                receipt.tagNames = data.tags.Where(t => current.tagIds.Contains(t.id)).Select(t => t.name).ToList();
            }
        });
    }
    private static QuestTask RequireTask(PlayerData data, string id) => data.tasks.FirstOrDefault(t => t.id == id) ?? throw new ArgumentException("Task no longer exists.");
    public bool SaveTag(string id, string name, out string savedId)
    {
        name = (name ?? "").Trim(); string newId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id; savedId = null;
        bool result = Commit(data =>
        {
            if (name.Length == 0 || name.Length > 30) throw new ArgumentException("Enter a tag name (1–30 characters).");
            if (data.tags.Any(t => t.id != newId && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A tag with that name already exists.");
            var tag = data.tags.FirstOrDefault(t => t.id == newId);
            if (tag == null) data.tags.Add(new QuestTag { id = newId, name = name }); else tag.name = name;
        });
        if (result) savedId = newId;
        return result;
    }
    public bool DeleteTag(string id) => Commit(data =>
    {
        if (data.tags.RemoveAll(t => t.id == id) == 0) throw new ArgumentException("Tag no longer exists.");
        foreach (var task in data.tasks) task.tagIds.Remove(id);
    });

    public List<TaskView> Query(TaskTab tab, string search = "", TaskDifficulty? difficulty = null, IEnumerable<string> tagIds = null)
    {
        var data = Snapshot;
        var selectedTags = (tagIds ?? Array.Empty<string>()).ToArray();
        var rows = new List<TaskView>();
        if (tab == TaskTab.History)
            foreach (var item in data.history.Where(h => !h.revoked).OrderByDescending(h => h.completedUtc)) rows.Add(new TaskView
            {
                task = item.snapshot, history = item, tagNames = item.tagNames,
                occurrence = new TaskOccurrence { taskId = item.taskId, day = item.day, completed = true, rewardGranted = true, completedSubtaskIds = item.snapshot.subtasks.Select(s => s.id).ToList() }
            });
        else
            foreach (var task in data.tasks)
            {
                var occurrence = data.occurrences.FirstOrDefault(o => o.taskId == task.id && o.day == OccurrenceDay(task, Today)) ?? new TaskOccurrence { taskId = task.id, day = OccurrenceDay(task, Today) };
                bool show = tab == TaskTab.Daily ? task.daily
                    : tab == TaskTab.Todo ? !task.daily && !occurrence.completed
                    : tab == TaskTab.Tags ? !occurrence.completed && Eligible(task)
                    : !occurrence.completed && (task.daily ? Eligible(task) : task.scheduled && TryDate(task.date, out var due) && due.Date <= clock.LocalNow.Date);
                if (show) rows.Add(new TaskView { task = task, occurrence = occurrence, tagNames = data.tags.Where(t => task.tagIds.Contains(t.id)).Select(t => t.name).ToList() });
            }
        search = (search ?? "").Trim();
        rows = rows.Where(v => (!difficulty.HasValue || v.task.difficulty == difficulty.Value)
            && selectedTags.All(id => v.task.tagIds.Contains(id))
            && (search.Length == 0 || ((v.task.title ?? "") + " " + v.task.description + " " + string.Join(" ", v.tagNames)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        if (tab != TaskTab.History) rows = rows.OrderBy(v => v.Completed).ThenBy(v => v.task.scheduled ? v.task.date : "9999").ThenBy(v => v.task.minuteOfDay).ThenBy(v => v.task.title).ToList();
        return rows;
    }
}
