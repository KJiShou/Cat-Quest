using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Explicit, bounded Play Mode checks; never saves the scene or uses the live save.
[InitializeOnLoad]
public static class TutorialVerification
{
    const string Flag="CatQuest.TutorialVerification";
    const string Request="Temp/verify-tutorial.request";
    static IEnumerator run;
    static double next;
    static RewardScreenshotCapture capture;
    static Keyboard keyboard;
    static string Folder=>SessionState.GetString(Flag+".folder", "");
    static bool Before=>SessionState.GetBool(Flag+".before",false);
    static bool Intro=>SessionState.GetBool(Flag+".intro",false);
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object Group()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static TutorialVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){run=Flow();next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode)Restore();
        };
    }
    static void Restore()
    {
        run=null;
        if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
        var scene=SessionState.GetString(Flag+".scene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        SizeProperty.SetValue(View,SessionState.GetInt(Flag+".size",0));
        var group=Group();int builtin=Count(group,"GetBuiltinCount");
        for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
        EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);
        SessionState.SetBool(Flag,false);
        File.WriteAllText(Folder+"/restored.txt",DateTime.UtcNow.ToString("O")+" Restored save environment, start scene, Game View and pause state.");
    }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)){
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))){Finish("FAIL: timed out");return;}
            if(!EditorApplication.isPlaying||run==null)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try{if(!run.MoveNext()){Finish("PASS: "+(Before?"before/reference captures":"targeted checks and after captures"));return;}next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);}
            catch(Exception e){Finish("FAIL: "+e);}
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        string mode=File.ReadAllText(Request).Trim();bool before=mode=="before";File.Delete(Request);
        bool intro=mode=="intro" || mode=="intro-before";
        string folder="Library/TutorialVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+mode;Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);SessionState.SetBool(Flag+".before",before);
        SessionState.SetBool(Flag+".intro",intro);SessionState.SetBool(Flag+".introBefore",mode=="intro-before");
        SessionState.SetBool(Flag+".equipment",mode=="equipment");
        var store=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");SessionState.SetBool(Flag+".hadStore",store!=null);SessionState.SetString(Flag+".store",store??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(View));SessionState.SetInt(Flag+".count",Count(Group(),"GetCustomCount"));SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(300).Ticks.ToString());SessionState.SetBool(Flag,true);
        try{
            File.WriteAllText(folder+"/result.txt",DateTime.UtcNow.ToString("O")+" RUNNING");
            if(!before&&!intro&&mode!="equipment")TutorialRulesVerification.Run();
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(folder+"/save.json"));

            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(intro?"Assets/Scenes/IntroStory.unity":"Assets/Scenes/CharacterHome.unity");
            EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }catch(Exception e){Finish("FAIL starting: "+e);}
    }
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+" "+result);run=null;if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else Restore();}
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Capture(string name){if(!capture)capture=new GameObject("Task feedback capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Key(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    static void Submit(Button button){ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);}
    static void Visible(RectTransform rect)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
        foreach(var corner in corners){var point=RectTransformUtility.WorldToScreenPoint(null,corner);Check(canvas.pixelRect.Contains(point),"UI extends outside viewport: "+rect.name);}
    }
    sealed class CountingSource : ILeaderboardSource
    {
        public int loads;
        public IEnumerator Load(LeaderboardKind kind,Action<LeaderboardSnapshot> complete) { loads++; complete(new LeaderboardSnapshot{kind=kind});yield break; }
        public void Cancel() { }
    }
    sealed class CountingHttp : IAccountHttp
    {
        public int calls;
        public IEnumerator Send(string url,string method,string body,string contentType,string bearer,Action<AccountHttpResult> complete) {calls++;yield break;}
        public void Cancel() { }
    }
    static GameObject Hit(RectTransform rect)
    {
        var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        var hits=new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        return hits.Count>0 ? hits[0].gameObject : null;
    }
    static void Click(RectTransform target)
    {
        var hit=Hit(target);Check(hit,"No pointer hit for "+target.name);
        ExecuteEvents.ExecuteHierarchy(hit,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
    }
    static void FinishSpeech(TutorialController tutorial) { if(tutorial.IsTyping) Click((RectTransform)tutorial.typingShield.transform); }
    static void SpotlightBlocks(TutorialController tutorial, Button unrelated)
    {
        FinishSpeech(tutorial);
        Check(tutorial.spotlight && tutorial.spotlight.gameObject.activeInHierarchy,"Missing spotlight/input shield");
        var hit=Hit((RectTransform)unrelated.transform);
        Check(hit==tutorial.spotlight.gameObject,"Pointer escaped spotlight to "+unrelated.name+"; actual="+(hit?hit.name:"none")+"; topic="+tutorial.CurrentTopic+"; point="+RectTransformUtility.WorldToScreenPoint(null,unrelated.transform.TransformPoint(((RectTransform)unrelated.transform).rect.center))+"; screen="+Screen.width+"x"+Screen.height+"; active="+unrelated.gameObject.activeInHierarchy);
        Check(!tutorial.later.gameObject.activeSelf && tutorial.nextLabel.text=="Continue","Unexpected tutorial actions");
    }
    static IEnumerator Flow()
    {
        if(Intro)
        {
            var input=UnityEngine.Object.FindFirstObjectByType<UsernameInputController>();
            var story=UnityEngine.Object.FindFirstObjectByType<StoryController>();
            var username=(TMP_InputField)typeof(UsernameInputController).GetField("usernameInput",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input);
            username.text="Apprentice";input.ConfirmUsername();yield return .5;
            var state=typeof(StoryController).GetField("currentState",BindingFlags.Instance|BindingFlags.NonPublic);
            var step=typeof(StoryController).GetField("currentStep",BindingFlags.Instance|BindingFlags.NonPublic);
            var advance=typeof(StoryController).GetMethod("HandleAdvanceInput",BindingFlags.Instance|BindingFlags.NonPublic);
            int guard=0;
            while(guard++<160) {
                var current=(StoryStep)step.GetValue(story);
                if(current?.character?.DisplayName=="Master Loke"&&state.GetValue(story).ToString()=="Typing")break;
                advance.Invoke(story,null);yield return .08;
            }
            Check(guard<160,"Original story never reached Master Loke dialogue");
            yield return 1;
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}) {Resolution(size.x,size.y);yield return .5;Capture("original-story-"+size.x);yield return .3;}
            story.SkipStory();yield return 3;
            Check(UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>(),"Original Story skip failed to load CharacterHome");
            File.WriteAllText(Folder+"/checks.txt","PASS: username starts original story, original sequence reaches Master Loke, shared typewriter, original Skip loads CharacterHome.");
            yield break;
        }
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var tutorial=UnityEngine.Object.FindFirstObjectByType<TutorialController>();
        Check(nav&&tutorial,"Tutorial bindings unavailable");
        var manager=GameDataManager.Instance;var service=manager.Tasks;
        Check(manager.SavePath.Contains("TutorialVerification"),"Unsafe save path");
        yield return 2;
        if(SessionState.GetBool(Flag+".equipment",false)) {
            tutorial.Skip();
            manager.PlayerData.equipmentUnlocked=true; manager.SaveData();
            tutorial.SendMessage("OnApplicationFocus",true);
            tutorial.OpenTopic(2); yield return .5;
            FinishSpeech(tutorial); tutorial.Continue(); yield return .5;
            Check(tutorial.CurrentTopic=="equipment-open", "Missing equipment navigation explanation");
            FinishSpeech(tutorial); tutorial.Continue(); yield return .5;
            Check(nav.CurrentPage==MainMenuPage.CatHome,"Equipment guide did not navigate to Cat Home");
            FinishSpeech(tutorial);
            Resolution(1920,1080); yield return .5;
            foreach(var image in tutorial.highlight.GetComponentsInChildren<Image>()) image.type=Image.Type.Simple;
            yield return .2; Capture("equipment-highlight-before-simple"); yield return .3;
            foreach(var image in tutorial.highlight.GetComponentsInChildren<Image>()) image.type=Image.Type.Sliced;
            int coins=service.Snapshot.fishCoins, count=service.Snapshot.equipment.Count;
            for(int equipmentStep=0;equipmentStep<7;equipmentStep++) {
                Check(tutorial.CurrentTopic=="equipment-step-"+equipmentStep,"Equipment step missing: "+equipmentStep);
                FinishSpeech(tutorial);
                Check(tutorial.CurrentTarget && tutorial.highlight.gameObject.activeInHierarchy,"Equipment target missing");
                Check(tutorial.highlight.GetComponentsInChildren<Image>().All(i=>i.type==Image.Type.Sliced),"Highlight is not sliced");
                Check(Hit(tutorial.CurrentTarget)==tutorial.spotlight.gameObject,"Explanation allows equipment mutation");
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
                    Resolution(size.x,size.y); yield return .5;
                    tutorial.dialogue.body.ForceMeshUpdate();
                    Check(!tutorial.dialogue.body.isTextOverflowing,"Equipment text overflow at step "+equipmentStep);
                    Visible(tutorial.card); Capture("equipment-"+equipmentStep+"-"+size.x); yield return .3;
                    if(equipmentStep==2) {
                        var side=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
                        var expand=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(side);
                        bool previous=expand.isOn; expand.isOn=!previous; yield return .4;
                        Visible(tutorial.card); Capture("equipment-expanded-"+size.x); yield return .3; expand.isOn=previous; yield return .3;
                    }
                }
                if(equipmentStep==2) {
                    Check(new PlayerDataStore(manager.SavePath).Data.tutorial.equipmentStep==2,"Equipment progress did not survive reload");
                    tutorial.SendMessage("OnApplicationFocus",false); yield return .2;
                    Check(!tutorial.panel.activeSelf&&!tutorial.spotlight.gameObject.activeSelf,"Equipment guide did not release input");
                    tutorial.SendMessage("OnApplicationFocus",true); yield return .5;
                    Check(tutorial.CurrentTopic=="equipment-step-2","Equipment guide did not resume"); FinishSpeech(tutorial);
                }
                tutorial.Continue(); yield return .4;
            }
            Check(service.Tutorial.read.Contains("equipment")&&!tutorial.panel.activeSelf,"Equipment guide did not finish");
            Check(service.Snapshot.fishCoins==coins&&service.Snapshot.equipment.Count==count,"Equipment guide changed inventory/currency");
            tutorial.OpenTopic(2); yield return .4; FinishSpeech(tutorial); tutorial.Continue(); yield return .4;
            Check(tutorial.CurrentTopic=="equipment-step-0","Help did not restart equipment guide");
            tutorial.Skip(); Check(!tutorial.spotlight.gameObject.activeSelf,"Skip did not release spotlight");
            File.WriteAllText(Folder+"/checks.txt","PASS: seven equipment steps, navigation, read-only targets, text bounds at 1920x1080/1280x720/1024x768/2560x1080, sidebar states, saved progress reload, focus loss/resume, completion, Help replay, Skip, unchanged currency/inventory, sliced highlights.");
            yield break;
        }
        if(Before) {
            Resolution(1920,1080);yield return .5;Capture("before-welcome");yield return .3;
            tutorial.Skip(); manager.PlayerData.experience=100;manager.SaveData();
            nav.ShowPage(MainMenuPage.Focus);yield return 3;tutorial.OpenTopic(1);yield return 6;
            Capture("before-focus");yield return .3;yield break;
        }
        var http=new CountingHttp();var reader=new LeaderboardReader(AccountController.EnsureInstance(),http);LeaderboardSnapshot lockedResult=null;
        var load=reader.Load(LeaderboardKind.FocusMinutes,r=>lockedResult=r);while(load.MoveNext())yield return .01;
        Check(http.calls==0&&lockedResult!=null&&lockedResult.message.Contains("Lv.5"),"Locked reader made network request");
        var dashboard=UnityEngine.Object.FindFirstObjectByType<DashboardLeaderboardUI>();var source=new CountingSource();dashboard.SetSource(source);
        Check(source.loads==0,"Dashboard called locked leaderboard source");
        Check(!nav.ShowPage(MainMenuPage.Focus)&&!nav.ShowPage(MainMenuPage.Boss)&&!nav.ShowPage(MainMenuPage.Leaderboard),"Locked navigation bypass");
        tutorial.Continue();tutorial.Continue();yield return 1;
        tutorial.OpenTopic(0);yield return 1;
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
        {
            Resolution(size.x,size.y);yield return 1;Capture("welcome-"+size.x);yield return .3;
            Check(!tutorial.dialogue.body.isTextOverflowing,"Welcome text overflow");
            Visible((RectTransform)tutorial.next.transform);Visible((RectTransform)tutorial.skip.transform);
        }
        tutorial.Continue();tutorial.Continue();yield return .5;
        Check(tutorial.CurrentTopic=="task-open","Missing Tasks navigation explanation");
        FinishSpeech(tutorial);
        Check(!tutorial.next.interactable,"Tasks navigation should require target click");
        Click((RectTransform)nav.buttons[(int)MainMenuPage.Tasks].transform);yield return 1;
        Check(nav.CurrentPage==MainMenuPage.Tasks,"Target click did not open Tasks");
        Check(service.Tutorial.taskStep==2,"Task navigation not tracked");
        for(int stepIndex=0;stepIndex<=5;stepIndex++)
        {
            Check(tutorial.CurrentTopic=="task-page-"+stepIndex,"Task explanation order: "+stepIndex);
            if(tutorial.IsTyping)tutorial.Continue();
            yield return .5;Check(tutorial.CurrentTopic=="task-page-"+stepIndex,"Task guide advanced without Continue");
            Capture("task-step-"+stepIndex);yield return .3;
            SpotlightBlocks(tutorial,nav.buttons[(int)MainMenuPage.Dashboard]);
            if(stepIndex==0) {
                EventSystem.current.SetSelectedGameObject(nav.buttons[(int)MainMenuPage.Dashboard].gameObject);yield return .3;
                Check(EventSystem.current.currentSelectedGameObject!=nav.buttons[(int)MainMenuPage.Dashboard].gameObject,"Keyboard escaped spotlight");
            }
            if(stepIndex==4)tutorial.Continue();else {Check(!tutorial.next.interactable,"Action step allowed Continue bypass");Click(tutorial.CurrentTarget);}
            yield return .5;
        }
        Check(tutorial.tasks.QuickAddInput,"Target click did not open the task name field");
        Check(!tutorial.next.interactable,"Task save step advanced without a real save");
        Capture("task-input");yield return .3;
        var taskInput=tutorial.tasks.QuickAddInput;
        Check(Hit((RectTransform)taskInput.transform)!=tutorial.spotlight.gameObject,"Task name input blocked");
        FinishSpeech(tutorial);
        taskInput.Select();taskInput.ActivateInputField();
        taskInput.text="Read one page";
        keyboard=InputSystem.AddDevice<Keyboard>();Key(UnityEngine.InputSystem.Key.Enter);yield return .15;Key();yield return 1;
        Check(service.Snapshot.tasks.Any(t=>t.title=="Read one page"),"Task input did not save under spotlight");
        Check(service.Tutorial.taskStep==3&&service.Tutorial.taskPageStep==6&&service.Snapshot.history.Count==0,"Save advanced explanation or fabricated completion");
        for(int wait=0;wait<40 && tutorial.CurrentTopic!="task-page-6-saved";wait++)yield return .25;
        Check(tutorial.CurrentTopic=="task-page-6-saved","Saved-task explanation did not resume");
        Capture("task-saved");yield return .3;
        if(tutorial.IsTyping)tutorial.Continue();tutorial.Continue();yield return .5;
        Check(tutorial.CurrentTopic=="task-page-7","Task completion explanation missing");
        FinishSpeech(tutorial);
        var tick=tutorial.tasks.TutorialCompletionTarget(service.Tutorial.taskId);
        Check(tick && tick.rect.width < tutorial.tasks.TutorialTaskTarget(service.Tutorial.taskId).rect.width / 2 && Hit(tick)==tutorial.spotlight.gameObject,"Completion checkbox should be a precise, explanation-only target");
        if(tutorial.IsTyping)tutorial.Continue();
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
            Resolution(size.x,size.y);yield return .5;Capture("task-checkbox-"+size.x);yield return .3;
            Visible((RectTransform)tutorial.next.transform);Visible((RectTransform)tutorial.skip.transform);
        }
        if(tutorial.IsTyping)tutorial.Continue();Capture("task-checkbox");yield return .3;tutorial.Continue();yield return .5;
        Check(tutorial.CurrentTopic=="task-page-8","Task History explanation missing");
        FinishSpeech(tutorial);Click(tutorial.CurrentTarget);yield return .5;
        Check(service.Tutorial.taskStep==4&&service.Snapshot.history.Count==0,"Task guide fabricated completion");
        tutorial.OpenHelp();yield return .5;Capture("help");yield return .3;
        if(keyboard==null)keyboard=InputSystem.AddDevice<Keyboard>();
        Check(EventSystem.current.currentSelectedGameObject==tutorial.topics[0].gameObject,"Help initial keyboard selection");
        Key(UnityEngine.InputSystem.Key.Tab);yield return .15;Key();yield return .15;
        Check(EventSystem.current.currentSelectedGameObject==tutorial.topics[1].gameObject,"Help Tab navigation");
        Key(UnityEngine.InputSystem.Key.Escape);yield return .15;Key();yield return .15;
        Check(!tutorial.helpPanel.activeSelf&&!tutorial.helpBackdrop.activeSelf,"Help Escape dismissal");
        tutorial.OpenHelp();yield return .3;
        tutorial.OpenTopic(1);yield return .5;Capture("focus-locked-help");yield return .3;tutorial.Continue();tutorial.Continue();
        manager.PlayerData.experience=100;manager.SaveData();var p=service.Tutorial;p.dismissed=false;p.announcedLevel=2;service.SaveTutorial(p);
        Check(nav.ShowPage(MainMenuPage.Focus),"Lv.2 Focus navigation");yield return 5;
        tutorial.OpenTopic(1);yield return .2;
        var mentorAnimator=tutorial.mentor.CharacterAnimator;Check(mentorAnimator,"Missing animated Master Loke");
        Check(mentorAnimator.GetCurrentAnimatorStateInfo(0).IsName("sensei-talk"),"Typing does not play talk animation");
        for(int i=0;i<12;i++){Capture("talk-"+i.ToString("D2"));yield return .12;}
        Check(tutorial.mentor.CharacterAnimator,"Missing animated Master Loke");
        tutorial.Continue();yield return .2;
        Check(mentorAnimator.GetCurrentAnimatorStateInfo(0).IsName("sensei-idle"),"Skip typing did not restore idle");
        Check(tutorial.CurrentTopic=="focus-step-0","Focus skipped Pomodoro introduction");
        SpotlightBlocks(tutorial,nav.buttons[(int)MainMenuPage.Dashboard]);
        yield return 1;Check(tutorial.CurrentTopic=="focus-step-0","Focus advanced without Continue");
        for(int stepIndex=0;stepIndex<=6;stepIndex++)
        {
            Check(tutorial.CurrentTopic=="focus-step-"+stepIndex,"Focus step order incorrect: "+stepIndex);
            yield return 6;
            Check(!tutorial.dialogue.body.isTextOverflowing,"Focus text overflow at "+stepIndex);
            Capture("focus-step-"+stepIndex);yield return .3;
            if(stepIndex==0) {
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
                    Resolution(size.x,size.y);yield return .4;Capture("spotlight-focus-"+size.x);yield return .3;
                    Visible((RectTransform)tutorial.next.transform);Visible((RectTransform)tutorial.skip.transform);
                    SpotlightBlocks(tutorial,nav.buttons[(int)MainMenuPage.Dashboard]);
                    var menu=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
                    var expand=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu);
                    expand.isOn=true;yield return .4;Capture("spotlight-expanded-"+size.x);yield return .3;expand.isOn=false;yield return .3;
                }
            }
            Check(service.Tutorial.focusStep==stepIndex,"Focus step not saved");
            if(stepIndex==2) {
                var increase=tutorial.focusPage.focusIncrease;
                var hit=Hit((RectTransform)increase.transform);
                Check(hit && hit.GetComponentInParent<Button>()==increase,"Focus duration target is not clickable");
                int minutes=tutorial.focusPage.Controller.Options.focusMinutes;
                Submit(increase);yield return .3;
                Check(tutorial.focusPage.Controller.Options.focusMinutes>minutes && tutorial.CurrentTopic=="focus-step-2","Time setting failed or advanced tutorial");
            }
            if(stepIndex==6) Check(Hit((RectTransform)tutorial.focusPage.start.transform)!=tutorial.spotlight.gameObject,"Start remains blocked on action step");
            FinishSpeech(tutorial);
            if(stepIndex==6)Click(tutorial.CurrentTarget);else tutorial.Continue();yield return .4;
        }
        Check(FocusRunner.EnsureInstance().Controller.Phase==FocusPhase.Focusing,"Target click did not start Focus");
        if(tutorial.panel.activeSelf){FinishSpeech(tutorial);tutorial.Continue();}yield return .5;
        Check(FocusRunner.EnsureInstance().Controller.Cancel(),"Focus cancel failed");
        manager.PlayerData.experience=550;manager.PlayerData.tickets=1;manager.SaveData();p=service.Tutorial;p.announcedLevel=5;service.SaveTutorial(p);
        Check(nav.ShowPage(MainMenuPage.Boss),"Lv.5 Boss navigation");yield return 3;tutorial.OpenTopic(3);yield return .5;
        int ticketsBefore=service.Snapshot.tickets,battlesBefore=service.Snapshot.battles.Count;
        for(int stepIndex=0;stepIndex<=6;stepIndex++)
        {
            Check(tutorial.CurrentTopic=="boss-step-"+stepIndex,"Boss explanation order: "+stepIndex);
            if(tutorial.IsTyping)tutorial.Continue();yield return .4;
            Check(tutorial.CurrentTopic=="boss-step-"+stepIndex,"Boss advanced without Continue");
            Check(!tutorial.dialogue.body.isTextOverflowing,"Boss text overflow "+stepIndex);
            Capture("boss-step-"+stepIndex);yield return .3;
            SpotlightBlocks(tutorial,nav.buttons[(int)MainMenuPage.Dashboard]);
            if(stepIndex==3) {
                Check(tutorial.bossPage.stagePicker.activeSelf,"Stage explanation did not open selector");
                tutorial.SendMessage("OnApplicationFocus",false);yield return .2;
                Check(!tutorial.bossPage.stagePicker.activeSelf,"Guide-owned selector remained after focus loss");
                tutorial.SendMessage("OnApplicationFocus",true);yield return .5;
                Check(tutorial.CurrentTopic=="boss-step-3"&&tutorial.bossPage.stagePicker.activeSelf,"Boss did not resume same step");
            }
            if(stepIndex==4) {
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
                    Resolution(size.x,size.y);yield return .5;Capture("boss-mastery-"+size.x);yield return .3;
                    Visible((RectTransform)tutorial.next.transform);Visible((RectTransform)tutorial.skip.transform);
                }
            }
            FinishSpeech(tutorial);
            if(stepIndex==2 || stepIndex==6)Click(tutorial.CurrentTarget);else tutorial.Continue();yield return .5;
        }
        Check(tutorial.bossPage.confirmation.activeSelf,"Challenge target did not open original confirmation");
        Submit(tutorial.bossPage.cancel);yield return .3;
        Check(!tutorial.bossPage.stagePicker.activeSelf&&!tutorial.spotlight.gameObject.activeSelf,"Boss guide did not release popup/input");
        Check(service.Snapshot.tickets==ticketsBefore&&service.Snapshot.battles.Count==battlesBefore,"Boss tutorial spent tickets or entered battle");
        Check(nav.ShowPage(MainMenuPage.Leaderboard),"Lv.5 leaderboard navigation");yield return 1;tutorial.OpenTopic(4);yield return .5;
        var accountBefore=JsonUtility.ToJson(service.AccountSettings);
        for(int stepIndex=0;stepIndex<3;stepIndex++) {
            Check(tutorial.CurrentTopic=="leaderboard-step-"+stepIndex,"Leaderboard step order incorrect");
            string key=tutorial.CurrentTopic;
            if(tutorial.IsTyping) {
                Click((RectTransform)tutorial.settings.upload.transform);yield return .2;
                Check(!tutorial.IsTyping && tutorial.CurrentTopic==key,"Typing click advanced the step");
            }
            Check(JsonUtility.ToJson(service.AccountSettings)==accountBefore,"Tutorial changed sharing or display name");
            Check(Hit((RectTransform)tutorial.settings.upload.transform)==tutorial.spotlight.gameObject,"Upload toggle was interactive in explanation");
            Check(!tutorial.dialogue.body.text.Contains(";"),"Tutorial sentence contains semicolon");
            Capture("leaderboard-step-"+stepIndex);yield return .3;
            if(stepIndex==2)foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) {
                Resolution(size.x,size.y);yield return .5;Capture("leaderboard-privacy-"+size.x);yield return .3;
                Visible((RectTransform)tutorial.next.transform);Visible((RectTransform)tutorial.skip.transform);
            }
            tutorial.Continue();yield return .5;
        }
        Check(!tutorial.settings.gameObject.activeSelf&&JsonUtility.ToJson(service.AccountSettings)==accountBefore,"Leaderboard guide did not preserve settings/close owned view");
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
        manager.PlayerData.experience=0;manager.SaveData();p=service.Tutorial;service.SaveTutorial(p);
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
        {
            Resolution(size.x,size.y);yield return .5;
            foreach(bool expanded in new[]{false,true}) {toggle.isOn=expanded;yield return .5;Capture("locks-"+size.x+"-"+expanded);yield return .3;}
        }
        var gate=nav.buttons[(int)MainMenuPage.Leaderboard].GetComponent<FeatureLockUI>();
        gate.OnSelect(new BaseEventData(EventSystem.current));yield return .3;Capture("lock-keyboard-hint");yield return .3;gate.OnDeselect(new BaseEventData(EventSystem.current));
        tutorial.settings.Open(nav.gameObject);yield return .5;Capture("settings-help-entry");yield return .3;
        tutorial.OpenHelp();yield return .3;Check(tutorial.helpPanel.activeSelf&&!tutorial.settings.gameObject.activeSelf,"Settings Help handoff failed");tutorial.CloseHelp();
        p=service.Tutorial;p.dismissed=false;p.taskStep=4;p.announcedLevel=1;p.read.Add("reward");p.read.Add("daily");p.read.Add("equipment");
        manager.PlayerData.experience=550;manager.SaveData();service.SaveTutorial(p);
        typeof(TutorialController).GetField("snoozed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(tutorial,false);
        yield return 6;
        Check(tutorial.CurrentTopic=="unlock"&&nav.CurrentPage==MainMenuPage.Dashboard,"Cross-threshold unlock did not wait for user navigation");
        Capture("combined-unlock");yield return .3;tutorial.Continue();tutorial.Continue();yield return .5;
        Check(service.Tutorial.announcedLevel==5&&nav.CurrentPage==MainMenuPage.Focus,"Combined unlock ordering/receipt");
        nav.ShowPage(MainMenuPage.Dashboard);manager.PlayerData.experience=0;manager.SaveData();service.SaveTutorial(service.Tutorial);yield return 4;
        tutorial.OpenTopic(0);yield return .5;tutorial.SendMessage("OnApplicationFocus",false);yield return .2;
        Check(!tutorial.panel.activeSelf&&!tutorial.spotlight.gameObject.activeSelf,"Guide remained active after focus loss");tutorial.SendMessage("OnApplicationFocus",true);yield return .5;
        Check(tutorial.panel.activeSelf,"Guide did not recover after focus return");
        tutorial.Skip();yield return .5;
        Check(!tutorial.spotlight.gameObject.activeSelf,"Skip did not release input shield");
        Check(service.Tutorial.dismissed&&!nav.ShowPage(MainMenuPage.Focus),"Skip bypassed level locks");
        File.WriteAllText(Folder+"/checks.txt",DateTime.UtcNow.ToString("O")+" PASS: serialized guide, boundaries/navigation, real task save, no fabricated completion, help, Master animator, Focus cancellation, Boss/leaderboard access, skip remains locked. Screenshots require visual review.");
    }
}
