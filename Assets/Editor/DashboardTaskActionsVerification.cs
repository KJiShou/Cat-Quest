using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs only inside DashboardAddVerification's isolated save and restored Editor session.
public static class DashboardTaskActionsVerification
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static DashboardTaskRow Row(DashboardTasksUI owner, string title) => owner.GetComponentsInChildren<DashboardTaskRow>()
        .Single(r => r.taskTitle.text == title);
    static TaskContextMenu Menu => UnityEngine.Object.FindFirstObjectByType<TaskContextMenu>();
    static void RightClick(Component target)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)target.transform;
        var pointer = new PointerEventData(EventSystem.current) { button=PointerEventData.InputButton.Right,
            position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)) };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0,"Dashboard right click has no hit");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
        Check(Menu,"Dashboard right click did not open menu");
    }
    public static IEnumerator Run(DashboardTasksUI owner, MainMenuNavigation nav, Action<int,int> resolution, Action<string> capture)
    {
        var manager=GameDataManager.EnsureInstance();var service=manager.Service;
        foreach(var wait in TaskCalendarPlayVerification.Await(manager.Ready))yield return wait;
        Check(manager.SavePath.Contains("DashboardAddVerification"),"Actions test requires isolated save");
        string id=null;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.UpsertAsync(new QuestTask { title="! Dashboard daily actions",daily=true,date=TaskService.DateKey(service.LocalNow),
            subtasks=new System.Collections.Generic.List<QuestSubtask>{new QuestSubtask{id="step",title="Keep progress"}} },Array.Empty<string>()),value=>id=value))yield return wait;
        foreach(var wait in TaskCalendarPlayVerification.Await(service.CompleteAsync(id,true)))yield return wait;
        int xp=service.Experience,coins=service.FishCoins,tickets=service.Tickets;
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
        var row=Row(owner,"! Dashboard daily actions");Check(row.completeButton.interactable&&row.tick.activeSelf,"Completed Daily checkbox cannot reopen");
        using(TaskCalendarPlayVerification.FailSqliteWrites()) { row.completeButton.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;Check(service.Query(TaskTab.Daily).Single(t=>t.task.id==id).Completed,"Failed undo changed task"); }
        var dialog=(TaskConfirmDialog)typeof(DashboardTasksUI).GetField("confirmation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner);
        dialog.Dismiss();
        row.completeButton.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .4;
        Check(!service.Query(TaskTab.Daily).Single(t=>t.task.id==id).Completed,"Dashboard undo failed");
        var loaded=SqlitePlayVerificationSupport.ReloadAsync();
        foreach(var wait in SqlitePlayVerificationSupport.Wait(loaded))yield return wait;
        Check(!loaded.GetAwaiter().GetResult().occurrences.Single(o=>o.taskId==id).completed,"Undo missing on reload");
        Row(owner,"! Dashboard daily actions").completeButton.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .4;
        Check(service.Query(TaskTab.Daily).Single(t=>t.task.id==id).Completed,"Dashboard re-completion failed");
        Check(service.Experience==xp&&service.FishCoins==coins&&service.Tickets==tickets,"Re-completion paid twice");
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
        {
            resolution(size.x,size.y);yield return .5;
            owner.taskScroll.verticalNormalizedPosition=1;yield return .3;
            row=Row(owner,"! Dashboard daily actions");RightClick(row.completeButton);yield return .3;
            Check(!Menu.complete.interactable,"Completed task can complete from menu");
            capture("dashboard-context-"+size.x);yield return .2;
            Menu.difficulty.onClick.Invoke();Menu.difficultyOptions[(int)TaskDifficulty.Hard].onClick.Invoke();Menu.save.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
            Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).difficulty==TaskDifficulty.Hard,"Dashboard difficulty update failed");
            RightClick(Row(owner,"! Dashboard daily actions"));Menu.tags.onClick.Invoke();yield return .3;
            var tag=Menu.tagContent.GetComponentsInChildren<TaskContextTagRow>().First();tag.button.onClick.Invoke();
            capture("dashboard-context-tags-"+size.x);yield return .2;Menu.save.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
            Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).tagIds.Contains(tag.TagId)==(size.x==1920),"Dashboard tags update failed");
            RightClick(Row(owner,"! Dashboard daily actions"));Menu.schedule.onClick.Invoke();yield return .3;
            Menu.tomorrow.onClick.Invoke();Menu.save.onClick.Invoke();foreach(var wait in TaskCalendarPlayVerification.Await(service.FlushAsync()))yield return wait;yield return .3;
            Check(service.TaskSnapshot.tasks.Single(t=>t.id==id).date==TaskService.DateKey(service.LocalNow.AddDays(1)),"Dashboard schedule update failed");
            // A Daily moved to tomorrow leaves Dashboard. Restore the fixture through the service.
            var draft=service.TaskSnapshot.tasks.Single(t=>t.id==id);draft.date=TaskService.DateKey(service.LocalNow);
            foreach(var wait in TaskCalendarPlayVerification.Await(service.UpsertAsync(draft,new[]{"step"})))yield return wait;yield return .3;
            RightClick(Row(owner,"! Dashboard daily actions"));Menu.delete.onClick.Invoke();yield return .2;
            Check(dialog.Root.activeSelf,"Dashboard delete lacks confirmation");dialog.Dismiss();
            Check(service.TaskSnapshot.tasks.Any(t=>t.id==id),"Cancel deleted task");
            RightClick(Row(owner,"! Dashboard daily actions"));nav.ShowPage(MainMenuPage.Tasks);yield return .2;
            Check(!Menu,"Page closure left Dashboard menu open");nav.ShowPage(MainMenuPage.Dashboard);yield return .4;
        }
        RightClick(Row(owner,"! Dashboard daily actions"));Menu.edit.onClick.Invoke();yield return .5;
        Check(nav.CurrentPage==MainMenuPage.Tasks&&nav.taskSystem.IsEditing,"Dashboard Edit did not open real task editor");
        File.WriteAllText("Library/DashboardAddVerification/task-actions.txt",DateTime.UtcNow.ToString("O")+" PASS: completed Daily undo, failed-save retry, reload, no repeat rewards; right-click row and checkbox, difficulty, tags, schedule, delete cancel, page-close cleanup and edit navigation at 1920x1080/1280x720.");
    }
}
