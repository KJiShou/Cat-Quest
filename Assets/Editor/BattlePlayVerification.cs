using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BattlePlayVerification
{
    const string Flag="CatQuest.BattlePlayCheck",Folder="Library/BattleVerification";
    static int step;static double next;static RewardScreenshotCapture capture;static int tickets;static float startX,normalSpeed;
    static string error;
    static bool loadedScene;
    static Quaternion bossRestRotation;
    static Vector3 bossRestScale;
    static Vector3 buttonTextRest;
    static BattlePlayVerification(){Application.logMessageReceived+=(message,trace,type)=>{if(SessionState.GetBool(Flag,false)&&(type==LogType.Exception||type==LogType.Error||message.Contains("Animator.GotoState")||message.Contains("inactive object")))error=message;};EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){step=0;error=null;loadedScene=false;next=EditorApplication.timeSinceStartup+3;}
        if(s==PlayModeStateChange.EnteredEditMode){UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);}
    };}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{width,height});
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-battle-play.request")){
            File.Delete("Temp/verify-battle-play.request");Directory.CreateDirectory(Folder);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");SessionState.SetBool(Flag,true);Resolution(1920,1080);EditorApplication.isPlaying=true;return;
        }
        if(!EditorApplication.isPlaying||!SessionState.GetBool(Flag,false)||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();
        try{
            if(!loadedScene){loadedScene=true;UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");next=EditorApplication.timeSinceStartup+3;return;}
            var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
            if(!nav&&step==0){UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");next=EditorApplication.timeSinceStartup+3;return;}
            Check(nav,"Navigation missing");var panel=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
            if(step==0){
                Check(GameDataManager.Instance.SavePath.Contains("BattleVerification"),"Must use isolated save");
                GameDataManager.Instance.PlayerData.tickets=10;GameDataManager.Instance.PlayerData.experience=2025;GameDataManager.Instance.SaveData();
                nav.ShowPage(MainMenuPage.Boss);
            }
            if(step==1){Check(panel.actors.Ready,"Actors not ready");panel.stageButton.onClick.Invoke();Capture("stage-select-1920");}
            if(step==2){panel.SelectStage(2);Check(panel.actors.Boss.name.StartsWith("Orc"),"Orc not selected");panel.Action();Check(!panel.confirmation.activeSelf,"Locked stage entered");Capture("orc-preview");}
            if(step==3){panel.SelectStage(3);Check(panel.actors.Boss.name.StartsWith("Demon"),"Demon not selected");panel.SelectStage(1);Resolution(1280,720);}
            if(step==4){panel.stageButton.onClick.Invoke();Capture("stage-select-1280");}
            if(step==5){panel.SelectStage(1);var animator=panel.actors.Boss.GetComponentInChildren<Animator>();bossRestRotation=animator.transform.localRotation;bossRestScale=animator.transform.localScale;Capture("battle-ready-1280");}
            if(step==6){tickets=GameDataManager.Instance.PlayerData.tickets;startX=panel.actors.Cat.position.x;panel.Action();Check(panel.confirmation.activeSelf,"Entry confirmation missing");Capture("confirm-1280");}
            if(step==7){panel.Confirm();Check(panel.IsBattling&&GameDataManager.Instance.PlayerData.tickets==tickets-1,"Entry did not deduct exactly once");}
            if(step==8){Check(panel.actors.Cat.position.x>startX,"Cat did not run toward boss");Capture("run-1280");}
            if(step==9){normalSpeed=panel.actors.CatAnimationSpeed;Capture("attack-1280");}
            if(step==10){
                if(panel.IsBattling){next=EditorApplication.timeSinceStartup+1;return;}
                Check(panel.Simulation.Outcome==BattleOutcome.Victory,"Expected Lv10 Boss1 victory");
                Check(GameDataManager.Instance.PlayerData.bossProgress[0].wins==1,"Victory not saved");Check(panel.resultPanels[0].activeSelf && panel.resultValues[0].text.Contains("30 EXP"),"Victory result/reward missing");Capture("victory-1280");
            }
            if(step==11){
                Check(Math.Abs(panel.actors.Cat.position.x-startX)<.001,"Cat did not return home after result");
                Check(panel.bossBar.value==1 && panel.catHealth.GetComponent<UnityEngine.UI.Slider>().value==1,"Post-result health did not reset together");
                CheckRestored(panel);Capture("post-battle-reset-1280");
            }
            if(step==12){
                panel.CloseResult();
                // Explicitly sample a completed death, then reselect the same boss.
                var animator=panel.actors.Boss.GetComponentInChildren<Animator>();
                animator.Play("Die",0,1);animator.Update(0);panel.SelectStage(1);CheckRestored(panel);
                var stats=GameDataManager.Instance.Tasks.CatStats;stats.aspd=2;panel.actors.ResetPose();panel.actors.StartCombat(stats,BattleRules.Boss(1));
                Check(Math.Abs(panel.actors.CatAnimationSpeed-normalSpeed*2)<.001,"Double ASPD did not double animation speed");Capture("attack-speed-2x");
            }
            if(step==13){panel.SelectStage(1);panel.Action();panel.Confirm();nav.ShowPage(MainMenuPage.Dashboard);Check(GameDataManager.Instance.PlayerData.battles.Last().outcome==BattleOutcome.Stopped,"Leaving page must stop without refund");nav.ShowPage(MainMenuPage.Boss);}
            if(step==14){
                CheckRestored(panel);
                GameDataManager.Instance.PlayerData.experience=0;GameDataManager.Instance.SaveData();
                panel.SelectStage(1);panel.Action();panel.Confirm();panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);
                Check(panel.resultPanels[1].activeSelf && !panel.resultPanels[0].activeSelf,"Defeat result missing");
                Check(!panel.resultValues[1].text.Contains("+"),"Defeat shows fake reward");Capture("defeat-result-1280");
            }
            if(step==15){panel.resultContinue[1].onClick.Invoke();Check(!panel.resultPanels[1].activeSelf,"Continue did not close result");}
            if(step==16){
                var health=panel.catHealth.GetComponent<PlayerHealthFeedback>();health.SetHealth(1,false);health.SetHealth(.2f,true);
                var bossHealth=panel.bossBar.GetComponent<PlayerHealthFeedback>();bossHealth.SetHealth(1,false);bossHealth.SetHealth(.2f,true);
                var numbers=panel.GetComponent<BattleDamageNumbers>();
                for(int i=0;i<6;i++)numbers.Spawn(10+i,i%2==0,i%2==0?panel.actors.Cat:panel.actors.Boss,panel.arena.StageCamera);
                Check(numbers.ActiveCount==6,"Rapid hits overwrite numbers");
            }
            if(step==17){
                var health=panel.catHealth.GetComponent<PlayerHealthFeedback>();
                float shown=panel.catHealth.GetComponent<UnityEngine.UI.Slider>().value;
                Check(shown>.2f && shown<1,"Health must drain smoothly");
                Check(health.GhostValue>shown,"Ghost must trail the main fill");
                Check(health.FillColor.r>health.FillColor.b,"Panic health must be red");
                var bossHealth=panel.bossBar.GetComponent<PlayerHealthFeedback>();
                Check(panel.bossBar.value>.2f && panel.bossBar.value<1,"Boss health must drain smoothly");
                Check(bossHealth.GhostValue>panel.bossBar.value,"Boss ghost must trail main fill");
                Check(panel.bossBar.transform.localScale.x>.5f,"Boss hit must punch scale");
                Check(Vector2.Distance(((RectTransform)panel.bossBar.transform).anchoredPosition,new Vector2(0,-52))>.01f,"Boss hit must shake position");
                Capture("combat-feedback-1280");
            }
            if(step==18){
                var health=panel.catHealth.GetComponent<PlayerHealthFeedback>();
                Check(Math.Abs(health.GhostValue-.2f)<.001,"Ghost did not settle");
                Check(panel.GetComponent<BattleDamageNumbers>().ActiveCount==0,"Damage numbers did not expire");
                health.SetHealth(1,false);Check(health.FillColor.b>health.FillColor.r,"Reset did not restore healthy color");
                var bossHealth=panel.bossBar.GetComponent<PlayerHealthFeedback>();
                Check(Math.Abs(bossHealth.GhostValue-.2f)<.001,"Boss ghost did not settle");
                Check(Vector2.Distance(((RectTransform)panel.bossBar.transform).anchoredPosition,new Vector2(0,-52))<.001f,"Boss shake drifted");
                Check(Vector3.Distance(panel.bossBar.transform.localScale,Vector3.one*.5f)<.001f,"Boss punch did not reset");
                Check(bossHealth.FillColor==bossHealth.healthy,"Boss must retain red color");
                panel.SelectStage(1);Check(panel.bossBar.value==1 && bossHealth.GhostValue==1,"Stage selection must reset boss health and ghost");
            }
            if(step==19){
                buttonTextRest=panel.stageButton.GetComponentInChildren<TMPro.TMP_Text>().textInfo.meshInfo[0].vertices[0];
                foreach(var control in UnityEngine.UI.Selectable.allSelectablesArray)if(control is UnityEngine.UI.Button)Check(control.GetComponent<JuicyButton>(),"Button missing shared feedback: "+control.name);
                panel.stageButton.GetComponent<JuicyButton>().OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
                panel.action.GetComponent<JuicyButton>().OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));}
            if(step==20){
                CheckButtonText(panel.stageButton);
                Check(panel.stageButton.GetComponent<JuicyButton>().DrawScale.x>1.03f,"Shared button hover missing");
                var juice=panel.action.GetComponent<JuicyButton>();Check(juice.visual.localScale.x>1.03f,"Hover must enlarge button");Capture("button-hover-1280");
            }
            if(step==21){panel.stageButton.GetComponent<JuicyButton>().OnPointerDown(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));panel.action.GetComponent<JuicyButton>().OnPointerDown(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));}
            if(step==22){
                CheckButtonText(panel.stageButton);
                Check(panel.stageButton.GetComponent<JuicyButton>().DrawScale.y<.92f,"Shared button press missing");
                Check(panel.stageButton.transform.localScale==Vector3.one,"Shared button must preserve layout scale");
                var juice=panel.action.GetComponent<JuicyButton>();Check(juice.visual.localScale.y<.92f,"Press must squash button");Capture("button-pressed-1280");
            }
            if(step==23){panel.stageButton.interactable=false;var juice=panel.action.GetComponent<JuicyButton>();juice.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));juice.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));}
            if(step==24){
                CheckButtonText(panel.stageButton);
                Check(Math.Abs(panel.stageButton.GetComponent<JuicyButton>().DrawScale.x-1)<.005f,"Disabled button must reset feedback");panel.stageButton.interactable=true;
                var juice=panel.action.GetComponent<JuicyButton>();Check(Math.Abs(juice.visual.localScale.x-1)<.005f,"Button must return to rest");Capture("button-normal-1280");
            }
            if(step==25){Check(error==null,error);File.WriteAllText(Folder+"/play-result.txt","PASS: stage selector/portraits at 1920 and 1280, Orc/Demon preview, gate rejection, confirmation, one-ticket entry, cat run, combat, victory persistence, 2x ASPD doubles animation speed, navigation stops battle; death pose, health, same-stage replay and navigation reset; victory/defeat result prefabs, actual rewards and Continue; smooth drain, ghost lag/settle, panic/reset colors, independent pooled damage numbers and expiration.");Resolution(1920,1080);EditorApplication.isPlaying=false;return;}
            step++;next=EditorApplication.timeSinceStartup+(step==17?.12:step==18?1.6:step==11?2.5:step==9?1.5:.5);
        }catch(Exception e){File.WriteAllText(Folder+"/play-result.txt",e.ToString());Debug.LogException(e);EditorApplication.isPlaying=false;}
    }
    static void CheckButtonText(UnityEngine.UI.Button button)
    {
        var label=button.GetComponentInChildren<TMPro.TMP_Text>();
        var root=(RectTransform)button.transform;var juice=button.GetComponent<JuicyButton>();
        Vector3 pivot=root.rect.center;
        Vector3 baseline=root.InverseTransformPoint(label.transform.TransformPoint(buttonTextRest));
        Vector3 expected=pivot+Vector3.Scale(baseline-pivot,new Vector3(juice.DrawScale.x,juice.DrawScale.y,1))+Vector3.up*juice.DrawOffset;
        Vector3 actual=root.InverseTransformPoint(label.transform.TransformPoint(label.textInfo.meshInfo[0].vertices[0]));
        Check(Vector3.Distance(expected,actual)<.3f,"Button text does not follow background: "+Vector3.Distance(expected,actual));
    }
    static void CheckRestored(BossPageUI panel)
    {
        var animator=panel.actors.Boss.GetComponentInChildren<Animator>();
        Check(Quaternion.Angle(animator.transform.localRotation,bossRestRotation)<.01f,"Boss root kept death rotation");
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Boss did not return to Idle");
        // Idle itself pulses the root scale; compare the same animation phase.
        animator.Play("Idle",0,0);animator.Update(0);
        Check(Vector3.Distance(animator.transform.localScale,bossRestScale)<.001f,"Boss root kept death scale");
    }
    static void Capture(string name){if(!capture)capture=new GameObject("Battle Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
}
