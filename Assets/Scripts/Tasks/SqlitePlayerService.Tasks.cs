using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

public sealed partial class SqlitePlayerService
{
    string sqlObservedTaskDay;
    long sqlObservedTaskVersion;
    internal void ResetTaskDateCache() { sqlObservedTaskDay = null; sqlObservedTaskVersion = 0; }
    static string SqlTaskDay(QuestTask task, string today) => task.daily ? today : "once";
    static string SqlOccurrenceId(string taskId, string day) => taskId?.Length + ":" + taskId + day;
    static bool SqlTaskEligible(QuestTask task, DateTime local) => !task.daily || TaskRepeat.Matches(task, local);
    static QuestTask SqlTaskRequired(IPlayerTransaction tx, string id)
        => tx.Read<QuestTask>(PlayerRecordKind.Tasks, id) ?? throw new PlayerCommandException("Task no longer exists.");
    static TaskOccurrence SqlOccurrence(IPlayerTransaction tx, QuestTask task, string today)
    {
        string day = SqlTaskDay(task, today);
        return tx.Read<TaskOccurrence>(PlayerRecordKind.Occurrences, SqlOccurrenceId(task.id, day))
            ?? new TaskOccurrence { taskId = task.id, day = day };
    }
    static List<QuestTag> SqlTaskTags(IPlayerTransaction tx, QuestTask task)
    {
        // Preserve catalog order, matching historical snapshot behavior.
        var ids = new HashSet<string>(task.tagIds ?? new List<string>(), StringComparer.Ordinal);
        return tx.Tags().Where(t => ids.Contains(t.id)).ToList();
    }
    public async Task<OperationResult<bool>> RefreshDateAsync()
    {
        DateTime local = clock.LocalNow;
        string today = TaskService.DateKey(local);
        if (sqlObservedTaskDay == today) return new OperationResult<bool> { Succeeded = true, Value = true, Version = sqlObservedTaskVersion };
        var result = await ExecuteCommandAsync(tx => {
            foreach (var task in tx.ActiveTasks().Where(t => t.daily && SqlTaskEligible(t, local))) {
                string id = SqlOccurrenceId(task.id, today);
                if (tx.Read<TaskOccurrence>(PlayerRecordKind.Occurrences, id) == null)
                    tx.Put(PlayerRecordKind.Occurrences, new TaskOccurrence { taskId = task.id, day = today });
            }
            return true;
        });
        if (result.Succeeded) { sqlObservedTaskDay = today; sqlObservedTaskVersion = result.Version; }
        return result;
    }
    public Task<OperationResult<string>> UpsertAsync(QuestTask draft, IEnumerable<string> completedSubtasks)
        => SqlUpsertAsync(draft, completedSubtasks, null);

    Task<OperationResult<string>> SqlUpsertAsync(QuestTask draft, IEnumerable<string> completedSubtasks,
        Action<IPlayerTransaction, QuestTask, string> guard)
    {
        var task = draft?.DetachedCopy();
        var done = (completedSubtasks ?? Array.Empty<string>()).Distinct().ToArray();
        DateTime local = clock.LocalNow; string today = TaskService.DateKey(local);
        if (task != null) {
            task.title = (task.title ?? "").Trim(); task.description ??= "";
            task.tagIds ??= new List<string>(); task.subtasks ??= new List<QuestSubtask>();
            task.subtasks = task.subtasks.Where(s => s != null && !string.IsNullOrWhiteSpace(s.title)).ToList();
            foreach (var subtask in task.subtasks) {
                subtask.title = subtask.title.Trim();
                if (string.IsNullOrEmpty(subtask.id)) subtask.id = Guid.NewGuid().ToString("N");
            }
            if (string.IsNullOrEmpty(task.id)) task.id = Guid.NewGuid().ToString("N");
        }
        return ExecuteCommandAsync(tx => {
            if (task == null) throw new PlayerCommandException("Enter a task to save.");
            guard?.Invoke(tx, task, today);
            if (task.title.Length == 0 || task.title.Length > 100) throw new PlayerCommandException("Enter a task title (1–100 characters).");
            if (task.description.Length > 500) throw new PlayerCommandException("Description must be 500 characters or fewer.");
            if (task.subtasks.Any(s => s.title.Length > 150)) throw new PlayerCommandException("Subtask titles must be 150 characters or fewer.");
            if (task.subtasks.Select(s => s.id).Distinct().Count() != task.subtasks.Count) throw new PlayerCommandException("Subtask identities must be unique.");
            if (!Enum.IsDefined(typeof(TaskDifficulty), task.difficulty)) throw new PlayerCommandException("Choose a valid difficulty.");
            if (task.daily) {
                task.scheduled = true;
                if (!TaskRepeat.ValidRule(task)) throw new PlayerCommandException("Use an interval of 1–999, select repeat days, and choose a valid end date.");
            }
            if (task.scheduled && !TaskService.TryDate(task.date, out _)) throw new PlayerCommandException("Choose a valid schedule date.");
            task.tagIds = task.tagIds.Distinct().Where(id => tx.Read<QuestTag>(PlayerRecordKind.Tags, id) != null).ToList();
            var old = tx.Read<QuestTask>(PlayerRecordKind.Tasks, task.id);
            TaskOccurrence converted = null;
            if (old != null) {
                string previousId = SqlOccurrenceId(old.id, SqlTaskDay(old, today));
                var previous = tx.Read<TaskOccurrence>(PlayerRecordKind.Occurrences, previousId);
                if (!old.daily && previous?.completed == true) throw new PlayerCommandException("Completed tasks are read-only in History.");
                if (old.daily != task.daily && previous != null && (SqlTaskEligible(task, local) || previous.rewardGranted)) {
                    converted = SqlOccurrence(tx, task, today);
                    converted.completed = previous.completed; converted.rewardGranted = previous.rewardGranted;
                    converted.completedSubtaskIds = new List<string>(previous.completedSubtaskIds);
                    tx.Put(PlayerRecordKind.Occurrences, converted);
                }
                if (old.daily && !SqlTaskEligible(task, local) && previous != null && !previous.rewardGranted && !previous.completed)
                    tx.Delete(PlayerRecordKind.Occurrences, previousId);
            }
            // The existing editor removes then appends edited tasks; retain its
            // stable ordering for otherwise equal task sort keys.
            if (old != null) tx.Delete(PlayerRecordKind.Tasks, task.id);
            tx.Put(PlayerRecordKind.Tasks, task);
            var tutorial = tx.Read<TutorialProgress>(PlayerRecordKind.Tutorial, "tutorial");
            if (old == null && tutorial != null && !tutorial.dismissed && tutorial.taskStep is 1 or 2) {
                tutorial.taskStep = 3; tutorial.taskId = task.id; tx.Put(PlayerRecordKind.Tutorial, tutorial);
            }
            if (SqlTaskEligible(task, local)) {
                var occurrence = converted ?? SqlOccurrence(tx, task, today);
                if (!occurrence.completed) occurrence.completedSubtaskIds = done.Where(id => task.subtasks.Any(s => s.id == id)).ToList();
                tx.Put(PlayerRecordKind.Occurrences, occurrence);
            }
            return task.id;
        });
    }

    public Task<OperationResult<bool>> DeleteAsync(string taskId) => ExecuteCommandAsync(tx => {
        SqlTaskRequired(tx, taskId); tx.Delete(PlayerRecordKind.Tasks, taskId); return true;
    });

    public Task<OperationResult<bool>> SetSubtaskAsync(string taskId, string subtaskId, bool isDone)
    {
        DateTime local = clock.LocalNow; string today = TaskService.DateKey(local);
        return ExecuteCommandAsync(tx => {
            var task = SqlTaskRequired(tx, taskId);
            if (!SqlTaskEligible(task, local)) throw new PlayerCommandException("This task is not scheduled for today.");
            if (!task.subtasks.Any(s => s.id == subtaskId)) throw new PlayerCommandException("Subtask no longer exists.");
            var occurrence = SqlOccurrence(tx, task, today);
            if (occurrence.completed) throw new PlayerCommandException("Completed tasks are read-only.");
            occurrence.completedSubtaskIds.Remove(subtaskId);
            if (isDone) occurrence.completedSubtaskIds.Add(subtaskId);
            tx.Put(PlayerRecordKind.Occurrences, occurrence); return true;
        });
    }

    public Task<OperationResult<TaskCompletion>> CompleteAsync(string taskId, bool confirmIncomplete = false)
    {
        DateTime local = clock.LocalNow; string today = TaskService.DateKey(local);
        string completedUtc = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        string receiptId = Guid.NewGuid().ToString("N");
        return ExecuteCommandAsync(tx => {
            var task = SqlTaskRequired(tx, taskId);
            var occurrence = SqlOccurrence(tx, task, today);
            var receipt = tx.FindCompletion(taskId, occurrence.day);
            if (occurrence.completed) return receipt;
            if (!SqlTaskEligible(task, local)) throw new PlayerCommandException("This task is not scheduled for today.");
            if (!confirmIncomplete && task.subtasks.Any(s => !occurrence.completedSubtaskIds.Contains(s.id)))
                throw new PlayerCommandException("Confirm completing the remaining subtasks first.");
            occurrence.completed = true; occurrence.completedSubtaskIds = task.subtasks.Select(s => s.id).ToList();
            var tags = SqlTaskTags(tx, task);
            if (!occurrence.rewardGranted) {
                var profile = tx.Read<PlayerData>(PlayerRecordKind.Profile, "profile");
                var rewardDay = SqlRewardDay(tx, today);
                TaskService.CatLevel(profile.experience, out int level, out _, out _);
                var amount = SqlTaskAllowance(task.difficulty, rewardDay, level);
                SqlAddRewards(profile, amount);
                rewardDay.firstTaskSettled = true;
                rewardDay.taskExperience = checked(rewardDay.taskExperience + amount.experience);
                rewardDay.taskFishCoins = checked(rewardDay.taskFishCoins + amount.fishCoins);
                occurrence.rewardGranted = true;
                receipt = new TaskCompletion { id = receiptId, taskId = taskId, day = occurrence.day,
                    completedUtc = completedUtc, completedLocalDay = today, rewardedLocalDay = today,
                    snapshot = task.DetachedCopy(), tagNames = tags.Select(t => t.name).ToList(), tagColors = tags.Select(t => t.colorHex).ToList(),
                    fishCoins = amount.fishCoins, experience = amount.experience, tickets = amount.tickets };
                tx.Put(PlayerRecordKind.Profile, profile); tx.Put(PlayerRecordKind.RewardDays, rewardDay);
                tx.Put(PlayerRecordKind.History, receipt);
            } else if (receipt != null && receipt.revoked) {
                receipt.revoked = false; receipt.completedUtc = completedUtc; receipt.completedLocalDay = today;
                receipt.snapshot = task.DetachedCopy(); receipt.tagNames = tags.Select(t => t.name).ToList(); receipt.tagColors = tags.Select(t => t.colorHex).ToList();
                tx.Put(PlayerRecordKind.History, receipt);
            }
            tx.Put(PlayerRecordKind.Occurrences, occurrence);
            return receipt;
        });
    }

    public Task<OperationResult<bool>> UndoCompletionAsync(string historyId)
    {
        string today = TaskService.DateKey(clock.LocalNow);
        return ExecuteCommandAsync(tx => {
            var receipt = tx.Read<TaskCompletion>(PlayerRecordKind.History, historyId);
            if (receipt != null && receipt.revoked) return true;
            var task = receipt == null ? null : tx.Read<QuestTask>(PlayerRecordKind.Tasks, receipt.taskId);
            var occurrence = task == null ? null : SqlOccurrence(tx, task, today);
            if (receipt == null || task == null || receipt.day != SqlTaskDay(task, today) || !occurrence.completed)
                throw new PlayerCommandException("This completed task can no longer be restored.");
            occurrence.completed = false; receipt.revoked = true;
            tx.Put(PlayerRecordKind.Occurrences, occurrence); tx.Put(PlayerRecordKind.History, receipt); return true;
        });
    }

    public Task<OperationResult<string>> SaveTagAsync(string id, string name) => SqlSaveTagAsync(id, name, null, true);
    public Task<OperationResult<string>> SaveTagAsync(string id, string name, string colorHex) => SqlSaveTagAsync(id, name, colorHex, false);
    Task<OperationResult<string>> SqlSaveTagAsync(string id, string name, string colorHex, bool preserveColor)
    {
        name = (name ?? "").Trim(); string newId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
        return ExecuteCommandAsync(tx => {
            var tag = tx.Read<QuestTag>(PlayerRecordKind.Tags, newId);
            string requestedColor = preserveColor ? tag?.colorHex ?? TagColors.DefaultHex : colorHex;
            if (name.Length == 0 || name.Length > 30) throw new PlayerCommandException("Enter a tag name (1–30 characters).");
            if (!TagColors.TryNormalize(requestedColor, out string color)) throw new PlayerCommandException("Choose a valid tag color.");
            if (tx.Tags().Any(t => t.id != newId && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)))
                throw new PlayerCommandException("A tag with that name already exists.");
            if (tag == null && !string.IsNullOrEmpty(id)) throw new PlayerCommandException("Tag no longer exists.");
            tag ??= new QuestTag { id = newId }; tag.name = name; tag.colorHex = color;
            tx.Put(PlayerRecordKind.Tags, tag); return newId;
        });
    }
    public Task<OperationResult<bool>> DeleteTagAsync(string id) => ExecuteCommandAsync(tx => {
        if (tx.Read<QuestTag>(PlayerRecordKind.Tags, id) == null) throw new PlayerCommandException("Tag no longer exists.");
        foreach (var task in tx.ActiveTasks(id,true)) { task.tagIds.Remove(id); tx.Put(PlayerRecordKind.Tasks, task); }
        tx.Delete(PlayerRecordKind.Tags, id); return true;
    });

    public Task<OperationResult<string>> ConfirmChatDraftAsync(AiTaskDraft proposal)
    {
        if (proposal?.task == null || proposal.state != "pending")
            return Task.FromResult(new OperationResult<string> { Error = PlayerStorageError.InvalidData, Message = "This proposal is no longer pending." });
        string proposalDay = proposal.day, target = proposal.targetId, expected = proposal.expected, originalId = proposal.task.id;
        return SqlUpsertAsync(proposal.task, proposal.completed, (tx, task, today) => {
            if (proposalDay != today) throw new PlayerCommandException("The date changed. Request a fresh task proposal.");
            if (string.IsNullOrEmpty(originalId)) throw new PlayerCommandException("Missing proposal identity.");
            var old = tx.Read<QuestTask>(PlayerRecordKind.Tasks, task.id);
            if (string.IsNullOrEmpty(target)) {
                if (old != null) throw new PlayerCommandException("This task was already added. Open it in Tasks.");
            } else {
                if (target != task.id || old == null) throw new PlayerCommandException("The original task no longer exists.");
                var occurrence = SqlOccurrence(tx, old, today);
                if (occurrence.completed || occurrence.rewardGranted || AiChatProtocol.Fingerprint(old, occurrence) != expected)
                    throw new PlayerCommandException("The task changed or was settled. Request a fresh proposal.");
            }
        });
    }
}
