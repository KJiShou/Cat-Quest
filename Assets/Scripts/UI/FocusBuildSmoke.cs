using System;
using System.IO;
using System.Linq;
using UnityEngine;

// Opt-in standalone integration harness. Never instantiated during normal startup.
public sealed class FocusBuildSmoke : MonoBehaviour
{
    [Serializable] public sealed class Command { public string action; public int focusMinutes=25,restMinutes=5,seconds;public string screenshot; }
    [Serializable] public sealed class Result {
        public string phase,error,companionDirectory,ownerId,sessionId,questTitle,animationState,ninjaSprite,acornSprite;
        public float animationTime; public bool training;
        public double remaining;public int width,height,fish,exp,coins,tickets,focusMinutes,restMinutes;public bool settingsLocked;public bool controlsBound,forest,widgetOpen;
    }
    string directory;float next;MainMenuNavigation navigation;FocusPanelUI panel;FocusController focus;
    void Start() { directory=CompanionProtocol.Argument("--smoke-dir");Directory.CreateDirectory(directory); }
    void Update()
    {
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
                switch(command.action) {
                    case "configure": panel.focusDuration.value=command.focusMinutes;panel.restDuration.value=command.restMinutes;break;
                    case "quest": panel.questList.GetComponentsInChildren<UnityEngine.UI.Button>().Last().onClick.Invoke();break;
                    case "start":panel.start.onClick.Invoke();break;
                    case "end":panel.endEarly.onClick.Invoke();break;
                    case "cancel":panel.cancel.onClick.Invoke();break;
                    case "rest":panel.endRest.onClick.Invoke();break;
                    case "advance":for(int i=0;i<command.seconds;i++)focus.Advance(1);break;
                    case "widget":CompanionHost.Ensure().SetEnabled(true);break;
                    case "close-widget":CompanionHost.Ensure().SetEnabled(false);break;
                    case "dashboard":navigation.ShowPage(MainMenuPage.Dashboard);break;
                    case "focus":navigation.ShowPage(MainMenuPage.Focus);break;
                    case "screenshot":ScreenCapture.CaptureScreenshot(Path.Combine(directory,command.screenshot));break;
                    case "quit":Destroy(FindFirstObjectByType<FocusExitGuard>());Application.Quit();break;
                }
            }
            var host=CompanionHost.Ensure();var preview=FindFirstObjectByType<CharacterHomePreview>(FindObjectsInactive.Include);
            var data=GameDataManager.Instance.PlayerData;
            var stage=panel.GetComponentInChildren<FocusScenePreview>(true)?.Stage;
            var battle=stage?stage.GetComponentInChildren<FocusTrainingBattle>(true):null;
            var animation=battle?battle.animator.GetCurrentAnimatorStateInfo(0):default;
            CompanionProtocol.Write(Path.Combine(directory,"smoke-result.json"),new Result{
                animationState=battle?(animation.IsName("Training")?"Training":"Idle"):"",animationTime=animation.normalizedTime,training=battle && battle.IsTraining,
                ninjaSprite=battle?battle.transform.Find("Training Ninja").GetComponent<SpriteRenderer>().sprite.name:"",
                acornSprite=battle?battle.transform.Find("Acorn").GetComponent<SpriteRenderer>().sprite.name:"",
                focusMinutes=focus.Options.focusMinutes,restMinutes=focus.Options.restMinutes,settingsLocked=!panel.focusDuration.interactable,phase=focus.Phase.ToString(),remaining=focus.RemainingSeconds,width=Screen.width,height=Screen.height,
                fish=focus.SpectatorFishCount,exp=data.experience,coins=data.fishCoins,tickets=data.tickets,
                ownerId=host.OwnerId,sessionId=focus.SessionId ?? "",companionDirectory=host.DirectoryPath,widgetOpen=host.IsOpen,error=host.Error,
                questTitle=focus.Options.questTitle,controlsBound=panel.questList && panel.focusDuration && panel.restDuration && panel.focusDuration.wholeNumbers && panel.restDuration.wholeNumbers,
                forest=preview && preview.showSceneBackground && preview.previewCamera && preview.character });
        }catch(Exception e){File.WriteAllText(Path.Combine(directory,"smoke-error.txt"),e.ToString());}
    }
}
