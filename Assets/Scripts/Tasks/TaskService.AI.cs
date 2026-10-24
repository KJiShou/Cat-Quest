using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class TaskService
{
    public List<TaskView> QueryForChat(string scope, string value)
    {
        if (!RefreshDate()) throw new ArgumentException(LastError);
        if (scope == "today") return Query(TaskTab.Today);
        var snapshot = Snapshot;
        var rows = snapshot.tasks.Select(task => new TaskView { task = task,
            occurrence = snapshot.occurrences.FirstOrDefault(o => o.taskId == task.id && o.day == OccurrenceDay(task, Today)) ??
                new TaskOccurrence { taskId = task.id, day = OccurrenceDay(task, Today) } }).Where(v => !v.Completed).ToList();
        if (scope == "search" && !string.IsNullOrWhiteSpace(value)) return rows.Where(v =>
            ((v.task.title ?? "") + " " + v.task.description).IndexOf(value.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (scope == "id" && !string.IsNullOrWhiteSpace(value)) return rows.Where(v => v.task.id == value).ToList();
        if (scope == "date" && TryDate(value, out var date))
            return rows.Where(v => v.task.daily ? TryDate(v.task.date, out var start) && start.Date <= date.Date :
                v.task.scheduled && v.task.date == DateKey(date)).ToList();
        throw new ArgumentException("Specify today, a date, a task name or a task ID.");
    }
    public bool ConfirmChatDraft(AiTaskDraft proposal, out string savedId)
    {
        savedId = null;
        if (proposal == null || proposal.task == null || proposal.state != "pending") { LastError = "This proposal is no longer pending."; return false; }
        if (!RefreshDate()) return false;
        return UpsertChecked(proposal.task, proposal.completed, data => {
            if (proposal.day != Today) throw new ArgumentException("The date changed. Request a fresh task proposal.");
            if (string.IsNullOrEmpty(proposal.task.id)) throw new ArgumentException("Missing proposal identity.");
            var old = data.tasks.FirstOrDefault(t => t.id == proposal.task.id);
            if (string.IsNullOrEmpty(proposal.targetId)) {
                if (old != null) throw new ArgumentException("This task was already added. Open it in Tasks.");
            } else {
                if (proposal.targetId != proposal.task.id || old == null) throw new ArgumentException("The original task no longer exists.");
                var occurrence = GetOccurrence(data, old, Today);
                if (occurrence.completed || occurrence.rewardGranted || AiChatProtocol.Fingerprint(old, occurrence) != proposal.expected)
                    throw new ArgumentException("The task changed or was settled. Request a fresh proposal.");
            }
        }, out savedId);
    }
}
