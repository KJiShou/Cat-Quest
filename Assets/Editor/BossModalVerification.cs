using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class BossModalVerification
{
    const string Flag="CatQuest.BossModals",Folder="Library/BossModalVerification";
    static IEnumerator run; static double next; static RewardScreenshotCapture capture; static string error;
    static object View=>EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo Size=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static BossModalVerification(){
        EditorApplication.update+=Poll;
        Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Flag,false)&&(t==LogType.Error||t==LogType.Exception))error=m;};
        EditorApplication.playModeStateChanged+=s=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(s==PlayModeStateChange.EnteredPlayMode){run=Run();next=EditorApplication.timeSinceStartup+2;error=null;}
            if(s==PlayModeStateChange.EnteredEditMode){run=null;SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetString(Flag+"env","") is string old&&old.Length>0?old:null);Size.SetValue(View,SessionState.GetInt(Flag+"size",0));EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+"scene",""));}
        };
    }
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-boss-modals.request")){
            var mode=File.ReadAllText("Temp/verify-boss-modals.request").Trim();File.Delete("Temp/verify-boss-modals.request");Directory.CreateDirectory(Folder+"/"+mode);SessionState.SetString(Flag+"mode",mode);
            SessionState.SetString(Flag+"env",Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"");SessionState.SetInt(Flag+"size",(int)Size.GetValue(View));SessionState.SetString(Flag+"scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetFloat(Flag+"timeScale",Time.timeScale);SessionState.SetFloat(Flag+"start",(float)EditorApplication.timeSinceStartup);SessionState.SetBool(Flag,true);File.WriteAllText(Folder+"/"+mode+"/result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/"+mode+"/save-"+Guid.NewGuid().ToString("N")+".json"));EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool(Flag,false))return;
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+"start",0)>150){Finish("FAIL timeout");return;}
        if(!EditorApplication.isPlaying||run==null||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();try{if(error!=null)throw new Exception(error);if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else Finish("PASS "+DateTime.UtcNow.ToString("O")+": isolated save; Boss confirmation, stage chests and claim selector captured at 1920x1080 and 1280x720.");}catch(Exception e){Finish("FAIL "+e);}
    }
    static void Finish(string result){if(EditorApplication.isPlaying)Time.timeScale=SessionState.GetFloat(Flag+"timeScale",1);File.WriteAllText(Folder+"/"+SessionState.GetString(Flag+"mode","")+"/result.txt",result);run=null;EditorApplication.isPlaying=false;}
    static void Capture(string name){if(!capture)capture=new GameObject("Boss Modal Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+SessionState.GetString(Flag+"mode","")+"/"+name+".png"));}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Modal(Transform modal){typeof(EquipmentPlayVerification).GetMethod("CheckScreenModal",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{modal});}
    static void TextFits(Transform root){Canvas.ForceUpdateCanvases();foreach(var text in root.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Text overflow: "+text.text);}}
    static float ParticleTime(ChestRewardParticles particle)=>(float)typeof(ChestRewardParticles).GetField("elapsed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(particle);
    static IEnumerator Run(){
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");foreach(var obj in asset.GetComponentsInChildren<Transform>(true))Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(obj.gameObject)==0,"Missing script "+obj.name);
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var manager=GameDataManager.EnsureInstance();
        if(!manager.SavePath.Contains("BossModalVerification"))throw new Exception("Save isolation failed");
        manager.PlayerData.experience=2025;manager.PlayerData.tickets=10;manager.PlayerData.rewardDays.Add(new RewardDay{day="2026-10-24",questsClaimed=true});
        foreach(var p in manager.PlayerData.bossProgress){p.wins=11;p.pendingClaims=2;}manager.SaveData();
        nav.ShowPage(MainMenuPage.Boss);yield return 1;
        var boss=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();var gear=boss.GetComponent<EquipmentPageUI>();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .6;
            boss.Action();yield return .4;Modal(boss.confirmation.transform);TextFits(boss.confirmation.transform);Capture("entry-"+width);yield return .3;boss.cancel.onClick.Invoke();
            boss.stageButton.onClick.Invoke();yield return .4;Capture("stages-"+width);yield return .3;
            gear.OpenClaim(1);yield return .5;Modal(gear.transform.Find("ItemModal"));TextFits(gear.claimView.transform);Capture("claim-"+width);yield return .3;
            gear.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();boss.closeStages.onClick.Invoke();
        }
        var view=gear.claimView;var data=manager.PlayerData;var progress=data.bossProgress.Single(x=>x.bossId==1);
        foreach(int wins in new[]{0,4,5,9,10,11,14,15,30}){
            progress.wins=wins;progress.pendingClaims=Math.Min(3,wins/5);progress.claimedComponents=0;gear.OpenClaim(1);yield return .1;
            Check(view.progress.value==Math.Min(15,wins),"Mastery boundary "+wins);
            for(int i=0;i<3;i++)Check(view.particles[i].gameObject.activeSelf==(wins>=(i+1)*5),"Claim sparkle boundary "+wins+"/"+i);
            if(wins==4||wins==15){Capture("mastery-"+wins);yield return .2;}
        }
        progress.wins=11;progress.pendingClaims=2;gear.OpenClaim(1);yield return .2;
        Canvas.ForceUpdateCanvases();foreach(var particle in view.particles.Where(p=>p.gameObject.activeInHierarchy)){Check(particle.GetComponent<CanvasRenderer>(),"Particles missing renderer");var mesh=particle.canvasRenderer.GetMesh();Check(mesh&&mesh.vertexCount>30,"Particle mesh was not rendered");Check(!particle.raycastTarget,"Particle blocks input");}
        float oldTime=Time.timeScale;Time.timeScale=0;
        float before=ParticleTime(view.particles[0]);yield return .3;Check(ParticleTime(view.particles[0])>before,"Sparkles must advance while paused");Time.timeScale=oldTime;
        var chest=view.chests[0].rectTransform;
        view.particles[0].SendMessage("OnApplicationFocus",true);var resting=chest.localRotation;
        bool moved=false,settled=false;
        for(int frame=0;frame<60;frame++){
            float angle=Quaternion.Angle(chest.localRotation,resting);
            if(angle>.15f)moved=true;
            if(moved&&angle<.01f)settled=true;
            Capture("sparkle-"+frame.ToString("00"));yield return .08;
        }
        Check(moved&&settled,"Chest must wobble briefly then rest");
        view.particles[0].SendMessage("OnApplicationFocus",false);Check(Quaternion.Angle(chest.localRotation,resting)<.01f,"Focus loss must restore chest pose");
        view.particles[0].SendMessage("OnApplicationFocus",true);
        var events=UnityEngine.EventSystems.EventSystem.current;
        var button=view.buttons[0];var feedback=button.GetComponent<JuicyButton>();
        var pointer=new UnityEngine.EventSystems.PointerEventData(events){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left};
        feedback.OnPointerEnter(pointer);yield return .2;Capture("choice-hover");yield return .15;
        feedback.OnPointerDown(pointer);yield return .15;Check(feedback.DrawScale.x!=1,"Press feedback did not animate");Capture("choice-pressed");yield return .15;
        for(int n=0;n<5;n++){feedback.OnPointerUp(pointer);feedback.OnPointerDown(pointer);}feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);feedback.OnDeselect(new UnityEngine.EventSystems.BaseEventData(events));yield return .5;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.015f,"Button failed to settle");
        feedback.OnSubmit(new UnityEngine.EventSystems.BaseEventData(events));yield return .2;
        feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus loss must reset feedback");
        gear.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();Check(feedback.DrawScale==Vector2.one,"Disable must reset feedback");gear.OpenClaim(1);yield return .2;
        Check(ParticleTime(view.particles[0])<.6f,"Sparkles must restart on reopen");
        int owned=data.equipment.Count;view.buttons[0].onClick.Invoke();yield return .2;
        gear.transform.Find("ItemDetail/Popup/Button_Select").GetComponent<Button>().onClick.Invoke();yield return .2;
        Check(manager.Tasks.Snapshot.equipment.Count==owned,"Preview must not grant equipment");
        gear.transform.Find("ConfirmEquipment/Card/Confirm").GetComponent<Button>().onClick.Invoke();yield return .3;
        Check(manager.Tasks.Snapshot.equipment.Count==owned+1,"Claim not committed");Check(!view.buttons[0].interactable&&view.ownedMarks[0].activeSelf,"Owned card remains selectable");Check(!view.particles[0].gameObject.activeSelf&&view.particles[1].gameObject.activeSelf,"Claim sparkle was not settled");
        Capture("claimed");yield return .2;
        view.buttons[1].onClick.Invoke();gear.transform.Find("ItemDetail/Popup/Button_Select").GetComponent<Button>().onClick.Invoke();gear.transform.Find("ConfirmEquipment/Card/Confirm").GetComponent<Button>().onClick.Invoke();yield return .3;
        Check(view.particles.All(p=>!p.gameObject.activeSelf),"No claims remaining must stop glow");
        Check(new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock()).Snapshot.equipment.Count==owned+2,"Claims did not persist");
        gear.transform.Find("ItemModal/Card/Close").GetComponent<Button>().onClick.Invoke();boss.closeStages.onClick.Invoke();
        int tickets=manager.Tasks.Snapshot.tickets;boss.Action();boss.cancel.onClick.Invoke();Check(manager.Tasks.Snapshot.tickets==tickets,"Cancel consumed entry");
        boss.Action();boss.Confirm();yield return .2;Check(boss.IsBattling,"Battle entry failed");boss.Action();yield return .3;Capture("stop-confirmation");yield return .2;boss.Confirm();yield return .2;
        Check(!boss.IsBattling&&manager.Tasks.Snapshot.tickets==tickets-1,"Stop must retain spent entry");
        var current=manager.PlayerData.bossProgress.Single(p=>p.bossId==1);current.wins=15;current.pendingClaims=1;manager.SaveData();
        Check(manager.Tasks.ClaimEquipment("boss1-head"),"Prepare full set in isolated save");
        nav.ShowPage(MainMenuPage.CatHome);yield return .4;
        var equipment=nav.pages[(int)MainMenuPage.CatHome].GetComponent<EquipmentPageUI>();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .3;
            manager.Tasks.Unequip(EquipmentSlot.Weapon);manager.Tasks.Unequip(EquipmentSlot.Body);manager.Tasks.Unequip(EquipmentSlot.Head);
            equipment.OpenItem("boss1-head");yield return .2;
            var detail=equipment.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
            var inactive=detail.setBonusCards[0].color;
            TextFits(detail.transform);Capture("set-inactive-"+width);yield return .2;
            manager.Tasks.Equip("boss1-weapon",EquipmentSlot.Weapon);manager.Tasks.Equip("boss1-body",EquipmentSlot.Body);equipment.OpenItem("boss1-head");yield return .2;
            Check(detail.setBonusCards[0].color!=inactive&&detail.setBonusCards[1].color==inactive,"Two-piece state must highlight only its earned effect");
            TextFits(detail.transform);Capture("set-two-"+width);yield return .2;
            manager.Tasks.Equip("boss1-head",EquipmentSlot.Head);equipment.OpenItem("boss1-head");yield return .2;
            Check(detail.setBonusCards.All(c=>c.color!=inactive),"Three-piece state must highlight both effects");
            Check(detail.setBonusTitles.All(t=>!t.text.Contains("ACTIVE")),"Remove ACTIVE text");
            TextFits(detail.transform);Capture("set-three-"+width);yield return .2;
            detail.close.onClick.Invoke();
        }
        File.WriteAllText(Folder+"/"+SessionState.GetString(Flag+"mode","")+"/checks.txt","PASS: prefab missing-script scan; both-size text overflow and full-screen raycast blocking; 0/4/5/9/10/11/14/15/30 wins; unscaled sparkles and reopen reset; hover/press/repeated-input/exit/submit/disable/focus-loss feedback; two authoritative claims, owned cards and particle shutdown; reload; entry cancel and stop ticket preservation.");
    }
}
