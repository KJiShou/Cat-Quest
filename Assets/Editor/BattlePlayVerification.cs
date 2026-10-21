using System;
using System.IO;
using System.Collections;
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
    static IEnumerator impacts;
    static Quaternion bossRestRotation;
    static Vector3 bossRestScale;
    static Vector3 buttonTextRest;
    static BattlePlayVerification(){Application.logMessageReceived+=(message,trace,type)=>{if(SessionState.GetBool(Flag,false)&&(type==LogType.Exception||type==LogType.Error||message.Contains("Animator.GotoState")||message.Contains("inactive object")))error=message;};EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){step=0;error=null;loadedScene=false;impacts=null;next=EditorApplication.timeSinceStartup+3;}
        if(s==PlayModeStateChange.EnteredEditMode){UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetString(Flag+".env","") is string prior&&prior.Length>0?prior:null);
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,SessionState.GetInt(Flag+".size",0));Time.timeScale=SessionState.GetFloat(Flag+".timeScale",1);}
    };}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{width,height});
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-battle-play.request")){
            File.Delete("Temp/verify-battle-play.request");Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/play-result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            SessionState.SetString(Flag+".env",Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"");SessionState.SetFloat(Flag+".timeScale",Time.timeScale);SessionState.SetFloat(Flag+".started",(float)EditorApplication.timeSinceStartup);
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));SessionState.SetInt(Flag+".size",(int)view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(view));Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");SessionState.SetBool(Flag,true);Resolution(1920,1080);EditorApplication.isPlaying=true;return;
        }
        if(SessionState.GetBool(Flag,false)&&EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+".started",0)>180){File.WriteAllText(Folder+"/play-result.txt","FAIL: timeout");EditorApplication.isPlaying=false;return;}
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
                Check(GameDataManager.Instance.PlayerData.bossProgress[0].wins==1,"Victory not saved");Check(panel.resultPanels[0].activeSelf && panel.resultValues[0].text=="+30"&&panel.resultValues[0].transform.parent.Find("Icon_Trophy").gameObject.activeSelf,"Victory result/reward missing");Capture("victory-1280");
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
            if(step==25){
                if(impacts==null)impacts=ImpactChecks(panel);
                if(impacts.MoveNext()){next=EditorApplication.timeSinceStartup+Convert.ToDouble(impacts.Current);return;}
                Check(error==null,error);File.WriteAllText(Folder+"/play-result.txt","PASS "+DateTime.UtcNow.ToString("O")+": 55ms visual hit stop, camera shake/restore, stronger recoil on all three bosses, repeated/simultaneous hits, unscaled playback, attack continuity, death/reset/disable/focus cleanup; stage selector/portraits at 1920 and 1280, Orc/Demon preview, gate rejection, confirmation, one-ticket entry, cat run, combat, victory persistence, 2x ASPD doubles animation speed, navigation stops battle; death pose, health, same-stage replay and navigation reset; victory/defeat result prefabs, actual rewards and Continue; smooth drain, ghost lag/settle, panic/reset colors, independent pooled damage numbers and expiration.");Resolution(1920,1080);EditorApplication.isPlaying=false;return;}
            step++;next=EditorApplication.timeSinceStartup+(step==17?.12:step==18?1.6:step==11?2.5:step==9?1.5:.5);
        }catch(Exception e){File.WriteAllText(Folder+"/play-result.txt",e.ToString());Debug.LogException(e);EditorApplication.isPlaying=false;}
    }
    static IEnumerator RewardChecks(BossPageUI panel)
    {
        var manager=GameDataManager.Instance;var service=manager.Tasks;
        panel.CloseResult();manager.PlayerData.experience=2025;manager.PlayerData.tickets=10;
        manager.PlayerData.rewardDays.Add(new RewardDay{day="2026-10-21",questsClaimed=true});manager.SaveData();
        panel.SelectStage(1);panel.Action();panel.Confirm();panel.Simulation.Advance(120);panel.Settle(panel.Simulation.Outcome);
        Check(panel.resultPanels[0].activeSelf&&manager.PlayerData.battles.Last().experience==0,"Repeat victory fixture");
        Check(panel.resultValues[0].text==""&&!panel.resultValues[0].transform.parent.gameObject.activeSelf&&!panel.resultDetails[0].gameObject.activeSelf,"Zero-EXP victory shows no mastery or empty reward frame");
        foreach(int width in new[]{1920,1280}){Resolution(width,width==1920?1080:720);yield return .3;Capture("victory-no-exp-"+width);yield return .3;}
        panel.CloseResult();panel.stageButton.onClick.Invoke();yield return .2;
        Check(panel.masteryParticles.All(p=>!p.gameObject.activeSelf),"No particles without a claim");
        var progress=manager.PlayerData.bossProgress[0];progress.wins=5;progress.pendingClaims=1;manager.SaveData();
        panel.stageButton.onClick.Invoke();yield return .2;
        Check(panel.masteryParticles[0].isActiveAndEnabled&&!panel.masteryParticles[1].gameObject.activeSelf,"Only claimable chest emits");
        Check(panel.masteryParticles.All(p=>!p.raycastTarget),"Particles do not block chest input");
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .3;
            for(int frame=0;frame<4;frame++){Capture("chest-particles-"+width+"-"+frame);yield return .2;}
        }
        panel.closeStages.onClick.Invoke();Check(!panel.masteryParticles[0].isActiveAndEnabled,"Closed Stage stops particles");
        panel.stageButton.onClick.Invoke();yield return .2;Check(panel.masteryParticles[0].isActiveAndEnabled,"Reopened Stage resumes particles");
        Check(service.ClaimEquipment("boss1-weapon"),"Claim fixture reward");yield return .2;
        Check(!panel.masteryParticles[0].gameObject.activeSelf,"Claim consumes chest sparkle immediately");
        panel.closeStages.onClick.Invoke();
    }
    static IEnumerator ImpactChecks(BossPageUI panel)
    {
        var rewardChecks=RewardChecks(panel);while(rewardChecks.MoveNext())yield return rewardChecks.Current;

        panel.CloseResult();yield return .3;
        var actors=panel.actors;
        foreach(int id in new[]{1,2,3}){
            Resolution(id==1?1920:1280,id==1?1080:720);panel.SelectStage(id);yield return .3;
            actors.StartRun();actors.Run(2);actors.StartCombat(new CombatStats(1000,5,5,2),BattleRules.Boss(id));yield return .15;
            var catAnimator=actors.Cat.GetComponentInChildren<Animator>();var bossAnimator=actors.Boss.GetComponentInChildren<Animator>();
            float catPhase=catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var catPosition=actors.Cat.position;var bossPosition=actors.Boss.position;
            var camera=panel.arena.StageCamera.transform;var cameraRest=camera.localPosition;
            Capture("impact-"+id+"-before");yield return .05;
            float phaseBeforeStop=catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            actors.Hit(true);actors.Hit(false);
            Check(actors.IsHitStopped&&catAnimator.speed==0&&bossAnimator.speed==0,"Both actor animations must freeze on impact");
            Check(Time.timeScale==1,"Hit stop must not pause the game clock");
            catAnimator.Update(.02f);Check(Mathf.Abs(catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime-phaseBeforeStop)<.0001f,"Frozen attack phase must hold");
            Check(actors.CatReacting&&actors.BossReacting,"Both actors must react to simultaneous hits");
            bool displacedCat=false,displacedBoss=false,shook=false;
            for(int frame=0;frame<12;frame++){
                Capture("impact-"+id+"-"+frame.ToString("00"));yield return .025;
                shook|=Vector3.Distance(camera.localPosition,cameraRest)>.001f;
                displacedCat|=actors.Cat.position.x<catPosition.x-.001f;displacedBoss|=actors.Boss.position.x>bossPosition.x+.001f;
            }
            Check(shook,"Arena camera must visibly shake");
            Check(!actors.IsHitStopped&&catAnimator.speed>0&&bossAnimator.speed>0,"Animations must resume after hit stop");
            Check(displacedCat&&displacedBoss,"Both actors must recoil away from the attacker");
            Check(catAnimator.GetCurrentAnimatorStateInfo(0).IsName("Attack")&&catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime>catPhase,"Hurt must not restart or stop the attack cycle");
            yield return .35;Check(!actors.CatReacting&&!actors.BossReacting,"Hit reactions must settle");
            Check(Vector3.Distance(actors.Cat.position,catPosition)<.002f&&Vector3.Distance(actors.Boss.position,bossPosition)<.002f,"Hit position drift");
            Check(Vector3.Distance(camera.localPosition,cameraRest)<.0001f&&!actors.IsShaking,"Camera shake must settle without drift");
            float repeatPhase=catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime,repeatTime=Time.unscaledTime;
            // Rapid repeats remain bounded; paused game time must not freeze UI combat feedback.
            Time.timeScale=0;
            for(int repeat=0;repeat<5;repeat++){actors.Hit(true);actors.Hit(false);yield return .035;}
            yield return .4;Check(!actors.CatReacting&&!actors.BossReacting,"Unscaled hit feedback did not settle");
            Check(Vector3.Distance(actors.Cat.position,catPosition)<.002f&&Vector3.Distance(actors.Boss.position,bossPosition)<.002f,"Repeated hits accumulated displacement");
            Check(Mathf.Abs(catAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime-repeatPhase-2*(Time.unscaledTime-repeatTime))<.1f,"Repeated hit stops must catch up attack phase");
            Check(!actors.IsHitStopped&&Vector3.Distance(camera.localPosition,cameraRest)<.0001f,"Repeated hit stops restore speed and camera");
            Time.timeScale=1;
            actors.Hit(true);actors.Hit(false);yield return .06;
            actors.SendMessage("OnApplicationFocus",false);Check(!actors.IsHitStopped&&catAnimator.speed>0&&Vector3.Distance(camera.localPosition,cameraRest)<.0001f,"Focus loss must restore speed/camera");Check(!actors.CatReacting&&!actors.BossReacting,"Focus loss must clear impacts");
            actors.Hit(true);yield return .06;actors.Finish(BattleOutcome.Victory);
            Check(!actors.BossReacting&&bossAnimator.GetCurrentAnimatorStateInfo(0).IsName("Die"),"Lethal result must retain death animation");
            actors.Hit(true);Check(!actors.BossReacting,"Post-result hit must not restart recoil");
            actors.ResetPose();actors.StartCombat(new CombatStats(1000,5,5,1),BattleRules.Boss(id));actors.Hit(false);yield return .06;
            actors.enabled=false;Check(!actors.IsHitStopped&&catAnimator.speed>0&&Vector3.Distance(camera.localPosition,cameraRest)<.0001f,"Disable must restore speed/camera");Check(!actors.CatReacting&&!actors.BossReacting,"Disable must clear recoil");actors.enabled=true;actors.ResetPose();
            Check(catAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Re-enable/reset must restore idle");
            Capture("impact-"+id+"-reset");yield return .05;
        }
        panel.SelectStage(1);
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
