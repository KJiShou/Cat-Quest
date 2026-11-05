using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LevelUpPlayVerification
{
    const string Flag="CatQuest.LevelUpPlay", Variable="CATQUEST_TASK_TEST_STORE";
    static IEnumerator flow;
    static double next;
    static string Folder=>SessionState.GetString(Flag+".folder","");
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo Size=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object Group()=>typeof(FocusLayoutVerification).GetMethod("SizeGroup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static LevelUpPlayVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){flow=Run();next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode){
                Environment.SetEnvironmentVariable(Variable,SessionState.GetBool(Flag+".hadEnv",false)?SessionState.GetString(Flag+".env",""):null);
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));
                Time.timeScale=SessionState.GetFloat(Flag+".scale",1);
                Size.SetValue(View,SessionState.GetInt(Flag+".size",0));
                var group=Group();int builtin=Count(group,"GetBuiltinCount");
                for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)
                    group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
                EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);
                File.WriteAllText(Folder+"/done.txt","Restored save environment, start scene, Game View, pause and time scale.");
                SessionState.SetBool(Flag,false);flow=null;
            }
        };
    }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)&&EditorApplication.isPlaying&&flow!=null){
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+".deadline","0"))){Finish("FAIL: timeout");return;}
            if(EditorApplication.timeSinceStartup<next)return;
            try { if(flow.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(flow.Current);else Finish(SessionState.GetBool(Flag+".before",false)?"PASS: before/reference captures only.":"PASS: reference rendering; persisted level-five and multi-level rewards; queued/dismissed modal; unscaled intro/settle, keyboard, raycast blocking, disable/focus reset, Daily Reward previews/claimed receipt; four viewport sizes and intro frames."); }
            catch(Exception e){Finish("FAIL: "+e);Debug.LogException(e);}return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-level-up-ui.request"))return;
        SessionState.SetBool(Flag+".before",File.ReadAllText("Temp/verify-level-up-ui.request").Trim()=="before");
        File.Delete("Temp/verify-level-up-ui.request");
        string folder="Library/LevelUpPlayVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);string previous=Environment.GetEnvironmentVariable(Variable);
        SessionState.SetBool(Flag+".hadEnv",previous!=null);SessionState.SetString(Flag+".env",previous??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)Size.GetValue(View));SessionState.SetInt(Flag+".count",Count(Group(),"GetCustomCount"));
        SessionState.SetFloat(Flag+".scale",Time.timeScale);SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(150).Ticks.ToString());SessionState.SetBool(Flag,true);
        Environment.SetEnvironmentVariable(Variable,Path.GetFullPath(folder+"/save.json"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        EditorApplication.isPaused=false;Resolution(1920,1080);EditorApplication.isPlaying=true;
    }
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("o")+" | Unity "+Application.unityVersion+"\n"+result);flow=null;EditorApplication.isPlaying=false;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/"+name+".png"));
    static void Seed(int xp){var manager=GameDataManager.Instance;manager.PlayerData.experience=xp;manager.SaveData();var flight=UnityEngine.Object.FindFirstObjectByType<RewardFlightUI>();flight.enabled=false;flight.enabled=true;}
    static void CheckBackground(LevelUpCelebrationUI ui, Image reference)
    {
        Canvas.ForceUpdateCanvases();
        var background=ui.panel.GetComponent<Image>();
        Check(background.enabled && background.sprite==reference.sprite && background.color==reference.color && background.color.a==1,
            "Full-viewport panel must render the opaque SimpleCasual background");
        var corners=new Vector3[4];background.rectTransform.GetWorldCorners(corners);
        var min=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var max=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
        Check(min.x<=.5f && min.y<=.5f && max.x>=Screen.width-.5f && max.y>=Screen.height-.5f,"Level-up background leaves viewport edges exposed");
        Check(ui.footer && ui.footerSource,"Missing responsive lower background binding");
        ui.footer.GetWorldCorners(corners);
        var bottomLeft=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var topRight=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
        Check(bottomLeft.x<=.5f && bottomLeft.y<=.5f && topRight.x>=Screen.width-.5f,"Lower background does not reach viewport edges");
        var hits=new System.Collections.Generic.List<RaycastResult>();
        foreach(var point in new[]{new Vector2(1,1),new Vector2(1,Screen.height-1),new Vector2(Screen.width-1,1),new Vector2(Screen.width-1,Screen.height-1)}) {
            hits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Check(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(ui.panel.transform),"Level-up corner leaked input");
        }
    }
    static IEnumerator Run()
    {
        var manager=GameDataManager.EnsureInstance();var service=manager.Tasks;
        Check(manager.SavePath.Contains("LevelUpPlayVerification"),"Must use isolated save");
        var ui=UnityEngine.Object.FindFirstObjectByType<LevelUpCelebrationUI>();Check(ui,"Missing editable level-up prefab binding");
        var tutorial=UnityEngine.Object.FindFirstObjectByType<TutorialController>();if(tutorial)tutorial.enabled=false;
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/LevelUp.prefab");
        var reference=(GameObject)PrefabUtility.InstantiatePrefab(source,ui.panel.transform);
        var rect=reference.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(2560,1440);rect.anchoredPosition=Vector2.zero;rect.localScale=ui.design.localScale;
        ui.design.gameObject.SetActive(false);ui.panel.SetActive(true);yield return .5;Capture("reference-1920");yield return .3;
        UnityEngine.Object.DestroyImmediate(reference);ui.design.gameObject.SetActive(true);ui.panel.SetActive(false);
        if(SessionState.GetBool(Flag+".before",false)) {
            ui.panel.SetActive(true);ui.level.text="5";ui.fish.text="+25";ui.tickets.text="+1";
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
                Resolution(size.x,size.y);yield return .5;Capture("before-level5-"+size.x);yield return .3;
            }
            yield break;
        }
        Seed(549);yield return .2;
        Time.timeScale=0;
        Check(service.Upsert(new QuestTask{title="Level-five test",difficulty=TaskDifficulty.Hard},null,out var id)&&service.Complete(id,true),service.LastError);
        int balance=service.Snapshot.fishCoins;
        for(int i=0;i<100&&!ui.IsShowing;i++)yield return .05;
        Check(ui.IsShowing&&ui.level.text=="5"&&ui.fish.text=="+25"&&ui.tickets.text=="+1"&&ui.ticketCard.activeSelf,"Milestone display");
        for(int frame=0;frame<20;frame++){Capture("intro-"+frame.ToString("00"));yield return .05;}
        Check(ui.continueButton.interactable&&Math.Abs(ui.badge.localScale.x-1)<.001&&ui.fade.alpha==1,"Unscaled animation did not settle");
        var feedback=ui.continueButton.GetComponent<JuicyButton>();Check(feedback,"Missing shared button feedback");
        var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
        var hitArea=(RectTransform)ui.continueButton.transform;var hitScale=hitArea.localScale;var hitPosition=hitArea.anchoredPosition;
        feedback.OnPointerEnter(pointer);yield return .2;Check(feedback.DrawScale.y>1,"Hover feedback missing");Capture("button-hover");yield return .1;
        feedback.OnPointerDown(pointer);yield return .2;Check(feedback.DrawScale.y<1,"Press feedback missing");Capture("button-pressed");yield return .1;
        feedback.OnPointerUp(pointer);feedback.OnPointerExit(pointer);yield return .2;
        Check(hitArea.localScale==hitScale&&hitArea.anchoredPosition==hitPosition,"Button motion moved hit area");
        for(int i=0;i<5;i++){feedback.OnPointerDown(pointer);feedback.OnPointerUp(pointer);}
        ui.fade.interactable=false;yield return .5;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01,"Disabled feedback did not settle");
        ui.fade.interactable=true;feedback.SendMessage("OnApplicationFocus",false);
        Check(feedback.DrawScale==Vector2.one,"Button focus-loss reset");
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=new Vector2(20,20)},hits);
        Check(hits.Count>0&&hits[0].gameObject.transform.IsChildOf(ui.panel.transform),"Popup leaked background input");
        var side=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var expand=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(side);
        var instant=typeof(ResisesSideMenuInLayoutGroup).GetField("instantChange",BindingFlags.Instance|BindingFlags.NonPublic);
        bool oldExpanded=expand.isOn,oldInstant=(bool)instant.GetValue(side);instant.SetValue(side,true);
        var sourceBackground=source.transform.Find("Background_Gradatient_Sky").GetComponent<Image>();
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
            Resolution(size.x,size.y);yield return .5;
            foreach(bool expanded in new[]{false,true}) {
                expand.isOn=expanded;yield return .2;CheckBackground(ui,sourceBackground);
                Capture("level5-"+size.x+"-"+(expanded?"expanded":"collapsed"));yield return .3;
            }
        }
        expand.isOn=oldExpanded;instant.SetValue(side,oldInstant);Resolution(1280,720);yield return .5;
        ((ISubmitHandler)ui.continueButton).OnSubmit(new BaseEventData(EventSystem.current));yield return .3;
        ui.Dismiss();ui.Dismiss();Check(!ui.IsShowing&&service.Snapshot.fishCoins==balance,"Dismiss paid or replayed reward");
        Check(service.Complete(id,true)&&ui.PendingCount==0,"Duplicate completion queued popup");
        // Current Focus rewards cannot span several levels and Focus is locked at Lv.1.
        // Exercise the same committed multi-level boundary as RewardBalanceVerification.
        Seed(99);Action<PlayerData> award=data=>data.experience+=300;
        Check((bool)typeof(TaskService).GetMethod("Commit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(service,new object[]{award}),service.LastError);
        for(int i=0;i<100&&!ui.IsShowing;i++)yield return .05;
        yield return .8;Check(ui.level.text=="4"&&ui.fish.text=="+45"&&!ui.ticketCard.activeSelf&&ui.info.text.Contains("1"),"Multi-level presentation");
        Capture("multi-level-1280");yield return .3;
        ui.SendMessage("OnApplicationFocus",false);yield return .1;Check(Math.Abs(ui.badge.localScale.x-1)<.001,"Focus loss accumulated scale");
        Seed(749);
        Check(service.Upsert(new QuestTask{title="Queued level"},null,out var queuedId)&&service.Complete(queuedId,true),service.LastError);
        Check(ui.PendingCount==1&&ui.level.text=="4","Consecutive level replaced the visible celebration");
        ui.Dismiss();ui.Dismiss();
        for(int i=0;i<100&&!ui.IsShowing;i++)yield return .05;
        yield return .8;Check(ui.IsShowing&&ui.level.text=="6"&&ui.fish.text=="+30"&&ui.PendingCount==0,"Queued level missing or duplicated");
        ui.enabled=false;ui.enabled=true;yield return .2;Check(!ui.IsShowing&&ui.PendingCount==0,"Re-enable replayed historical reward");
        Check(service.ClaimDailyGoal(DailyRewardGoal.FirstQuest),service.LastError);
        Seed(13050);
        for(int i=0;i<2;i++){service.Upsert(new QuestTask{title="Daily goal "+i},null,out var task);service.Complete(task,true);}
        var goal=UnityEngine.Object.FindObjectsByType<DailyGoalUI>(FindObjectsSortMode.None).Single(g=>g.goal==DailyRewardGoal.ThreeQuests);
        Check(goal.experienceReward.text=="78"&&goal.fishReward.text=="39"&&goal.claim.interactable,"Daily reward bindings/level preview");
        Capture("daily-ready-1280");yield return .3;Resolution(1920,1080);yield return .5;Capture("daily-ready-1920");yield return .3;
        goal.claim.onClick.Invoke();yield return .3;
        Check(service.DailyGoalClaimed(goal.goal)&&goal.experienceReward.text=="78"&&goal.fishReward.text=="39","Claimed daily reward changed");
        int coins=service.Snapshot.fishCoins;goal.claim.onClick.Invoke();Check(service.Snapshot.fishCoins==coins,"Repeated claim paid");
        Capture("daily-claimed-1920");yield return .3;
    }
}
