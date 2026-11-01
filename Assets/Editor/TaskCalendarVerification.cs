using System;
using System.Linq;
using UnityEngine;

[UnityEditor.InitializeOnLoad]
public static class TaskCalendarVerification
{
    static TaskCalendarVerification() { UnityEditor.EditorApplication.update += () => {
        if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists("Temp/verify-calendar-domain.request")) return;
        System.IO.File.Delete("Temp/verify-calendar-domain.request");
        try { VerifyBindings(); RewardSystemVerification.Run(); System.IO.File.WriteAllText("Temp/verify-calendar-domain.result", "PASS " + DateTime.UtcNow.ToString("O")); }
        catch (Exception e) { System.IO.File.WriteAllText("Temp/verify-calendar-domain.result", e.ToString()); }
    }; }
    sealed class Clock : ITaskClock { public DateTime now = new DateTime(2026, 11, 1, 12, 0, 0); public DateTime LocalNow => now; public DateTime UtcNow => now.AddHours(-8); }
    sealed class Store : IPlayerDataStore
    {
        public PlayerData Data { get; private set; } = new PlayerData(); public int writes; public bool fail;
        public bool TrySave(PlayerData data, out string error) { error = fail ? "Calendar test failure" : null; if (fail) return false; Data = data; writes++; return true; }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception("Calendar: " + message); }
    public static void VerifyBindings()
    {
        var canvas = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        var owner = canvas.GetComponentInChildren<TaskSystemUI>(true);var calendar=owner.calendar;
        Check(calendar && calendar.days.Length==42 && calendar.drawerAdd && calendar.rowTemplate && calendar.detailRoot && calendar.jumpPicker, "missing serialized calendar binding");
        Check(calendar.listMode && calendar.calendarMode && calendar.listHeader && calendar.listBody, "missing Canvas view binding");
        Check(calendar.selectedViewSprite && calendar.neutralButtonSprite && calendar.todayButtonSprite, "missing calendar state artwork");
        Check(owner.tabButtons.Length==5 && calendar.listHeader.transform.IsChildOf(owner.transform), "existing tabs changed");
        foreach(var item in UnityEditor.AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Task/Calendar"}))
        {
            var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(item));
            foreach(var transform in asset.GetComponentsInChildren<Transform>(true))Check(UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)==0,"missing script in "+asset.name);
        }
    }
    public static void Run()
    {
        var clock = new Clock(); var store = new Store(); var service = new TaskService(store, clock);
        Check(service.Upsert(new QuestTask { title = "Scheduled", scheduled = true, date = "2026-11-03" }, null, out var todo), service.LastError);
        Check(service.Upsert(new QuestTask { title = "No date" }, null, out var unscheduled), service.LastError);
        Check(service.Upsert(new QuestTask { title = "Daily", daily = true, scheduled = true, date = "2026-11-01" }, null, out var daily), service.LastError);
        Check(service.Upsert(new QuestTask { title = "Future daily", daily = true, scheduled = true, date = "2026-11-10" }, null, out var future), service.LastError);
        string before = JsonUtility.ToJson(service.Snapshot); int writes = store.writes;
        var result = service.QueryCalendar(new DateTime(2026, 10, 26), new DateTime(2026, 12, 6));
        Check(result.days.Count == 42 && result.noDate.Single().view.task.id == unscheduled, "range/no-date grouping");
        Check(result.days[new DateTime(2026, 11, 3)].items.Any(i => i.view.task.id == todo), "Todo scheduled date");
        Check(result.days[new DateTime(2026, 11, 9)].items.All(i => i.view.task.id != future), "future start eligibility");
        Check(result.days[new DateTime(2026, 11, 10)].items.Single(i => i.view.task.id == future).kind == CalendarTaskKind.Preview, "future preview kind");
        Check(result.days[new DateTime(2026, 10, 31)].items.Count == 0, "fabricated past Daily");
        Check(store.writes == writes && JsonUtility.ToJson(service.Snapshot) == before, "browsing wrote data");
        Check(service.Complete(todo, true) && service.Complete(daily, true), service.LastError);
        result = service.QueryCalendar(clock.now.Date, clock.now.Date.AddDays(14));
        Check(result.days[clock.now.Date.AddDays(2)].items.Single(i => i.view.task.id == todo).kind == CalendarTaskKind.History, "receipt must stay on scheduled date");
        Check(result.days[clock.now.Date].items.Count(i => i.view.task.id == daily) == 1, "duplicate completed Daily");
        Check(service.QueryCalendar(clock.now.Date, clock.now.Date.AddDays(14), showCompleted: false).days[clock.now.Date].items.Count == 0, "completed toggle");
        var receipt = service.Snapshot.history.Single(h => h.taskId == daily); var balances = service.Snapshot;
        Check(service.UndoCompletion(receipt.id), service.LastError);
        Check(service.QueryCalendar(clock.now.Date, clock.now.Date).days[clock.now.Date].items.Single().kind == CalendarTaskKind.Task, "revoked receipt duplicated live occurrence");
        store.fail = true; Check(!service.Complete(daily, true), "failed save accepted"); store.fail = false;
        Check(service.Complete(daily, true) && service.Complete(daily, true), service.LastError);
        Check(service.Snapshot.experience == balances.experience && service.Snapshot.fishCoins == balances.fishCoins, "recompletion paid twice");
        Check(service.Delete(daily), service.LastError);
        result = service.QueryCalendar(clock.now.Date, clock.now.Date.AddDays(14));
        Check(result.days[clock.now.Date].items.Any(i => i.view.task.id == daily) && result.days[clock.now.Date.AddDays(1)].items.All(i => i.view.task.id != daily), "deleted template history/preview");
        Check(service.Upsert(new QuestTask { title = "Missed", daily = true, scheduled = true, date = "2026-11-01" }, null, out _), service.LastError);
        clock.now = clock.now.AddDays(2); service.RefreshDate();
        result = service.QueryCalendar(new DateTime(2026,11,1), new DateTime(2026,11,3));
        Check(result.days[new DateTime(2026,11,1)].missed == 1 && result.days[new DateTime(2026,11,2)].missed == 0, "missed/offline gap");
        Check(service.QueryCalendar(new DateTime(2026,11,1),new DateTime(2026,11,3),"Missed").days[new DateTime(2026,11,1)].missed == 0, "invented historical filter metadata");
        foreach (var date in new[] { new DateTime(2024,2,1), new DateTime(2025,2,1), new DateTime(2026,4,1), new DateTime(2026,12,1) })
            Check(service.QueryCalendar(date,date.AddMonths(1).AddDays(-1)).days.Count == DateTime.DaysInMonth(date.Year,date.Month), "month length");
        Check(service.QueryCalendar(DateTime.MinValue,DateTime.MinValue).days.Count == 1 && service.QueryCalendar(DateTime.MaxValue.Date,DateTime.MaxValue.Date).days.Count == 1, "date boundary overflow");
        before=JsonUtility.ToJson(service.Snapshot); writes=store.writes;
        service.QueryCalendar(clock.now.Date,clock.now.Date.AddDays(41),"missing",TaskDifficulty.Hard,new[]{"missing-tag"});
        Check(before==JsonUtility.ToJson(service.Snapshot)&&writes==store.writes,"filtered query mutated save");
    }
}
