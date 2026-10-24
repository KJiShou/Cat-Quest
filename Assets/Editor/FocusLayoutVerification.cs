using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Opt-in check, using a disposable save rather than the player's quests or rewards.
[InitializeOnLoad]
public static class FocusLayoutVerification
{
    const string Flag = "CatQuest.FocusLayoutCheck";
    const string Folder = "Library/FocusLayoutVerification";
    static int step;
    static double next;
    static RewardScreenshotCapture capture;
    static string questId;
    static FileStream saveLock;
    static string FolderNow => SessionState.GetString(Flag + ".folder", Folder);
    static bool Before => SessionState.GetBool(Flag + ".before", false);
    static EditorWindow View => EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty => View.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static object SizeGroup()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static FocusLayoutVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = 0; next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) {
                saveLock?.Dispose(); saveLock=null;
                SessionState.SetBool(Flag, false);
                Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", SessionState.GetBool(Flag+".hadStore",false)?SessionState.GetString(Flag+".store",""):null);
                var scene=SessionState.GetString(Flag+".scene","");
                EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
                SizeProperty.SetValue(View,SessionState.GetInt(Flag+".size",0));
                var group=SizeGroup();int builtin=Count(group,"GetBuiltinCount");
                for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+".count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
                EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);
                File.WriteAllText(FolderNow + "/done.txt", "Returned to Edit Mode; restored save environment, start scene, pause and Game View.");
            }
        };
    }
    static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
    static void Resolution(int width, int height) => typeof(DashboardRewardPlayVerification).GetMethod("Resolution", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { width, height });
    static void Capture(string name)
    {
        if (!capture) capture = new GameObject("Focus Layout Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Focus Play Mode verification exceeded 180 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { Tick(); } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-focus-layout.request")) return;
        SessionState.SetBool(Flag+".before",File.ReadAllText("Temp/verify-focus-layout.request").Trim()=="before");
        File.Delete("Temp/verify-focus-layout.request");
        string folder=Folder+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+(Before?"-before":"-after");Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);
        string previous=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        SessionState.SetBool(Flag+".hadStore",previous!=null);SessionState.SetString(Flag+".store",previous??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(View));SessionState.SetInt(Flag+".count",Count(SizeGroup(),"GetCustomCount"));
        SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(180).Ticks.ToString());
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(folder+"/save.json"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        SessionState.SetBool(Flag, true); Resolution(1920, 1080); EditorApplication.isPaused=false; EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var panel = nav.pages[2].GetComponent<FocusPanelUI>();
        var service = GameDataManager.Instance.Tasks;
        var focus = FocusRunner.EnsureInstance().Controller;
        Check(GameDataManager.Instance.SavePath.Contains("FocusLayoutVerification"), "Verification must use isolated save");
        if (Before)
        {
            if(step==0)nav.ShowPage(MainMenuPage.Focus);
            if(step==1)Capture("before-1920");
            if(step==2)Resolution(1280,720);
            if(step==3)Capture("before-1280");
            if(step==4){File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+"\nCaptured current authored Focus before UI patch.");EditorApplication.isPlaying=false;return;}
            step++;next=EditorApplication.timeSinceStartup+1;return;
        }
        if (step == 0) { Check(nav.ShowPage(MainMenuPage.Focus), "Cannot open Focus"); }
        if (step == 1) {
            Check((!panel.status || !panel.status.gameObject.activeSelf) && (!panel.message || !panel.message.gameObject.activeSelf), "Extra labels on sky");
            var heading=panel.transform.Find("Text");
            Check((!heading || !heading.gameObject.activeSelf) && (!panel.dashboard || !panel.dashboard.gameObject.activeInHierarchy), "Page heading or Return to Dashboard still visible");
            var preview = panel.GetComponentInChildren<FocusScenePreview>();
            Check(preview.transform.IsChildOf(panel.transform.Find("background")), "Scene must be inside Focus background");
            Check(preview.GetComponent<RectTransform>().anchorMin == Vector2.zero && preview.GetComponent<RectTransform>().anchorMax == Vector2.one, "Scene does not fill frame");
            Check(service.Upsert(new QuestTask { title = "Read chapter", daily = false }, null, out questId), service.LastError);
            Check(service.Upsert(new QuestTask { title = "Practice piano", daily = false }, null, out _), service.LastError);
            panel.focusDuration.value = 37; panel.restDuration.value = 7;
            Check(focus.Options.focusMinutes == 37 && focus.Options.restMinutes == 7, "Sliders not bound");
            Check(panel.rewards.text == "37" && panel.fishReward.text == "14" && panel.ticketReward.text == "1", "Reward numbers incorrect");
            Capture("focus-ready-1920");
        }
        if (step == 2) {
            panel.questDropdown.onClick.Invoke(); panel.questSearch.text = "READ";
            var buttons = panel.questList.GetComponentsInChildren<Button>();
            Check(buttons.Length == 2 && buttons.Any(b => b.name == questId), "Case-insensitive quest search failed");
            Capture("focus-search-1920");
        }
        if (step == 3) {
            panel.questSearch.text = "zzz-no-match";
            Check(panel.questList.GetComponentsInChildren<Button>().Length == 1, "Empty search should keep free focus");
            panel.questSearch.text = "chapter";
        }
        if (step == 4) {
            panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == questId).onClick.Invoke();
            Check(focus.Options.questId == questId && !panel.questPopup.gameObject.activeSelf && panel.questSearch.text == "", "Selection did not close or bind");
            panel.start.onClick.Invoke();
            Check(focus.Phase == FocusPhase.Focusing && !panel.questSearch.interactable && !panel.restDuration.interactable, "Session settings not locked");
            Capture("focus-running-1920");
        }
        if (step == 5) {
            for (int i=0; i<focus.FocusSeconds && focus.Phase==FocusPhase.Focusing; i++) focus.Advance(1);
            Check(focus.Phase == FocusPhase.Resting && focus.RestSeconds == 420 && panel.rewards.text == "37", "Rest/rewards changed");
            Capture("focus-rest-1920");
        }
        if (step == 6) {
            panel.endRest.onClick.Invoke(); Resolution(1280, 720);
            Check(panel.questSearch.interactable && panel.focusDuration.interactable, "Settings not unlocked");
        }
        if (step == 7) Capture("focus-ready-1280");
        if(step==8){panel.stopwatchMode.isOn=true;Check(focus.CountsUp && !panel.focusDurationCard.activeSelf && !panel.restDurationCard.activeSelf && panel.rewards.text=="0","Stopwatch settings incorrect");Capture("stopwatch-ready-1280");}
        if(step==9){panel.start.onClick.Invoke();for(int i=0;i<14400;i++)focus.Advance(1);Check(focus.Phase==FocusPhase.Focusing && panel.stopwatchMode.interactable==false,"Stopwatch limit/lock wrong");}
        if(step==10)Capture("stopwatch-four-hours-1280");
        if(step==11){panel.endEarly.onClick.Invoke();Check(focus.Phase==FocusPhase.Finished && focus.LastResult.experience==180,"Stopwatch saved incorrectly");Capture("stopwatch-saved-1280");}
        if(step==12){panel.countdownMode.isOn=true;panel.focusDuration.value=180;Check(focus.Options.focusMinutes==180 && !panel.restDurationCard.activeSelf,"Countdown settings incorrect");Capture("countdown-ready-1280");}
        if(step==13){Resolution(1920,1080);}
        if(step==14)Capture("countdown-ready-1920");
        if(step==15){panel.start.onClick.Invoke();for(int i=0;i<10800;i++)focus.Advance(1);Check(focus.Phase==FocusPhase.Finished && service.TodayRewards.focusMinutes==360,"Mixed mode cap incorrect");Capture("countdown-saved-1920");}
        if(step==16){panel.stopwatchMode.isOn=true;Capture("stopwatch-ready-1920");}
        if(step==17){panel.start.onClick.Invoke();for(int i=0;i<3600;i++)focus.Advance(1);}
        if(step==18)Capture("stopwatch-capped-1920");
        if(step==19){panel.cancel.onClick.Invoke();Check(focus.Phase==FocusPhase.Interrupted && service.TodayRewards.focusMinutes==360,"Cancel changed allowance");}
        if(step==20){
            panel.start.onClick.Invoke();for(int i=0;i<300;i++)focus.Advance(1);
            saveLock=File.Open(GameDataManager.Instance.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            panel.endEarly.onClick.Invoke();Check(focus.Phase==FocusPhase.Saving,"Locked save did not enter retry state");
        }
        if(step==21){Check(panel.allowance.gameObject.activeInHierarchy && panel.allowance.text.Contains("Retry") && panel.retry.gameObject.activeInHierarchy,"Save error/retry not visible");Capture("save-failure-1920");}
        if(step==22){saveLock.Dispose();saveLock=null;panel.retry.onClick.Invoke();Check(focus.Phase==FocusPhase.Finished,"Retry did not finish");}
        if(step==23)Capture("save-retried-1920");
        if(step==24){
            var data=new BaseEventData(EventSystem.current);
            EventSystem.current.SetSelectedGameObject(panel.countdownMode.gameObject);
            panel.countdownMode.OnSubmit(data);
            Check(focus.Options.mode==FocusTimerMode.Countdown,"Keyboard mode submit failed");
            var results=new System.Collections.Generic.List<RaycastResult>();
            var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,((RectTransform)panel.stopwatchMode.transform).TransformPoint(new Vector3(((RectTransform)panel.stopwatchMode.transform).rect.width * .25f,0,0))),button=PointerEventData.InputButton.Left};
            EventSystem.current.RaycastAll(pointer,results);
            Check(results.Count>0 && results[0].gameObject.transform.IsChildOf(panel.stopwatchMode.transform),"Mode label blocked from raycasts");
            panel.stopwatchMode.OnPointerClick(pointer);Check(focus.CountsUp,"Mode row click failed");
            nav.ShowPage(MainMenuPage.Dashboard);nav.ShowPage(MainMenuPage.Focus);
            panel.pomodoroMode.OnSubmit(data);Check(focus.Options.mode==FocusTimerMode.Pomodoro,"Re-enabled mode listener missing");
        }
        if(step>=25 && step<=32){
            var juicy=panel.start.GetComponent<JuicyButton>();Check(juicy,"Start action lost shared feedback");
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            if(step==25)juicy.OnPointerEnter(pointer);
            if(step==27)juicy.OnPointerDown(pointer);
            if(step==29)juicy.OnPointerUp(pointer);
            if(step==30){juicy.OnPointerExit(pointer);EventSystem.current.SetSelectedGameObject(null);}
            Capture("motion-"+(step-25).ToString("D2"));
            if(step==32)Check(Vector2.Distance(juicy.DrawScale,Vector2.one)<.015f && Mathf.Abs(juicy.DrawOffset)<.1f,"Feedback did not settle");
        }
        if(step==33){
            // Render the exact icon-menu reference used by the authored mode cards.
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Buttons/Btn_MenuButton_Rectangle01-Group.prefab");
            var reference=UnityEngine.Object.Instantiate(source.transform.Find("Btn_MenuButton_Rectangle01_Light").gameObject,panel.transform);reference.name="ReferenceOnly";
            var rect=(RectTransform)reference.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one;
        }
        if(step==34)Capture("simplecasual-reference-1920");
        if (step == 35) {
            UnityEngine.Object.Destroy(panel.transform.Find("ReferenceOnly").gameObject);
            panel.stopwatchMode.isOn=true;
        }
        if (step >= 36 && step <= 43) {
            var mode=panel.countdownMode;
            var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
            if(step==36)mode.OnPointerEnter(pointer);
            if(step==37)mode.OnPointerDown(pointer);
            if(step==38){mode.OnPointerUp(pointer);mode.OnPointerClick(pointer);Check(focus.Options.mode==FocusTimerMode.Countdown,"Mode card click did not select countdown");}
            if(step==39){mode.OnPointerExit(pointer);EventSystem.current.SetSelectedGameObject(null);}
            if(step==40){for(int i=0;i<12;i++){panel.pomodoroMode.OnPointerClick(pointer);mode.OnPointerClick(pointer);}Check(mode.isOn&&!panel.pomodoroMode.isOn&&!panel.stopwatchMode.isOn,"Rapid input broke exclusive mode selection");}
            if(step==41){panel.start.onClick.Invoke();panel.pomodoroMode.OnPointerClick(pointer);Check(focus.Options.mode==FocusTimerMode.Countdown&&!mode.interactable,"Locked mode changed");}
            if(step==42){panel.cancel.onClick.Invoke();nav.ShowPage(MainMenuPage.Dashboard);nav.ShowPage(MainMenuPage.Focus);}
            if(step==43){
                Check(mode.isOn&&mode.interactable,"Mode did not survive page reopen");
                foreach(var choice in new[]{panel.pomodoroMode,panel.stopwatchMode,panel.countdownMode}){
                    Check(choice.transform.Find("ModeIcon").GetComponent<Image>().sprite,"Mode icon missing");
                    foreach(var label in choice.GetComponentsInChildren<TMPro.TMP_Text>())Check(!label.isTextOverflowing,"Mode caption clipped: "+label.text);
                }
            }
            Capture("mode-feedback-"+step);
        }
        if (step == 44) {
            File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\nPASS: full-frame scene, custom Focus/Rest, reward preview/settlement, quest search/selection, settings locks, three modes, four-hour stopwatch, mixed daily cap, cancel, save error/retry, mode keyboard/pointer handlers, label raycast and disable/re-enable, shared button feedback settling, mode card icons/captions, rapid exclusive selection and disabled/reopened cards; rendered captures at 1920x1080 and 1280x720. Visual/motion acceptance requires inspection.");
            EditorApplication.isPlaying = false; return;
        }
        step++; next = EditorApplication.timeSinceStartup + (step>=25 && step<=32 ? .15 : 1);
    }
}
