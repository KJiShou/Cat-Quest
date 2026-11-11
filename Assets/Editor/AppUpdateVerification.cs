using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CatQuest.Updates;

public static class AppUpdateVerification
{
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void State(AppUpdateController controller,AppUpdateState state,string message)
        => typeof(AppUpdateController).GetMethod("Set",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{state,message});
    public static IEnumerator Run(Action<Vector2Int> resolution,Action<string> capture,Action<Selectable> click)
    {
        var controller=AppUpdateController.Ensure();
        State(controller,AppUpdateState.Current,"You have the latest version.");
        var launcher=UnityEngine.Object.FindFirstObjectByType<SettingsLauncher>();
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var tutorial=UnityEngine.Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
        if(tutorial) tutorial.gameObject.SetActive(false);
        nav.ShowPage(MainMenuPage.Dashboard);
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
        {
            resolution(size);yield return 3;
            launcher.Open();yield return .3;
            var settings=UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>();
            var panel=settings.GetComponent<AppUpdatePanel>();
            Check(panel && panel.entry && panel.primary && panel.popup && panel.settingsContent,"Update bindings missing");
            capture("updates-settings-"+size.x);yield return .2;
            click(panel.entry);yield return .2;
            Check(panel.IsOpen && !panel.settingsContent.interactable,"Update modal did not isolate Settings");
            var dimFeedback=panel.backdrop.GetComponent<JuicyButton>();
            Check(dimFeedback,"Backdrop feedback coverage missing");
            var dimPointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            dimFeedback.OnPointerEnter(dimPointer);yield return .2;
            Check(dimFeedback.DrawScale==Vector2.one && dimFeedback.DrawOffset==0,"Backdrop moved on hover");
            dimFeedback.OnPointerDown(dimPointer);yield return .2;
            Check(dimFeedback.DrawScale==Vector2.one && dimFeedback.DrawOffset==0,"Backdrop shrank on press");
            capture("updates-backdrop-pressed-"+size.x);yield return .1;
            dimFeedback.OnPointerUp(dimPointer);dimFeedback.OnPointerExit(dimPointer);
            dimFeedback.OnSelect(new BaseEventData(EventSystem.current));yield return .2;
            Check(dimFeedback.DrawScale==Vector2.one && dimFeedback.DrawOffset==0,"Backdrop moved on keyboard selection");
            dimFeedback.OnDeselect(new BaseEventData(EventSystem.current));
            capture("updates-current-"+size.x);yield return .2;
            var vendor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Popup_Update.prefab"),settings.transform.parent);
            vendor.transform.SetAsLastSibling();yield return .2;capture("updates-reference-"+size.x);yield return .2;UnityEngine.Object.Destroy(vendor);
            typeof(AppUpdateController).GetProperty("Release").SetValue(controller,new ReleaseManifest{version="1.3",size=120*1048576,notes="A calmer workspace for your next quest.\n\n• Improvements to Focus and Pet Widget\n• Small fixes and polish\n\nYour tasks, equipment and progress stay with you."});
            State(controller,AppUpdateState.Available,"Cat Quest 1.3 is available.");yield return .2;
            capture("updates-available-"+size.x);yield return .2;
            foreach(var text in panel.popup.GetComponentsInChildren<TMP_Text>()) {text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Update text overflow: "+text.name);}
            var feedback=panel.primary.GetComponent<JuicyButton>();Check(feedback,"Shared button feedback missing");
            var original=((RectTransform)panel.primary.transform).rect;
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            Time.timeScale=0;
            ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerEnterHandler);yield return .25;
            Check(feedback.DrawScale.y>1,"Hover motion missing");capture("updates-hover-"+size.x);yield return .1;
            ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerDownHandler);yield return .25;
            Check(feedback.DrawScale.y<1,"Press motion missing");capture("updates-press-"+size.x);yield return .1;
            ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerExitHandler);yield return .6;
            Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.005f && ((RectTransform)panel.primary.transform).rect==original,"Motion did not settle or changed layout");
            feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus-loss reset failed");Time.timeScale=1;
            if(size.x==1280)
            {
                for(int frame=0;frame<30;frame++)
                {
                    if(frame==0) ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerEnterHandler);
                    if(frame==8) ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerDownHandler);
                    if(frame==16) ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerUpHandler);
                    if(frame==20) ExecuteEvents.Execute(panel.primary.gameObject,pointer,ExecuteEvents.pointerExitHandler);
                    capture("updates-motion-"+frame.ToString("D3"));yield return .04;
                }
            }
            State(controller,AppUpdateState.Checking,"Checking for updates...");
            ExecuteEvents.Execute(panel.primary.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            Check(controller.State==AppUpdateState.Checking,"Disabled primary submitted");
            State(controller,AppUpdateState.Available,"Update available.");
            controller.Release.notes=string.Join("\n",Enumerable.Repeat("Long release notes remain readable by scrolling.",50));panel.Refresh();yield return .2;
            var scroll=panel.popup.GetComponentInChildren<ScrollRect>();scroll.verticalNormalizedPosition=0;yield return .2;
            Check(scroll.content.rect.height>scroll.viewport.rect.height && scroll.verticalNormalizedPosition<.01f,"Release notes cannot scroll");
            controller.Release.notes="An update for your next quest.";panel.Refresh();yield return .2;
            EventSystem.current.SetSelectedGameObject(panel.later.gameObject);
            ExecuteEvents.Execute(panel.later.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            Check(!panel.IsOpen && panel.OwnsInput,"Keyboard dismissal did not consume the current input frame");yield return .2;
            Check(!panel.OwnsInput,"Dismissed modal retained input ownership");click(panel.entry);
            State(controller,AppUpdateState.Downloading,"Downloading update...");
            capture("updates-downloading-"+size.x);yield return .2;
            click(panel.later);Check(!panel.IsOpen && controller.State==AppUpdateState.Downloading,"Dismiss cancelled download");click(panel.entry);
            State(controller,AppUpdateState.Ready,"Downloaded and verified.");yield return .2;Check(!panel.primary.interactable,"Editor update install should be disabled");capture("updates-ready-"+size.x);yield return .2;
            State(controller,AppUpdateState.Error,"Download failed. Your installed version has not changed. Try again.");yield return .2;capture("updates-error-"+size.x);yield return .2;
            State(controller,AppUpdateState.Installing,"Preparing to restart...");panel.Dismiss();Check(panel.IsOpen && !panel.close.interactable,"Installing modal can be dismissed");
            State(controller,AppUpdateState.Current,"You have the latest version.");click(panel.close);Check(!panel.IsOpen && EventSystem.current.currentSelectedGameObject==panel.entry.gameObject,"Close did not restore focus");
            settings.Close();yield return .2;
        }
    }
}
