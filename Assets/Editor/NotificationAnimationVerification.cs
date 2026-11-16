using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Uses TaskTabsVerification's isolated save and Editor restoration, without asset writes.
public static class NotificationAnimationVerification
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static IEnumerator Observe(NotificationReveal reveal,Action show,RewardScreenshotCapture capture,string path,bool frames)
    {
        Check(reveal,"Missing shared notification animation");reveal.Settle();Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)reveal.transform;float full=rect.rect.width,height=rect.rect.height;
        var center=rect.TransformPoint(rect.rect.center);var label=reveal.GetComponentInChildren<TMP_Text>(true);float textWidth=label.rectTransform.rect.width;
        var widths=new List<float>();bool centered=true,stableText=true;
        Canvas.WillRenderCanvases sample=()=>{
            widths.Add(rect.rect.width);centered&=Vector3.Distance(center,rect.TransformPoint(rect.rect.center))<.1f&&Mathf.Abs(height-rect.rect.height)<.1f;
            stableText&=Mathf.Abs(textWidth-label.rectTransform.rect.width)<.1f;
        };
        Canvas.willRenderCanvases+=sample;
        try{
            show();
            if(frames){for(int frame=0;frame<8;frame++){capture.Begin(Path.GetFullPath(path+"-"+frame.ToString("D2")+".png"));while(capture.Pending)yield return .01;}}
            yield return .4;
        }finally{Canvas.willRenderCanvases-=sample;}
        Check(centered&&stableText,"Notice drifted, changed height or squeezed/wrapped text");
        Check(widths.Any(w=>w>full*.03f&&w<full*.99f),"No rendered expansion frames");
        Check(Mathf.Abs(full-rect.rect.width)<.1f&&reveal.Progress==1,"Notice did not restore its authored width");
        File.WriteAllText(path+".txt",DateTime.UtcNow.ToString("O")+"\nWidth trace: "+string.Join(",",widths));
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var canvas=reveal.GetComponentInParent<Canvas>().rootCanvas;
        foreach(var corner in corners){var p=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,corner);Check(p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height,"Notification outside viewport");}
        label.ForceMeshUpdate();Check(!label.isTextOverflowing,"Notification text clipped");
    }
    public static IEnumerator Run(bool before)
    {
        string folder="Library/NotificationAnimationVerification/"+(before?"before":"after");Directory.CreateDirectory(folder);
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        var capture=new GameObject("Notice capture").AddComponent<RewardScreenshotCapture>();
        var tasks=nav.taskSystem;
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        foreach(var size in before?new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}:new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
        foreach(bool expanded in new[]{false,true}){
            typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{size.x,size.y});
            toggle.isOn=expanded;Check(nav.ShowPage(MainMenuPage.Tasks),"Tasks unavailable");yield return .5;
            string suffix=size.x+"x"+size.y+(expanded?"-expanded":"-collapsed");
            tasks.Notify("Task completed. Daily task reward limit reached; no additional rewards.");yield return .4;
            if(!before){var notice=UnityEngine.Object.FindFirstObjectByType<TaskResultNotice>();var observation=Observe(notice.reveal,()=>notice.Show(notice.message.text),capture,folder+"/task-motion-"+suffix,size.x==1920&&!expanded);while(observation.MoveNext())yield return observation.Current;}
            capture.Begin(Path.GetFullPath(folder+"/task-"+suffix+".png"));while(capture.Pending)yield return .05;
            Check(nav.ShowPage(MainMenuPage.Focus),"Focus unavailable");yield return .4;
            var focus=UnityEngine.Object.FindFirstObjectByType<FocusPanelUI>();
            if(before){focus.allowance.text="Focus saved.";focus.allowanceNotice.SetActive(true);yield return .4;}
            else{
                var controller=focus.Controller;if(controller.Phase==FocusPhase.Resting)controller.EndRest();
                var start=controller.StartAsync(new FocusSessionOptions());double end=EditorApplication.timeSinceStartup+12;while(!start.IsCompleted&&EditorApplication.timeSinceStartup<end)yield return .01;
                Check(start.IsCompleted&&!start.IsFaulted&&start.Result,"Cannot start isolated Focus");controller.Advance(1);
                var finish=controller.EndEarlyAsync();end=EditorApplication.timeSinceStartup+12;while(!finish.IsCompleted&&EditorApplication.timeSinceStartup<end)yield return .01;
                Check(finish.IsCompleted&&!finish.IsFaulted&&finish.Result,"Cannot settle isolated Focus");yield return .35;
                Check(focus.allowance.text.Contains("Focus saved"),"Committed Focus notice missing");
                var observation=Observe(focus.allowanceReveal,()=>focus.allowanceReveal.Play(),capture,folder+"/focus-motion-"+suffix,size.x==1920&&!expanded);while(observation.MoveNext())yield return observation.Current;
                typeof(FocusPanelUI).GetMethod("RefreshSummary",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(focus,null);
                Check(focus.allowanceReveal.Progress==1,"Unchanged timer refresh restarted notification");
            }
            capture.Begin(Path.GetFullPath(folder+"/focus-"+suffix+".png"));while(capture.Pending)yield return .05;
            focus.allowanceNotice.SetActive(false);
            Check(nav.ShowPage(MainMenuPage.Tasks),"Return to Tasks unavailable");yield return .2;
            tasks.Notify("Could not save this task. Please try again.",true);yield return .4;
            if(!before){var popup=GameObject.Find("TaskConfirmDialog").transform.Find("Popup").GetComponent<NotificationReveal>();var observation=Observe(popup,()=>tasks.Notify("Could not save this task. Please try again.",true),capture,folder+"/error-motion-"+suffix,size.x==1920&&!expanded);while(observation.MoveNext())yield return observation.Current;}
            capture.Begin(Path.GetFullPath(folder+"/error-"+suffix+".png"));while(capture.Pending)yield return .05;
            var dialog=(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tasks);dialog.Dismiss();
        }
        if(!before){
            Check(nav.ShowPage(MainMenuPage.Focus),"Focus unavailable for error check");yield return .2;
            var focus=UnityEngine.Object.FindFirstObjectByType<FocusPanelUI>();if(focus.Controller.Phase==FocusPhase.Resting)focus.Controller.EndRest();
            using(var locked=new SQLite.SQLiteConnection(GameDataManager.Instance.SavePath)){
                locked.Execute("BEGIN IMMEDIATE");
                var start=focus.Controller.StartAsync();double end=EditorApplication.timeSinceStartup+12;while(!start.IsCompleted&&EditorApplication.timeSinceStartup<end)yield return .01;
                Check(start.IsCompleted&&!start.IsFaulted&&!start.Result,"Expected isolated locked-save failure");yield return 4.2;
                Check(focus.allowanceNotice.activeInHierarchy&&focus.allowanceReveal.Progress==1&&!string.IsNullOrEmpty(focus.Controller.ErrorMessage),"Persistent Focus failure expired or remained clipped");
                capture.Begin(Path.GetFullPath(folder+"/focus-persistent-error.png"));while(capture.Pending)yield return .05;
                locked.Execute("ROLLBACK");
            }
            var retry=focus.Controller.StartAsync();double retryEnd=EditorApplication.timeSinceStartup+12;while(!retry.IsCompleted&&EditorApplication.timeSinceStartup<retryEnd)yield return .01;
            Check(retry.IsCompleted&&!retry.IsFaulted&&retry.Result,"Focus retry failed after releasing test save");focus.Controller.Advance(1);
            var settle=focus.Controller.EndEarlyAsync();retryEnd=EditorApplication.timeSinceStartup+12;while(!settle.IsCompleted&&EditorApplication.timeSinceStartup<retryEnd)yield return .01;
            Check(settle.IsCompleted&&!settle.IsFaulted&&settle.Result,"Focus retry result did not save");
            yield return .35;Check(focus.allowance.text.Contains("Focus saved."),"Retry did not replace error with saved notification");
            Check(nav.ShowPage(MainMenuPage.Tasks),"Return to Tasks failed");yield return .2;
            tasks.Notify("Could not save this task. Please try again.",true);yield return .35;
            var dialog=(TaskConfirmDialog)typeof(TaskSystemUI).GetField("confirmation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tasks);
            var ok=dialog.Root.transform.Find("Popup/Confirm").GetComponent<Button>();
            ExecuteEvents.Execute(ok.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            Check(!dialog.Root.activeSelf,"Error notification OK keyboard dismissal failed");
            int confirmed=0;dialog.Show("Confirm","Confirmation behavior is unchanged.",()=>confirmed++);
            Check(dialog.Root.transform.Find("Popup").GetComponent<NotificationReveal>().Progress==1,"Confirmation unintentionally inherited notice animation");
            ok.onClick.Invoke();Check(confirmed==1&&!dialog.Root.activeSelf,"Confirmation callback changed");
            tasks.Notify("Task saved.");var notice=UnityEngine.Object.FindFirstObjectByType<TaskResultNotice>();yield return .08;
            notice.Show("Task updated.");Check(notice.reveal.Progress<.1f,"Replacement notice did not restart");yield return .35;
            Check(notice.message.text=="Task updated."&&notice.reveal.Progress==1,"Replacement notice failed to settle");
            notice.Dismiss();tasks.Notify("Task deleted.");yield return .1;notice.SendMessage("OnApplicationFocus",false);Check(!notice.gameObject.activeSelf,"Focus loss did not dismiss Task notice");
            tasks.Notify("Task saved.");yield return 4.2;Check(!notice.gameObject.activeSelf,"Task notice no longer expires after four seconds");
            var settings=tasks.Service.Settings;bool oldReduced=settings.reduceMotion;float oldTime=Time.timeScale;
            System.Threading.Tasks.Task<OperationResult<bool>> restore=null;
            try{
                settings.reduceMotion=true;var save=tasks.Service.SaveSettingsAsync(settings);double end=EditorApplication.timeSinceStartup+12;while(!save.IsCompleted&&EditorApplication.timeSinceStartup<end)yield return .01;Check(save.IsCompleted&&!save.IsFaulted&&save.Result.Succeeded,tasks.Service.LastError);tasks.Notify("Task saved.");Check(notice.reveal.Progress==1,"Reduced Motion did not reveal immediately");
                settings.reduceMotion=false;save=tasks.Service.SaveSettingsAsync(settings);end=EditorApplication.timeSinceStartup+12;while(!save.IsCompleted&&EditorApplication.timeSinceStartup<end)yield return .01;Check(save.IsCompleted&&!save.IsFaulted&&save.Result.Succeeded,tasks.Service.LastError);Time.timeScale=0;tasks.Notify("Task saved.");yield return .4;Check(notice.reveal.Progress==1,"Notice animation stopped at timeScale zero");
            }finally{Time.timeScale=oldTime;settings.reduceMotion=oldReduced;restore=tasks.Service.SaveSettingsAsync(settings);notice.Dismiss();}
            double restoreEnd=EditorApplication.timeSinceStartup+12;while(!restore.IsCompleted&&EditorApplication.timeSinceStartup<restoreEnd)yield return .01;Check(restore.IsCompleted&&!restore.IsFaulted&&restore.Result.Succeeded,"Restore motion setting failed");
            var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Frames/MessageFrame_Common_Blue.prefab"),tasks.GetComponentInParent<Canvas>().rootCanvas.transform);
            ((RectTransform)reference.transform).sizeDelta=new Vector2(620,72);tasks.Notify("Task saved.");yield return .4;
            capture.Begin(Path.GetFullPath(folder+"/source-reference.png"));while(capture.Pending)yield return .05;UnityEngine.Object.Destroy(reference);notice.Dismiss();
        }
        File.WriteAllText(folder+"/checks.txt",DateTime.UtcNow.ToString("O")+(before?" Before/reference captures.":" PASS: Task, actual Focus End Early and error notices; all four resolutions and both sidebar states; centered expansion, constant height and text width, intermediate rendered frames and exact settling, viewport/text containment, repeated messages, four-second expiry, focus-loss dismissal, unchanged refresh without replay, persistent locked-save error and retry, error OK keyboard dismissal, unchanged confirmation callback, Reduced Motion and unscaled animation. Isolated save; no authored asset writes."));
    }
}
