using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// Opt-in standalone integration harness. Never instantiated during normal startup.
public sealed class FocusBuildSmoke : MonoBehaviour
{
    [Serializable] public sealed class CameraCount { public string name,type,hierarchy,scene,targetAtFirstRender;public int instanceId; public long renders,textureRenders; }
    [Serializable] public sealed class PreviewState { public string kind,owner,target;public int componentId,cameraId,targetId,stageId;public bool active,cameraEnabled,targetCreated,rawBound,stageActive; }
    [Serializable] public sealed class Command { public string action,requestId,page; public FocusTimerMode mode; public int focusMinutes=25,restMinutes=5,seconds;public string screenshot;public bool enabled=true; }
    [Serializable] public sealed class Result {
        public string phase,error,companionDirectory,ownerId,sessionId,questTitle,animationState,ninjaSprite,acornSprite;
        public float animationTime; public bool training;
        public double remaining;public int width,height,fish,exp,coins,tickets,focusMinutes,restMinutes;public bool settingsLocked;public bool controlsBound,forest,widgetOpen;
        public bool notificationsEnabled,notificationsRegistered,cancelPrompt,endEarlyVisible;
        public int notificationAttempts,notificationsAccepted,notificationsShown,notificationActivations;
        public string notificationSessionId,notificationKind,notificationError;
        public FocusTimerMode mode; public double elapsed; public string timer;
        public string updateState,updateMessage,updateBlocked,appVersion;
        public string updateWorkDirectory;
        public float updateProgress;
        public bool developmentBuild;
        public string requestId,utc;
        public bool reduceMotion,focused,verificationQuiet;
        public int targetFrameRate;
        public int frameCount;
        public long cameraRenders,textureCameraRenders;
        public CameraCount[] cameras;
        public string previewPage,battleId,battleOutcome,expectedBattleOutcome;
        public PreviewState[] previews;
        public bool bossReady,bossActive;
        public int battleCount,battleExperience,battleCoins;
        public double simulationElapsed;
    }
    string directory;float next;MainMenuNavigation navigation;FocusPanelUI panel;FocusController focus;
    double verificationQuietUntil;
    string lastRequest;
    bool previewVerification;
    string expectedBattleOutcome;
    readonly Dictionary<int,CameraCount> cameraCounts=new Dictionary<int,CameraCount>();
    long cameraRenders,textureCameraRenders;
    void OnEnable() { RenderPipelineManager.beginCameraRendering+=CountCamera; }
    void OnDisable() { RenderPipelineManager.beginCameraRendering-=CountCamera; }
    void CountCamera(ScriptableRenderContext context,Camera camera)
    {
        if(!camera)return;
        int id=camera.GetInstanceID();
        if(!cameraCounts.TryGetValue(id,out var count)) {
            string hierarchy=camera.name;for(var parent=camera.transform.parent;parent;parent=parent.parent)hierarchy=parent.name+"/"+hierarchy;
            count=new CameraCount{name=camera.name,type=camera.cameraType.ToString(),hierarchy=hierarchy,scene=camera.gameObject.scene.name,instanceId=id,targetAtFirstRender=camera.targetTexture?camera.targetTexture.name:""};cameraCounts.Add(id,count);
        }
        count.renders++;cameraRenders++;
        if(camera.targetTexture){count.textureRenders++;textureCameraRenders++;}
    }
    PreviewState Preview(Component component,string kind,Camera camera,GameObject stage)
    {
        var behaviour=component as Behaviour;
        var raw=component.GetComponent<UnityEngine.UI.RawImage>();
        var target=camera?camera.targetTexture:null;
        return new PreviewState{kind=kind,owner=component.transform.parent?component.transform.parent.name:component.name,
            componentId=component.GetInstanceID(),cameraId=camera?camera.GetInstanceID():0,targetId=target?target.GetInstanceID():0,stageId=stage?stage.GetInstanceID():0,
            active=behaviour&&behaviour.isActiveAndEnabled,cameraEnabled=camera&&camera.enabled,targetCreated=target&&target.IsCreated(),target=target?target.name:"",
            rawBound=raw&&raw.enabled&&target&&raw.texture==target,stageActive=stage&&stage.activeInHierarchy};
    }
    PreviewState[] PreviewStates()
    {
        var states=new List<PreviewState>();
        foreach(var home in FindObjectsByType<CharacterHomePreview>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            states.Add(Preview(home,"Home",home.previewCamera,home.character?home.character.gameObject:null));
        foreach(var scene in FindObjectsByType<FocusScenePreview>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            states.Add(Preview(scene,"Focus",scene.PreviewCamera,scene.Stage));
        foreach(var arena in FindObjectsByType<BossArenaPreview>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            states.Add(Preview(arena,"Boss",arena.StageCamera,arena.Stage?arena.Stage.gameObject:null));
        return states.ToArray();
    }
    void Start() { directory=CompanionProtocol.Argument("--smoke-dir");Directory.CreateDirectory(directory); }
    void Update()
    {
        // Opt-in measurement only: silence probe polling/writes for a bounded
        // interval. FocusRunner, rendering and the real companion pipe keep running.
        if(Time.realtimeSinceStartupAsDouble<verificationQuietUntil)return;
        if(Time.unscaledTime<next) return;next=Time.unscaledTime+.2f;
        try {
            if(!navigation) {
                navigation=FindFirstObjectByType<MainMenuNavigation>();if(!navigation || !GameDataManager.Instance) return;
                panel=navigation.pages[(int)MainMenuPage.Focus].GetComponent<FocusPanelUI>();
                var service=GameDataManager.Instance.Tasks;
                if(service.Snapshot.tasks.Count==0)service.Upsert(new QuestTask{title="Read UX Research",date=TaskService.DateKey(service.LocalNow)},null,out _);
                navigation.ShowPage(MainMenuPage.Focus);focus=FocusRunner.EnsureInstance().Controller;
            }
            string commandPath=Path.Combine(directory,"smoke-command.json");
            var command=CompanionProtocol.Read<Command>(commandPath);
            if(command!=null) {
                File.Delete(commandPath);
                lastRequest=command.requestId;
                switch(command.action) {
                    case "performance-quiet":verificationQuietUntil=Time.realtimeSinceStartupAsDouble+Mathf.Clamp(command.seconds,1,45);break;
                    case "reduce-motion":
                        var motionSettings=GameDataManager.Instance.Tasks.Settings;motionSettings.reduceMotion=command.enabled;
                        if(!GameDataManager.Instance.Tasks.SaveSettings(motionSettings))throw new IOException(GameDataManager.Instance.Tasks.LastError);
                        break;
                    case "configure": focus.Configure(new FocusSessionOptions { mode=command.mode, focusMinutes=command.focusMinutes, restMinutes=command.restMinutes });break;
                    case "updates-prepare":AppUpdateController.Ensure().PrepareSmokeUpdate(directory);break;
                    case "updates-check":AppUpdateController.Ensure().Check();break;
                    case "updates-download":AppUpdateController.Ensure().Download();break;
                    case "updates-cancel":AppUpdateController.Ensure().CancelDownload();break;
                    case "updates-install":AppUpdateController.Ensure().Install();break;
                    case "updates-open":FindFirstObjectByType<SettingsLauncher>().Open();FindFirstObjectByType<AppUpdatePanel>().Open();break;
                    case "quest": panel.questList.GetComponentsInChildren<UnityEngine.UI.Button>().Last().onClick.Invoke();break;
                    case "start":panel.start.onClick.Invoke();break;
                    case "end":panel.endEarly.onClick.Invoke();break;
                    case "cancel":panel.cancel.onClick.Invoke();break;
                    case "confirm-cancel":panel.cancelConfirmation.Confirm();break;
                    case "keep-focus":panel.cancelConfirmation.Dismiss();break;
                    case "rest":panel.endRest.onClick.Invoke();break;
                    case "retry":focus.RetrySettlement();break;
                    case "advance":for(int i=0;i<command.seconds;i++)focus.Advance(1);break;
                    case "notifications":
                        var settings=GameDataManager.Instance.Tasks.Settings;settings.notificationsEnabled=command.enabled;
                        if(!GameDataManager.Instance.Tasks.SaveSettings(settings))throw new IOException(GameDataManager.Instance.Tasks.LastError);
                        break;
                    case "widget":CompanionHost.Ensure().SetEnabled(true);break;
                    case "close-widget":CompanionHost.Ensure().SetEnabled(false);break;
                    case "dashboard":navigation.ShowPage(MainMenuPage.Dashboard);break;
                    case "focus":navigation.ShowPage(MainMenuPage.Focus);break;
                    case "preview-page":
                        previewVerification=true;
                        if(!Enum.TryParse(command.page,out MainMenuPage previewPage)||!navigation.ShowPage(previewPage))throw new InvalidOperationException("Preview page is unavailable: "+command.page);
                        break;
                    case "boss-start":
                        var bossPage=navigation.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
                        bossPage.action.onClick.Invoke();bossPage.confirm.onClick.Invoke();
                        var latest=GameDataManager.Instance.PlayerData.battles.LastOrDefault();
                        if(latest!=null){var expected=new BattleSimulation(latest);expected.Advance(120);expectedBattleOutcome=expected.Outcome.ToString();}
                        break;
                    case "boss-confirm-again":navigation.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>().confirm.onClick.Invoke();break;
                    case "boss-skip":navigation.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>().SkipBattle();break;
                    case "screenshot":ScreenCapture.CaptureScreenshot(Path.Combine(directory,command.screenshot));break;
                    case "quit":Destroy(FindFirstObjectByType<FocusExitGuard>());Application.Quit();break;
                }
            }
            var host=CompanionHost.Ensure();var preview=FindFirstObjectByType<CharacterHomePreview>(FindObjectsInactive.Include);
            var runner=FocusRunner.EnsureInstance();
            var data=GameDataManager.Instance.PlayerData;
            var stage=panel.GetComponentInChildren<FocusScenePreview>(true)?.Stage;
            var battle=stage?stage.GetComponentInChildren<FocusTrainingBattle>(true):null;
            var animation=battle?battle.animator.GetCurrentAnimatorStateInfo(0):default;
            var bossUi=previewVerification?navigation.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>():null;
            var lastBattle=previewVerification?data.battles.LastOrDefault():null;
            CompanionProtocol.Write(Path.Combine(directory,"smoke-result.json"),new Result{
                requestId=lastRequest,utc=DateTime.UtcNow.ToString("O"),reduceMotion=UiMotion.Reduced,focused=Application.isFocused,
                targetFrameRate=Application.targetFrameRate,verificationQuiet=Time.realtimeSinceStartupAsDouble<verificationQuietUntil,
                frameCount=Time.frameCount,cameraRenders=cameraRenders,textureCameraRenders=textureCameraRenders,cameras=cameraCounts.Values.ToArray(),
                previewPage=previewVerification?navigation.CurrentPage.ToString():null,previews=previewVerification?PreviewStates():null,
                bossReady=bossUi&&bossUi.arena.IsReady,bossActive=bossUi&&bossUi.IsBattling,simulationElapsed=bossUi&&bossUi.Simulation!=null?bossUi.Simulation.Elapsed:0,
                battleId=lastBattle?.id,battleOutcome=lastBattle?.outcome.ToString(),battleCount=previewVerification?data.battles.Count:0,
                battleExperience=lastBattle?.experience??0,battleCoins=lastBattle?.fishCoins??0,expectedBattleOutcome=expectedBattleOutcome,
                updateState=AppUpdateController.Instance?AppUpdateController.Instance.State.ToString():"",updateMessage=AppUpdateController.Instance?AppUpdateController.Instance.Message:"",updateBlocked=AppUpdateController.RestartBlockReason(),appVersion=Application.version,
                updateWorkDirectory=AppUpdateController.Instance?AppUpdateController.Instance.DownloadDirectory:null,updateProgress=AppUpdateController.Instance?AppUpdateController.Instance.Progress:0,developmentBuild=Debug.isDebugBuild,
                animationState=battle?(animation.IsName("Training")?"Training":"Idle"):"",animationTime=animation.normalizedTime,training=battle && battle.IsTraining,
                ninjaSprite=battle?battle.transform.Find("Training Ninja").GetComponent<SpriteRenderer>().sprite.name:"",
                acornSprite=battle?battle.transform.Find("Acorn").GetComponent<SpriteRenderer>().sprite.name:"",
                focusMinutes=focus.Options.focusMinutes,restMinutes=focus.Options.restMinutes,settingsLocked=!panel.focusDuration.interactable,phase=focus.Phase.ToString(),remaining=focus.RemainingSeconds,width=Screen.width,height=Screen.height,
                mode=focus.Options.mode,elapsed=focus.ElapsedSeconds,timer=panel.timer.text,cancelPrompt=panel.cancelConfirmation&&panel.cancelConfirmation.IsOpen,endEarlyVisible=panel.endEarly.gameObject.activeSelf,
                fish=focus.SpectatorFishCount,exp=data.experience,coins=data.fishCoins,tickets=data.tickets,
                ownerId=host.OwnerId,sessionId=focus.SessionId ?? "",companionDirectory=host.DirectoryPath,widgetOpen=host.IsOpen,error=host.Error,
                notificationsEnabled=GameDataManager.Instance.Tasks.Settings.notificationsEnabled,notificationsRegistered=runner.NotificationsRegistered,
                notificationAttempts=runner.NotificationAttempts,notificationsAccepted=runner.NotificationsAccepted,notificationsShown=runner.NotificationsShown,notificationActivations=runner.NotificationActivations,
                notificationSessionId=runner.LastNotificationSessionId,notificationKind=runner.LastNotificationKind,notificationError=runner.NotificationError,
                questTitle=focus.Options.questTitle,controlsBound=panel.questList && panel.focusDuration && panel.restDuration && panel.focusDuration.wholeNumbers && panel.restDuration.wholeNumbers,
                forest=preview && preview.showSceneBackground && preview.previewCamera && preview.character });
        }catch(Exception e){File.WriteAllText(Path.Combine(directory,"smoke-error.txt"),e.ToString());}
    }
}
