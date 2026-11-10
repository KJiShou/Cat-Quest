using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class RemainingPresentationVerificationChecks
{
    public static IEnumerator Run(MainMenuNavigation nav,GameDataManager manager,bool before,string output,Action<bool,string> check)
    {
        RemainingFixesVerification.Resolution(1920,1080);yield return .3;
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        bool priorToggle=toggle.isOn;
        string metrics=output+"/preview-resize.csv";
        File.WriteAllText(metrics,"page,transitions,renderedFrames,targetReplacements,largestFrameSeconds,editorMeasurement\n");
        try {
            if(!before) {
                var ownership=SharedCameraLifecycle(nav,output,check);
                try {while(ownership.MoveNext())yield return ownership.Current;}
                finally {(ownership as IDisposable)?.Dispose();}
            }
            foreach(var page in new[]{MainMenuPage.CatHome,MainMenuPage.Focus,MainMenuPage.Boss}) {
                check(nav.ShowPage(page),"Open "+page+" for preview measurement");yield return 2;
                var container=nav.pages[(int)page];
                RawImage view=page==MainMenuPage.CatHome?container.GetComponentInChildren<CharacterHomePreview>().GetComponent<RawImage>()
                    :page==MainMenuPage.Focus?container.GetComponentInChildren<FocusScenePreview>().GetComponent<RawImage>()
                    :container.GetComponentInChildren<BossArenaPreview>().GetComponent<RawImage>();
                check(view&&view.texture is RenderTexture,"Preview target exists for "+page);
                toggle.isOn=false;yield return .5;
                int replacements=0,frames=0,lastFrame=-1;Texture last=view.texture;float peak=0;
                Canvas.WillRenderCanvases sample=()=>{
                    if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;frames++;
                    peak=Mathf.Max(peak,Time.unscaledDeltaTime);
                    if(view.texture!=last){replacements++;last=view.texture;}
                };
                Canvas.willRenderCanvases+=sample;
                try {for(int i=0;i<4;i++){toggle.isOn=!toggle.isOn;yield return .65;}}
                finally {Canvas.willRenderCanvases-=sample;}
                File.AppendAllText(metrics,string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},4,{1},{2},{3:F6},true\n",page,frames,replacements,peak));
                check(frames>4,"Measured real rendered preview frames for "+page);
                if(!before)check(replacements<=5,"Preview resize settles with at most one replacement per sidebar transition for "+page);
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}) {
                    RemainingFixesVerification.Resolution(size.x,size.y);yield return .5;
                    var shot=RemainingFixesVerification.Capture("presentation-"+page+"-"+size.x);
                    while(shot.MoveNext())yield return shot.Current;
                }
                RemainingFixesVerification.Resolution(1920,1080);yield return .3;
            }
            var launcher=UnityEngine.Object.FindFirstObjectByType<SettingsLauncher>();launcher.Open();yield return .3;
            var settings=UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>();
            foreach(int width in new[]{1920,1280}) {
                RemainingFixesVerification.Resolution(width,width==1920?1080:720);yield return .3;
                var shot=RemainingFixesVerification.Capture("settings-motion-"+width);while(shot.MoveNext())yield return shot.Current;
            }
            settings.Close();
            if(!before) {
                var checks=MotionSettings(nav,manager,launcher,settings,output,check);
                while(checks.MoveNext())yield return checks.Current;
            }
        }finally {toggle.isOn=priorToggle;}
    }
    // Can also be enumerated alone by the existing isolated runner. These checks
    // observe actual SRP camera renders; minimization itself remains a native test.
    public static IEnumerator SharedCameraLifecycle(MainMenuNavigation nav,string output,Action<bool,string> check)
    {
        var priorPage=nav.CurrentPage;
        var home=nav.pages[(int)MainMenuPage.CatHome].GetComponentInChildren<CharacterHomePreview>(true);
        check(home&&home.previewCamera,"Cat Home has its authored external forest camera");
        var camera=home.previewCamera;
        var dashboard=nav.pages[(int)MainMenuPage.Dashboard].GetComponentsInChildren<CharacterHomePreview>(true)
            .SingleOrDefault(preview=>preview.previewCamera==camera);
        check(dashboard&&dashboard!=home,"Dashboard and Cat Home use the same real external camera");
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        int rendered=0;
        Action<ScriptableRenderContext,Camera> sample=(context,current)=>{if(current==camera)rendered++;};
        string trace=Path.Combine(output,"shared-camera-lifecycle.csv");
        File.WriteAllText(trace,"cycle,page,enabled,renderedFrames,targetRestored\n");
        RenderPipelineManager.beginCameraRendering+=sample;
        try {
            check(nav.ShowPage(MainMenuPage.Dashboard),"Activate the authored Dashboard camera owner");yield return .2;
            object binding=typeof(CharacterHomePreview).GetField("cameraBinding",hidden).GetValue(dashboard);
            check(binding!=null,"The authored external camera has a shared lifecycle binding");
            var originalTarget=(RenderTexture)binding.GetType().GetField("originalTarget").GetValue(binding);
            check((bool)binding.GetType().GetField("originalEnabled").GetValue(binding),"The real forest camera starts from its authored enabled state");
            foreach(int cycle in new[]{0,1}) {
                foreach(var page in new[]{MainMenuPage.Dashboard,MainMenuPage.Focus,MainMenuPage.CatHome,MainMenuPage.Focus}) {
                    check(nav.ShowPage(page),"Shared camera navigation reaches "+page+" in cycle "+cycle);
                    rendered=0;yield return .25;
                    bool hiddenPage=page==MainMenuPage.Focus;
                    bool restored=camera.targetTexture==originalTarget;
                    File.AppendAllText(trace,$"{cycle},{page},{camera.enabled},{rendered},{restored}\n");
                    if(hiddenPage) {
                        check(!dashboard.isActiveAndEnabled&&!home.isActiveAndEnabled&&!camera.enabled&&rendered==0,
                            "Focus does not render the hidden shared forest camera after either owner closes (cycle "+cycle+")");
                        check(restored,"Closing all forest owners restores the original camera target");
                    } else {
                        var owner=page==MainMenuPage.Dashboard?dashboard:home;
                        check(owner.isActiveAndEnabled&&camera.enabled&&rendered>0,
                            page+" reacquires real camera rendering after repeated navigation");
                        check(owner.GetComponent<RawImage>().texture is RenderTexture&&camera.targetTexture==owner.GetComponent<RawImage>().texture,
                            page+" renders into its own retained preview target");
                    }
                }
            }
            foreach(bool enabled in new[]{false,true}) foreach(int first in new[]{0,1}) {
                var fixture=CameraOwners(enabled,first,check);
                try {while(fixture.MoveNext())yield return fixture.Current;}
                finally {(fixture as IDisposable)?.Dispose();}
            }
        } finally {
            RenderPipelineManager.beginCameraRendering-=sample;
            nav.ShowPage(priorPage);
        }
    }
    static CharacterHomePreview CameraOwner(Transform parent,Camera camera,SpriteRenderer character,string name)
    {
        // No Canvas: this transient binding fixture never appears in application UI.
        var root=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));
        root.SetActive(false);root.transform.SetParent(parent,false);
        ((RectTransform)root.transform).sizeDelta=new Vector2(160,120);
        var preview=root.AddComponent<CharacterHomePreview>();
        preview.previewCamera=camera;preview.character=character;
        return preview;
    }
    static IEnumerator CameraOwners(bool originalEnabled,int destroyFirst,Action<bool,string> check)
    {
        var root=new GameObject("Isolated shared preview ownership");
        var cameraObject=new GameObject("Isolated preview camera");cameraObject.transform.SetParent(root.transform);
        var camera=cameraObject.AddComponent<Camera>();camera.cullingMask=0;
        var original=new RenderTexture(16,16,0){name="Isolated original camera target"};original.Create();
        camera.targetTexture=original;camera.enabled=originalEnabled;
        var model=new GameObject("Isolated preview character");model.transform.SetParent(root.transform);
        var character=model.AddComponent<SpriteRenderer>();
        var owners=new[]{CameraOwner(root.transform,camera,character,"Owner A"),CameraOwner(root.transform,camera,character,"Owner B")};
        string context=$" (original enabled={originalEnabled}, destroy first={destroyFirst})";
        try {
            owners[0].gameObject.SetActive(true);yield return .05;
            owners[1].gameObject.SetActive(true);yield return .05;
            var targets=owners.Select(owner=>owner.GetComponent<RawImage>().texture as RenderTexture).ToArray();
            check(targets.All(target=>target&&target.IsCreated())&&targets[0]!=targets[1],"Two camera owners retain distinct valid targets"+context);
            check(camera.enabled==originalEnabled,"Acquiring owners preserves an originally disabled camera"+context);
            // Force the outgoing target to be current, independently of Unity's
            // unspecified ordering between two active LateUpdate callbacks.
            foreach(int outgoing in new[]{0,1}) {
                int remaining=1-outgoing;camera.targetTexture=targets[outgoing];
                owners[outgoing].enabled=false;
                check(camera.enabled==originalEnabled&&camera.targetTexture==targets[remaining],
                    "Disabling the current target owner hands rendering to the other active owner"+context+" / "+outgoing);
                owners[outgoing].enabled=true;yield return .05;
                check(owners[outgoing].GetComponent<RawImage>().texture==targets[outgoing],"Re-enable reuses the owner's retained target"+context+" / "+outgoing);
            }
            // Exercise both last-disable orders without destroying the binding.
            owners[destroyFirst].enabled=false;owners[1-destroyFirst].enabled=false;
            check(!camera.enabled&&camera.targetTexture==original,"No active owners means no rendering and the original target"+context);
            owners[1-destroyFirst].enabled=true;owners[destroyFirst].enabled=true;yield return .05;
            check(camera.enabled==originalEnabled,"Reversed reacquisition preserves the original enable state"+context);
            camera.targetTexture=targets[destroyFirst];
            UnityEngine.Object.Destroy(owners[destroyFirst].gameObject);yield return .05;
            int survivor=1-destroyFirst;
            check(camera.enabled==originalEnabled&&camera.targetTexture==targets[survivor]&&targets[survivor].IsCreated(),
                "Destroying either current owner leaves the surviving owner's target usable"+context);
            UnityEngine.Object.Destroy(owners[survivor].gameObject);yield return .05;
            check(camera.enabled==originalEnabled&&camera.targetTexture==original&&original.IsCreated(),
                "Destroying the last owner restores the camera's exact original target and enabled state"+context);
            var bindings=(IDictionary)typeof(CharacterHomePreview).GetField("sharedCameras",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            check(!bindings.Contains(camera),"Destroyed preview owners leave no stale camera binding"+context);
        } finally {
            root.SetActive(false);
            foreach(var owner in owners)if(owner)UnityEngine.Object.Destroy(owner.gameObject);
            camera.targetTexture=null;camera.enabled=false;
            UnityEngine.Object.Destroy(root);original.Release();UnityEngine.Object.Destroy(original);
        }
    }
    static IEnumerable<object> Capture(string name)
    {
        var run=RemainingFixesVerification.Capture(name);
        while(run.MoveNext())yield return run.Current;
    }
    static void ClickToggle(Toggle toggle,Action<bool,string> check)
    {
        Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)toggle.transform;
        var canvas=toggle.GetComponentInParent<Canvas>().rootCanvas;
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,
            position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center))};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Toggle>()==toggle,"Pointer raycast reaches Reduce motion without overlap");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator MotionSettings(MainMenuNavigation nav,GameDataManager manager,SettingsLauncher launcher,SettingsPanelUI settings,string output,Action<bool,string> check)
    {
        string isolated=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        check(!string.IsNullOrEmpty(isolated)&&Path.GetFullPath(isolated)==Path.GetFullPath(manager.SavePath),"Motion checks use the runner's isolated store");
        var service=manager.Tasks;var prior=service.Settings;var priorPage=nav.CurrentPage;
        string fixture=null;
        try {
            string legacyPath=Path.Combine(output,"legacy-motion-default.json");
            File.WriteAllText(legacyPath,"{\"version\":5,\"settings\":{\"soundVolume\":0.4,\"musicVolume\":0.7}}");
            var legacy=new PlayerDataStore(legacyPath).Data;
            check(!legacy.settings.reduceMotion&&Mathf.Approximately(legacy.settings.soundVolume,.4f),"Older saves default to original motion and retain unrelated settings");
            var normal=service.Settings;normal.reduceMotion=false;check(service.SaveSettings(normal),"Start motion checks with normal feedback");
            launcher.Open();yield return .2;
            check(settings.reduceMotion&&settings.reduceMotion.gameObject.activeInHierarchy&&!settings.reduceMotion.isOn,"Reduce motion is authored, bound and initialized from saved settings");
            var motionRow=(RectTransform)settings.reduceMotion.transform.parent;
            check(motionRow.name=="ReduceMotion"&&motionRow.parent==settings.settingsPage.transform.Find("Popup")
                && motionRow.sizeDelta==new Vector2(330,48)&&motionRow.anchoredPosition==new Vector2(350,30),"Motion setting occupies the dedicated right-column row");
            check(settings.settingsPage.GetComponentsInChildren<TMP_Text>().Any(text=>text.text=="Show on Dashboard"),"Existing dashboard visibility heading is retained");
            foreach(int width in new[]{1920,1280}) {
                RemainingFixesVerification.Resolution(width,width==1920?1080:720);yield return .3;
                ClickToggle(settings.reduceMotion,check);yield return .15;
                check(settings.reduceMotion.isOn&&service.ReduceMotion&&UiMotion.Reduced,"Pointer change commits Reduce motion at "+width);
                var visual=settings.reduceMotion.GetComponent<SettingsToggleVisual>();
                check(Mathf.Approximately(visual.handle.anchoredPosition.x,visual.travel),"Reduce-motion toggle settles directly to its saved on state");
                foreach(var delay in Capture("settings-reduce-motion-on-"+width))yield return delay;
                settings.Close();launcher.Open();yield return .15;
                check(settings.reduceMotion.isOn&&new PlayerDataStore(manager.SavePath).Data.settings.reduceMotion,"Reduce motion survives modal reopening and disk reload");
                EventSystem.current.SetSelectedGameObject(settings.reduceMotion.gameObject);
                ExecuteEvents.Execute(settings.reduceMotion.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return .15;
                check(!settings.reduceMotion.isOn&&!service.ReduceMotion,"Keyboard submit toggles the authored motion preference");
                check(settings.reduceMotion.navigation.selectOnUp&&settings.reduceMotion.navigation.selectOnDown,
                    "Reduce motion participates in the existing Settings focus order");
            }
            using(var locked=File.Open(manager.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                ClickToggle(settings.reduceMotion,check);yield return .15;
                check(!settings.reduceMotion.isOn&&!UiMotion.Reduced&&!string.IsNullOrEmpty(service.LastError)
                    &&settings.settingsStatus.text==service.LastError,"Failed motion save restores the committed toggle and keeps the error visible");
            }
            ClickToggle(settings.reduceMotion,check);yield return .15;
            check(settings.reduceMotion.isOn&&UiMotion.Reduced&&settings.settingsStatus.text=="Settings saved","Motion preference retries successfully after a write failure");
            settings.Close();
            var buttons=ButtonMotion(nav,service,output,check);
            while(buttons.MoveNext())yield return buttons.Current;
            string date=TaskService.DateKey(service.LocalNow.Date.AddDays(-1));
            check(service.Upsert(new QuestTask{title="Review date readability",scheduled=true,date=date,difficulty=TaskDifficulty.Normal},null,out fixture),"Create an isolated dated task for rendered review");
            check(nav.ShowPage(MainMenuPage.Tasks),"Open Tasks for date readability checks");
            yield return .3; // First activation runs TaskSystemUI.Start before row interactions.
            var tasks=nav.taskSystem;
            if(tasks.calendar.Active)tasks.calendar.SetMode(TaskDisplayMode.List);
            tasks.SelectTab(TaskTab.Todo);tasks.SelectTask(fixture);yield return .3;
            foreach(int width in new[]{1920,1280}) {
                RemainingFixesVerification.Resolution(width,width==1920?1080:720);yield return .3;
                var dates=tasks.listContent.GetComponentsInChildren<TMP_Text>().Where(text=>text.name=="DateTime").ToArray();
                check(dates.Length>0&&dates.All(text=>text.fontSize==20&&!text.enableAutoSizing),"Visible task dates retain their authored 20-point minimum at "+width);
                foreach(var delay in Capture("task-dates-"+width))yield return delay;
                tasks.calendar.SetMode(TaskDisplayMode.Calendar);
                tasks.calendar.SelectDate(service.LocalNow.Date.AddDays(-1));yield return .3;
                var calendarDates=tasks.calendar.rows.GetComponentsInChildren<TMP_Text>().Where(text=>text.name=="DateTime").ToArray();
                check(calendarDates.Length>0&&calendarDates.All(text=>text.fontSize==20&&!text.enableAutoSizing),"Calendar task dates use the same authored size at "+width);
                foreach(var delay in Capture("calendar-task-dates-"+width))yield return delay;
                tasks.calendar.CloseDrawer();tasks.calendar.SetMode(TaskDisplayMode.List);yield return .15;
            }
        }finally {
            if(settings&&settings.gameObject.activeSelf)settings.Close();
            if(fixture!=null)check(service.Delete(fixture),"Remove only the isolated date fixture");
            check(service.SaveSettings(prior),"Restore isolated presentation settings");
            nav.ShowPage(priorPage);
        }
    }
    static IEnumerator ButtonMotion(MainMenuNavigation nav,TaskService service,string output,Action<bool,string> check)
    {
        var button=nav.buttons[(int)MainMenuPage.Tasks];var feedback=button.GetComponent<JuicyButton>();
        check(feedback&&feedback.enabled,"Existing shared JuicyButton is available for motion regression");
        var rect=(RectTransform)button.transform;Vector2 size=rect.sizeDelta,position=rect.anchoredPosition;
        Vector3 scale=rect.localScale;float priorScale=Time.timeScale;bool enabled=feedback.enabled,interactable=button.interactable;
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        var data=new BaseEventData(EventSystem.current);
        try {
            var preference=service.Settings;preference.reduceMotion=false;check(service.SaveSettings(preference),"Enable ordinary shared button motion");
            EventSystem.current.SetSelectedGameObject(null);
            feedback.OnDeselect(data);feedback.OnPointerExit(pointer);yield return .7;
            var ordinary=MotionSequence(feedback,false,output);
            while(ordinary.MoveNext())yield return ordinary.Current;
            feedback.OnPointerEnter(pointer);yield return .2;
            check((feedback.DrawScale-Vector2.one).sqrMagnitude>.0001f,"Ordinary hover still has the established spring feedback");
            Time.timeScale=0;
            feedback.OnPointerDown(pointer);yield return .1;
            feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);yield return .7;
            Settled(feedback,check,"Release and pointer exit settle while gameplay time is paused");
            Time.timeScale=priorScale;
            feedback.OnSelect(data);feedback.OnSubmit(data);yield return .1;
            feedback.OnDeselect(data);yield return .7;
            Settled(feedback,check,"Keyboard select/submit/deselect settles");
            feedback.OnPointerEnter(pointer);feedback.OnPointerDown(pointer);yield return .1;
            feedback.enabled=false;
            Settled(feedback,check,"Disabling feedback resets a held press");
            feedback.enabled=true;yield return .1;
            Settled(feedback,check,"Re-enabling feedback has no stale press");
            feedback.OnPointerEnter(pointer);feedback.OnPointerDown(pointer);yield return .1;
            button.interactable=false;yield return .7;
            Settled(feedback,check,"Disabled buttons settle and clear their prior input");
            button.interactable=true;
            feedback.OnPointerEnter(pointer);feedback.OnPointerDown(pointer);yield return .1;
            button.gameObject.SendMessage("OnApplicationFocus",false,SendMessageOptions.DontRequireReceiver);yield return .1;
            Settled(feedback,check,"Application focus loss resets shared button motion");
            preference=service.Settings;preference.reduceMotion=true;check(service.SaveSettings(preference),"Enable reduced shared button motion");
            var reduced=MotionSequence(feedback,true,output);
            while(reduced.MoveNext())yield return reduced.Current;
            feedback.OnPointerEnter(pointer);feedback.OnSelect(data);feedback.OnSubmit(data);yield return .2;
            check(feedback.DrawScale==Vector2.one&&Mathf.Approximately(feedback.DrawOffset,0)&&feedback.DrawShine>0,
                "Reduce motion keeps hover/keyboard emphasis without spring scale or displacement");
            feedback.OnPointerDown(pointer);yield return .1;
            check(feedback.DrawScale==Vector2.one&&Mathf.Approximately(feedback.DrawOffset,0),"Reduced press keeps its geometry stable");
            feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);feedback.OnDeselect(data);yield return .1;
            Settled(feedback,check,"Reduced feedback clears after release and deselection");
            check(rect.sizeDelta==size&&rect.anchoredPosition==position&&rect.localScale==scale,"Shared feedback preserves the button hit area throughout input states");
        }finally {Time.timeScale=priorScale;feedback.enabled=enabled;button.interactable=interactable;feedback.OnDeselect(data);feedback.OnPointerExit(pointer);}
    }
    static void Settled(JuicyButton feedback,Action<bool,string> check,string message)
        =>check((feedback.DrawScale-Vector2.one).sqrMagnitude<.00002f&&Mathf.Abs(feedback.DrawOffset)<.02f&&feedback.DrawShine<.005f,message);
    static IEnumerator MotionSequence(JuicyButton feedback,bool reduced,string output)
    {
        string prefix=reduced?"motion-reduced":"motion-normal",file=Path.Combine(output,"motion-samples.csv");
        if(!File.Exists(file))File.WriteAllText(file,"mode,index,elapsedSeconds,phase,scaleX,scaleY,offset,shine\n");
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        feedback.OnDeselect(new BaseEventData(EventSystem.current));feedback.OnPointerExit(pointer);
        float start=Time.unscaledTime;
        try {
            for(int frame=0;frame<24;frame++) {
                float frameStart=Time.unscaledTime;
                if(frame==0)feedback.OnPointerEnter(pointer);
                if(frame==6)feedback.OnPointerDown(pointer);
                if(frame==10)feedback.OnPointerUp(pointer);
                if(frame==15)feedback.OnPointerExit(pointer);
                foreach(var delay in Capture(prefix+"-"+frame.ToString("D3")))yield return delay;
                string phase=frame<6?"hover":frame<10?"press":frame<15?"release":"exit";
                File.AppendAllText(file,string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2:F4},{3},{4:F5},{5:F5},{6:F5},{7:F5}\n",
                    prefix,frame,Time.unscaledTime-start,phase,feedback.DrawScale.x,feedback.DrawScale.y,feedback.DrawOffset,feedback.DrawShine));
                // Target 12 fps; record actual times because Editor screenshot I/O may take longer.
                float remaining=1f/12-(Time.unscaledTime-frameStart);
                if(remaining>0)yield return (double)remaining;
            }
        }finally {feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);feedback.OnDeselect(new BaseEventData(EventSystem.current));}
    }
}
