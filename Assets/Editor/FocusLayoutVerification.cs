using System;
using System.Collections;
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
    static string questId, tagId;
    static FileStream saveLock;
    static IEnumerator pointerFlow;
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
            if (state == PlayModeStateChange.EnteredPlayMode) { step = SessionState.GetBool(Flag+".lateOnly",false)?44:0; next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) {
                (pointerFlow as IDisposable)?.Dispose(); pointerFlow=null;
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
    // Resolve the visible topmost hit, just as the EventSystem does. Never dispatch
    // straight to a covered control: that hides modal/overlay regressions.
    static PointerEventData Pointer(Selectable control, Vector2 fraction)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)control.transform;
        var canvas = control.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var local = new Vector2(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, fraction.x), Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, fraction.y));
        return new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local)), button = PointerEventData.InputButton.Left };
    }
    static GameObject Hit(Selectable control, PointerEventData pointer)
    {
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        var target = hits.Count == 0 ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        Check(target == control.gameObject, "Blocked " + control.name + " at " + pointer.position + ": " + string.Join(" | ", hits.Select(h => AnimationUtility.CalculateTransformPath(h.gameObject.transform, null))));
        return target;
    }
    static void Click(Selectable control)
    {
        var pointer = Pointer(control, new Vector2(.5f, .5f));
        var target = Hit(control, pointer);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerExitHandler);
    }
    static bool DismissLevelUps(FocusPanelUI panel)
    {
        var modal = UnityEngine.Object.FindFirstObjectByType<LevelUpCelebrationUI>();
        if (!modal || (!modal.IsShowing && modal.PendingCount == 0)) return false;
        if (modal.IsShowing)
        {
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(Pointer(panel.stopwatchMode, new Vector2(.5f,.5f)), hits);
            Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(modal.panel.transform), "Level-up modal leaked input to Focus");
            if (modal.continueButton.interactable)
            {
                Click(modal.continueButton);
                Check(!modal.IsShowing, "Continue failed to dismiss level-up modal");
                File.AppendAllText(FolderNow+"/interactions.txt", "Confirmed modal blocking, clicked Continue through topmost raycast.\n");
            }
        }
        next = EditorApplication.timeSinceStartup + .2;
        return true;
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
        string request=File.ReadAllText("Temp/verify-focus-layout.request").Trim();
        SessionState.SetBool(Flag+".before",request=="before");
        SessionState.SetBool(Flag+".lateOnly",request=="late");
        SessionState.SetBool(Flag+".tagsOnly",request=="tags");
        SessionState.SetBool(Flag+".noticeOnly",request=="notice");
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
    static IEnumerator PointerChecks(FocusPanelUI panel, MainMenuNavigation nav)
    {
        var focus=panel.Controller;
        var oldTimeScale=Time.timeScale;
        try
        {
            Check(focus.Configure(new FocusSessionOptions()), "Cannot prepare pointer checks");
            nav.ShowPage(MainMenuPage.Focus);
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720) })
            {
                Resolution(size.x,size.y); yield return .5;
                while (DismissLevelUps(panel)) yield return .2;
                foreach (var mode in new[] {panel.pomodoroMode,panel.stopwatchMode,panel.countdownMode})
                {
                    foreach(var point in new[]{new Vector2(.25f,.5f),new Vector2(.5f,.8f),new Vector2(.5f,.2f),new Vector2(.75f,.5f)}) Hit(mode,Pointer(mode,point));
                    Click(mode); yield return .15;
                    Check(mode.isOn && new[]{panel.pomodoroMode,panel.stopwatchMode,panel.countdownMode}.Count(t=>t.isOn)==1,"Mouse did not select one exclusive mode");
                }
                Click(panel.pomodoroMode); yield return .15;
                var modePointer=Pointer(panel.countdownMode,new Vector2(.5f,.5f));
                var modeTarget=Hit(panel.countdownMode,modePointer);
                var modeRect=(RectTransform)modeTarget.transform;var position=modeRect.anchoredPosition;var scale=modeRect.localScale;
                EventSystem.current.SetSelectedGameObject(null);
                ExecuteEvents.Execute(modeTarget,modePointer,ExecuteEvents.pointerEnterHandler); yield return .2;
                float hover=panel.countdownMode.targetGraphic.canvasRenderer.GetColor().r;
                ExecuteEvents.Execute(modeTarget,modePointer,ExecuteEvents.pointerDownHandler); yield return .2;
                Check(panel.countdownMode.targetGraphic.canvasRenderer.GetColor().r<hover-.02f,"Mode press tint missing");
                Capture("pointer-mode-pressed-"+size.x); yield return .1;
                ExecuteEvents.Execute(modeTarget,modePointer,ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(modeTarget,modePointer,ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(null); yield return .3;
                Check(modeRect.anchoredPosition==position&&modeRect.localScale==scale,"Mode feedback moved hit area");
                Check(Mathf.Abs(panel.countdownMode.targetGraphic.canvasRenderer.GetColor().r-panel.countdownMode.colors.normalColor.r)<.02f,"Mode tint did not reset");
                Click(panel.questDropdown); yield return .15;
                var tag=panel.questList.GetComponentsInChildren<Button>().First(b=>b.name!="NoTags");
                Click(tag); Check(focus.Options.tagIds.Contains(tag.name),"Raycast tag click failed");
                Click(panel.questDropdown); yield return .15;
                Click(panel.questList.GetComponentsInChildren<Button>().Single(b=>b.name=="NoTags"));
                Check(!panel.questPopup.gameObject.activeSelf,"Clear did not close tags");
                var slider=panel.focusDuration; var sliderPointer=Pointer(slider,new Vector2(.65f,.5f));
                var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(sliderPointer,hits);
                Check(hits.Count>0 && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject)==slider.gameObject,"Slider pointer blocked");
                ExecuteEvents.Execute(slider.gameObject,sliderPointer,ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(slider.gameObject,sliderPointer,ExecuteEvents.pointerUpHandler);
                Check(focus.Options.focusMinutes==Mathf.RoundToInt(slider.value),"Mouse slider not bound");
                var juicy=panel.start.GetComponent<JuicyButton>();var rect=(RectTransform)panel.start.transform;var startPosition=rect.anchoredPosition;var startScale=rect.localScale;
                var pointer=Pointer(panel.start,new Vector2(.5f,.5f));var target=Hit(panel.start,pointer);
                Time.timeScale=0; EventSystem.current.SetSelectedGameObject(null);
                for(int frame=0;frame<24;frame++)
                {
                    if(frame==0)ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);
                    if(frame==6){Check(juicy.DrawScale.y>1.02f,"Unscaled hover missing");ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);}
                    if(frame==12){Check(juicy.DrawScale.y<.95f,"Unscaled press missing");ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerUpHandler);}
                    if(frame==16){ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerExitHandler);EventSystem.current.SetSelectedGameObject(null);}
                    Capture("pointer-motion-"+size.x+"-"+frame.ToString("D2"));
                    File.AppendAllText(FolderNow+"/motion.csv",$"{size.x},{frame},{Time.unscaledTime:F3},{juicy.DrawScale.x:F4},{juicy.DrawScale.y:F4},{juicy.DrawOffset:F4}\n");
                    yield return .05;
                }
                Check(Vector2.Distance(juicy.DrawScale,Vector2.one)<.015f&&Mathf.Abs(juicy.DrawOffset)<.1f,"Release/exit failed to settle");
                Check(rect.anchoredPosition==startPosition&&rect.localScale==startScale,"Motion moved start hit area");
                for(int i=0;i<12;i++){ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerUpHandler);}
                panel.start.interactable=false;yield return .4;
                Check(Vector2.Distance(juicy.DrawScale,Vector2.one)<.015f,"Disabled feedback did not settle");panel.start.interactable=true;
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);
                juicy.SendMessage("OnApplicationFocus",false);Check(juicy.DrawScale==Vector2.one,"Focus-loss feedback reset failed");
                ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);
                nav.ShowPage(MainMenuPage.Dashboard);nav.ShowPage(MainMenuPage.Focus);yield return .2;
                Check(juicy.DrawScale==Vector2.one&&juicy.DrawOffset==0,"Page close/reopen left feedback pressed");
                Time.timeScale=oldTimeScale;
                Click(panel.start); yield return .2;
                var previous=focus.Options.mode;Click(panel.stopwatchMode);
                Check(focus.Phase==FocusPhase.Focusing&&focus.Options.mode==previous&&!panel.stopwatchMode.interactable,"Disabled mode accepted mouse input");
                focus.Cancel();yield return .2;
                File.AppendAllText(FolderNow+"/interactions.txt",$"PASS {size.x}x{size.y}: topmost raycasts, modes, tags, slider, hover/press/release, stable hit area, unscaled spring, rapid input, disabled/page/focus reset. Pointer events dispatched through EventSystem; OS hardware mouse not driven.\n");
            }
        }
        finally { Time.timeScale=oldTimeScale; }
    }
    static void Tick()
    {
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var panel = nav.pages[2].GetComponent<FocusPanelUI>();
        var service = GameDataManager.Instance.Tasks;
        var focus = FocusRunner.EnsureInstance().Controller;
        Check(GameDataManager.Instance.SavePath.Contains("FocusLayoutVerification"), "Verification must use isolated save");
        if (SessionState.GetBool(Flag+".noticeOnly", false))
        {
            if (step == 0)
            {
                UnityEngine.Object.FindFirstObjectByType<TutorialController>().Skip();
                var player = GameDataManager.Instance.PlayerData;
                player.experience = 100;
                var today = service.TodayRewards;
                player.rewardDays.RemoveAll(day => day.day == today.day);
                today.focusMinutes = service.FocusDailyMinuteLimit;
                today.focusTickets = TaskService.FocusTicketCap;
                player.rewardDays.Add(today);
                GameDataManager.Instance.SaveData();
                Check(nav.ShowPage(MainMenuPage.Focus), "Cannot open unlocked Focus page");
                panel.stopwatchMode.isOn = true;
            }
            if (step == 1 || step == 6)
            {
                Click(panel.start);
                Check(focus.Phase == FocusPhase.Focusing, "Start Focus did not begin a session");
            }
            if (step == 2 || step == 7)
            {
                Click(panel.cancel);
                Check(focus.Phase == FocusPhase.Interrupted && panel.allowanceNotice.activeInHierarchy,
                    "Cancelled session should display its notice");
            }
            if (step == 3 || step == 8)
            {
                Check(panel.start.GetComponentInChildren<TMPro.TMP_Text>().color == TaskUIBuilder.Ink,
                    "Start Focus label color was overridden");
                Capture("cancel-notice-" + (step == 3 ? "1920" : "1280"));
            }
            if (step == 4 || step == 9)
            {
                Check(!panel.allowanceNotice.activeSelf, "Cancelled session notice did not expire");
                nav.ShowPage(MainMenuPage.Dashboard);
                nav.ShowPage(MainMenuPage.Focus);
                Check(!panel.allowanceNotice.activeSelf, "Reopening Focus replayed the expired notice");
                Capture("cancel-expired-" + (step == 4 ? "1920" : "1280"));
            }
            if (step == 5) Resolution(1280,720);
            if (step == 10)
            {
                File.WriteAllText(FolderNow+"/result.txt", "PASS: Stopwatch cancellation, button ink, notice expiry and page reopen at both sizes.");
                EditorApplication.isPlaying = false;
                return;
            }
            step++;
            next = EditorApplication.timeSinceStartup + (step == 4 || step == 9 ? 5 : .4);
            return;
        }
        if (Before)
        {
            if(step==0)nav.ShowPage(MainMenuPage.Focus);
            if(step==1)Capture("before-1920");
            if(step==2)Resolution(1280,720);
            if(step==3)Capture("before-1280");
            if(step==4){File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+"\nCaptured current authored Focus before UI patch.");EditorApplication.isPlaying=false;return;}
            step++;next=EditorApplication.timeSinceStartup+1;return;
        }
        if (SessionState.GetBool(Flag+".tagsOnly",false) && step >= 8)
        {
            if (step == 8)
            {
                var clear = panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == "NoTags");
                clear.OnSubmit(new BaseEventData(EventSystem.current));
                Check(focus.Options.tagIds.Count == 0 && !panel.questPopup.gameObject.activeSelf, "Keyboard clear failed");
                nav.ShowPage(MainMenuPage.Dashboard); nav.ShowPage(MainMenuPage.Focus);
                panel.questDropdown.onClick.Invoke(); panel.questSearch.text = "Reading";
                var choice = panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == tagId);
                choice.OnSubmit(new BaseEventData(EventSystem.current));
                Check(focus.Options.tagIds.Count == 1, "Keyboard selection/reopen duplicated listeners");
                Capture("tags-selected-1280");
            }
            if (step == 9) { Resolution(1920,1080); }
            if (step == 10)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_UI_Etc/InputField_WhiteCustom.prefab");
                var reference = UnityEngine.Object.Instantiate(source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "InputField_Light_Username").gameObject,panel.transform);
                reference.name = "ReferenceOnly";
                var rect = (RectTransform)reference.transform; rect.anchorMin = rect.anchorMax = new Vector2(.55f,.35f); rect.anchoredPosition = Vector2.zero; rect.localScale = Vector3.one * .5f;
            }
            if (step == 11) Capture("tags-simplecasual-reference-1920");
            if (step == 12)
            {
                UnityEngine.Object.Destroy(panel.transform.Find("ReferenceOnly").gameObject);
                File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+"\nPASS: Focus tags only: search, empty search, single-tag selection/replacement, keyboard selection/clear, page reopen, session lock, committed rewards and 1920x1080/1280x720 captures. Full mode raycast/motion flow is separate.");
                EditorApplication.isPlaying=false; return;
            }
            step++; next=EditorApplication.timeSinceStartup+1; return;
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
            Check(service.SaveTag(null, "Reading", out tagId), service.LastError);
            panel.focusDuration.value = 37; panel.restDuration.value = 7;
            Check(focus.Options.focusMinutes == 37 && focus.Options.restMinutes == 7, "Sliders not bound");
            Check(panel.rewards.text == "37" && panel.fishReward.text == "14" && panel.ticketReward.text == "1", "Reward numbers incorrect");
            Capture("focus-ready-1920");
        }
        if (step == 2) {
            panel.questDropdown.onClick.Invoke(); panel.questSearch.text = "READ";
            var buttons = panel.questList.GetComponentsInChildren<Button>();
            Check(buttons.Length == 2 && buttons.Any(b => b.name == tagId), "Case-insensitive tag search failed");
            Capture("focus-search-1920");
        }
        if (step == 3) {
            panel.questSearch.text = "zzz-no-match";
            Check(panel.questList.GetComponentsInChildren<Button>().Length == 1, "Empty search should keep free focus");
            panel.questSearch.text = "reading";
        }
        if (step == 4) {
            panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == tagId).onClick.Invoke();
            Check(focus.Options.tagIds.Contains(tagId) && !panel.questPopup.gameObject.activeSelf && string.IsNullOrEmpty(focus.Options.questId), "Tag selection did not bind independently of quests");
            panel.questSearch.text = "Study";
            panel.questList.GetComponentsInChildren<Button>().Single(b => b.name != "NoTags").onClick.Invoke();
            Check(focus.Options.tagIds.Count == 1 && !focus.Options.tagIds.Contains(tagId), "Tag selection did not replace previous tag");
            panel.questSearch.text = "Study";
            panel.questList.GetComponentsInChildren<Button>().Single(b => b.name != "NoTags").onClick.Invoke();
            Check(focus.Options.tagIds.Count == 1, "Repeated tag selection duplicated tag");
            Check(!panel.questPopup.gameObject.activeSelf && panel.questSearch.text == "", "Dropdown dismissal failed");
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
        if (step == 7) { panel.questDropdown.onClick.Invoke(); Capture("focus-tags-1280"); }
        if (step == 8) { panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == "NoTags").onClick.Invoke(); Check(focus.Options.tagIds.Count == 0 && !panel.questPopup.gameObject.activeSelf, "No tags did not clear selection"); }
        if(step==8){panel.stopwatchMode.isOn=true;Check(focus.CountsUp && !panel.focusDurationCard.activeSelf && !panel.restDurationCard.activeSelf && panel.rewards.text=="0","Stopwatch settings incorrect");Capture("stopwatch-ready-1280");}
        if(step==9){panel.start.onClick.Invoke();for(int i=0;i<14400;i++)focus.Advance(1);Check(focus.Phase==FocusPhase.Focusing && panel.stopwatchMode.interactable==false,"Stopwatch limit/lock wrong");}
        if(step==10)Capture("stopwatch-four-hours-1280");
        if(step==11){panel.endEarly.onClick.Invoke();Check(focus.Phase==FocusPhase.Finished && focus.LastResult.experience==180,"Stopwatch saved incorrectly");Capture("stopwatch-saved-1280");}
        if(step==12){
            Check(!panel.allowance.text.Contains("Focus saved."), "Saved notice did not expire while timer was stopped");
            nav.ShowPage(MainMenuPage.Dashboard);nav.ShowPage(MainMenuPage.Focus);
            Check(!panel.allowance.text.Contains("Focus saved."), "Page reopen restarted saved notice");
            Check(panel.start.GetComponentInChildren<TMPro.TMP_Text>().text=="Start Focus", "Finished action label incorrect");
            Capture("saved-notice-expired-1280");
        }
        if(step==13){panel.countdownMode.isOn=true;panel.focusDuration.value=180;Check(focus.Options.focusMinutes==180 && !panel.restDurationCard.activeSelf,"Countdown settings incorrect");Resolution(1920,1080);}
        if(step==14)Capture("countdown-ready-1920");
        if(step==15){panel.start.onClick.Invoke();for(int i=0;i<10800;i++)focus.Advance(1);Check(focus.Phase==FocusPhase.Finished && service.TodayRewards.focusMinutes==360,"Mixed mode cap incorrect");Capture("countdown-saved-1920");}
        if(step==16){
            Check(!panel.allowanceNotice.activeSelf && panel.rewardAllowanceBar.value == 360, "Capped session notice did not expire or cap progress disappeared");
            Capture("saved-notice-expired-1920");
        }
        if(step==17){panel.stopwatchMode.isOn=true;panel.start.onClick.Invoke();for(int i=0;i<3600;i++)focus.Advance(1);}
        if(step==18)Capture("stopwatch-capped-1920");
        if(step==19){panel.cancel.onClick.Invoke();Check(focus.Phase==FocusPhase.Interrupted && service.TodayRewards.focusMinutes==360,"Cancel changed allowance");}
        if(step==20){
            Check(!panel.allowanceNotice.activeSelf, "Interrupted notice did not expire");
            panel.start.onClick.Invoke();for(int i=0;i<300;i++)focus.Advance(1);
            saveLock=File.Open(GameDataManager.Instance.SavePath+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            panel.endEarly.onClick.Invoke();Check(focus.Phase==FocusPhase.Saving,"Locked save did not enter retry state");
        }
        if(step==21){Check(panel.allowance.gameObject.activeInHierarchy && panel.allowance.text.Contains("Retry") && panel.retry.gameObject.activeInHierarchy,"Save error/retry not visible");Capture("save-failure-1920");}
        if(step==22){saveLock.Dispose();saveLock=null;panel.retry.onClick.Invoke();Check(focus.Phase==FocusPhase.Finished,"Retry did not finish");}
        if(step==23)Capture("save-retried-1920");
        if(step==24){
            if (DismissLevelUps(panel)) return;
            var data=new BaseEventData(EventSystem.current);
            EventSystem.current.SetSelectedGameObject(panel.countdownMode.gameObject);
            panel.countdownMode.OnSubmit(data);
            Check(focus.Options.mode==FocusTimerMode.Countdown,"Keyboard mode submit failed");
            foreach (var mode in new[] { panel.pomodoroMode, panel.stopwatchMode, panel.countdownMode })
                foreach (var point in new[] { new Vector2(.25f,.5f), new Vector2(.5f,.8f), new Vector2(.5f,.2f), new Vector2(.75f,.5f) })
                    Hit(mode, Pointer(mode, point));
            Click(panel.stopwatchMode); Check(focus.CountsUp,"Mode row click failed");
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
            // Render the SimpleCasual input reference beside the retained project picker.
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_UI_Etc/InputField_WhiteCustom.prefab");
            var reference=UnityEngine.Object.Instantiate(source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "InputField_Light_Username").gameObject,panel.transform);reference.name="ReferenceOnly";
            var rect=(RectTransform)reference.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one * .5f;
        }
        if(step==34)Capture("simplecasual-reference-1920");
        if (step == 35) {
            UnityEngine.Object.Destroy(panel.transform.Find("ReferenceOnly").gameObject);
            panel.stopwatchMode.isOn=true;
        }
        if (step >= 36 && step <= 43) {
            var mode=panel.countdownMode;
            var pointer=Pointer(mode,new Vector2(.5f,.5f));
            var target=Hit(mode,pointer);
            if(step==36)ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerEnterHandler);
            if(step==37)ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerDownHandler);
            if(step==38){ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);Check(focus.Options.mode==FocusTimerMode.Countdown,"Mode card click did not select countdown");}
            if(step==39){ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerExitHandler);EventSystem.current.SetSelectedGameObject(null);}
            if(step==40){for(int i=0;i<12;i++){Click(panel.pomodoroMode);Click(mode);}Check(mode.isOn&&!panel.pomodoroMode.isOn&&!panel.stopwatchMode.isOn,"Rapid input broke exclusive mode selection");}
            if(step==41){Click(panel.start);Click(panel.pomodoroMode);Check(focus.Options.mode==FocusTimerMode.Countdown&&!mode.interactable,"Locked mode changed");}
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
            // Only this runner's isolated save: exercise late-game preview and larger allowance text.
            if(!service.Snapshot.tasks.Any(t=>t.id==questId))
                Check(service.Upsert(new QuestTask {title="Read chapter"},null,out questId),service.LastError);
            var data=GameDataManager.Instance.PlayerData;
            data.experience=13050;
            foreach(var day in data.rewardDays) { day.focusMinutes=0; day.focusTickets=0; }
            GameDataManager.Instance.SaveData();
            // The fixture bypasses domain events; resync the existing HUD before capturing it.
            foreach(var feedback in UnityEngine.Object.FindObjectsByType<RewardFlightUI>(FindObjectsSortMode.None)) {
                feedback.enabled=false; feedback.enabled=true;
            }
            focus.Configure(new FocusSessionOptions()); nav.ShowPage(MainMenuPage.Focus); Resolution(1920,1080);
        }
        if(step==45) {
            Check(panel.rewards.text=="97" && panel.fishReward.text=="39" && panel.ticketReward.text=="1", "Late-game reward preview");
            Check(panel.rewardAllowanceBar.maxValue==360 && panel.rewardAllowanceLabel.text.Contains("360")
                && !panel.allowanceNotice.activeSelf, "Late-game allowance progress without repeated rules");
            var target=nav.GetComponentsInChildren<RewardLimitHoverTarget>(true).Single(t=>t.kind==RewardLimitKind.Experience);
            target.tooltip.Show(target);
            Check(target.tooltip.note.text.Contains("702") && target.tooltip.focusRewards.text.Contains("1404"), "Late-game session/daily caps remain available in the EXP tooltip");
            target.tooltip.Hide(target);
            Capture("level30-ready-1920");
        }
        if(step==46)Resolution(1280,720);
        if(step==47) {
            foreach(var label in new[]{panel.rewards,panel.fishReward,panel.ticketReward,panel.rewardAllowanceLabel,panel.allowance})
                Check(!label.isTextOverflowing,"Late-game text clipped: "+label.text);
            Capture("level30-ready-1280");
        }
        if(step==48) {
            panel.focusDuration.value=10;panel.start.onClick.Invoke(); for(int i=0;i<600;i++)focus.Advance(1);
            Check(focus.Phase==FocusPhase.Resting && panel.rewards.text=="39" && panel.fishReward.text=="15", "Late-game completed Pomodoro UI settlement");
            Capture("level30-completed-1280");
        }
        if (step == 49) {
            panel.endRest.onClick.Invoke();
            nav.ShowPage(MainMenuPage.Tasks);
        }
        if(step==50) {
            // Let TaskSystemUI.Start initialize its default tab before selecting the fixture.
            var owner=nav.pages[1].GetComponent<TaskSystemUI>();
            owner.SelectTab(TaskTab.Todo);
            owner.SelectTask(service.Query(TaskTab.Todo).First(v=>v.task.id==questId).Key);
        }
        if(step==51 || step==53) Capture(step==51?"level30-task-1280":"level30-task-1920");
        if(step==52 || step==54) {
            var owner=nav.pages[1].GetComponent<TaskSystemUI>();
            var body=owner.detailsRoot.Find("ScrollRect/Content/Rewards/RewardsItem");
            var xp=body.Find("EXP/Text (TMP)").GetComponent<TMPro.TMP_Text>();
            var fish=body.Find("FishCoins/Text (TMP)").GetComponent<TMPro.TMP_Text>();
            Check(xp.text=="78" && fish.text=="39" && !xp.isTextOverflowing && !fish.isTextOverflowing,
                "Level-30 task preview or clipping: EXP="+xp.text+" overflow="+xp.isTextOverflowing+" Fish="+fish.text+" overflow="+fish.isTextOverflowing+" active="+owner.detailsRoot.gameObject.activeInHierarchy);
            var row=owner.listContent.Find("Task_"+owner.SelectedKey);
            Check(row && row.Find("FishNum").GetComponent<TMPro.TMP_Text>().text=="39"
                && row.Find("FishNum (1)").GetComponent<TMPro.TMP_Text>().text=="78", "Task row and detail rewards differ");
        }
        if(step==52)Resolution(1920,1080);
        if (step == 54 && !SessionState.GetBool(Flag+".lateOnly",false))
        {
            pointerFlow = PointerChecks(panel, nav);
            step = 55;
        }
        if (step == 55 && pointerFlow != null)
        {
            if (pointerFlow.MoveNext()) { next=EditorApplication.timeSinceStartup+Convert.ToDouble(pointerFlow.Current); return; }
            (pointerFlow as IDisposable)?.Dispose(); pointerFlow=null;
        }
        if (step >= 54) {
            string scope=SessionState.GetBool(Flag+".lateOnly",false)
                ? "Late-game checks only. "
                : "Full Focus flow: duration settings, three modes, fixed time caps, cancel, save retry, search, keyboard/pointer, disable/reopen and feedback reset. ";
            File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\nPASS: "+scope+
                "Lv.30 Focus preview, amount caps, End Early, task reward preview and overflow checks; captures at 1920x1080 and 1280x720. Full runs also exercise raycast-routed mouse events, modal Continue, controls and unscaled feedback at both sizes; see interactions.txt and motion.csv. Visual review is separate.");
            EditorApplication.isPlaying = false; return;
        }
        step++; next = EditorApplication.timeSinceStartup + (step==12 || step==16 || step==20 ? 5 : step>=25 && step<=32 ? .15 : 1);
    }
}
