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
public static class RewardLimitTooltipVerification
{
    const string Flag = "CatQuest.RewardLimitTooltipCheck";
    const string Folder = "Library/RewardLimitTooltipVerification";
    static int step;
    static double next;
    static RewardScreenshotCapture capture;
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
    static RewardLimitTooltipVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = 0; next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) {
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
        if (!capture) capture = new GameObject("Reward Tooltip Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Reward Tooltip Play Mode verification exceeded 180 seconds.");EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { Tick(); } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-reward-tooltip.request")) return;
        SessionState.SetBool(Flag+".before",File.ReadAllText("Temp/verify-reward-tooltip.request").Trim()=="before");
        File.Delete("Temp/verify-reward-tooltip.request");
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
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var manager=GameDataManager.Instance;
        Check(manager && manager.SavePath.Contains("RewardLimitTooltipVerification"),"Isolated save required");
        var fish=nav.fishCoins.GetComponentInParent<RewardLimitHoverTarget>();
        var ticket=nav.tickets.GetComponentInParent<RewardLimitHoverTarget>();
        var avatar=nav.GetComponentsInChildren<RewardLimitHoverTarget>(true).Single(t=>t.kind==RewardLimitKind.Experience);
        var pointer=new PointerEventData(EventSystem.current);
        if(Before){
            if(step==0)Capture("before-1920");
            if(step==1)Resolution(1280,720);
            if(step==2)Capture("before-1280");
            if(step==3){Finish("PASS: before captures");return;}
        }else{
            Check(fish&&ticket&&avatar&&fish.tooltip==ticket.tooltip&&fish.tooltip==avatar.tooltip,"Header bindings missing");
            var tip=fish.tooltip;
            if(step==0){
                Check(!tip.gameObject.activeSelf,"Tooltip visible initially");
                Check(tip.GetComponentsInChildren<Component>(true).All(c=>c),"Missing tooltip script");
                manager.SaveData();File.Copy(manager.SavePath,FolderNow+"/before-save.json");
                fish.OnPointerEnter(pointer);
                Check(tip.taskBar.value==0&&tip.focusBar.value==0,"Empty allowance bars incorrect");
            }
            if(step==1){Bounds(tip);Content(tip,RewardLimitKind.Fish);Capture("fish-empty-1920");}
            if(step==2){
                var service=manager.Tasks;
                manager.PlayerData.rewardDays.Add(new RewardDay{day=service.TodayRewards.day,taskExperience=60,taskFishCoins=30,focusMinutes=180,focusTickets=2});
                fish.OnPointerEnter(pointer);
                Check(tip.taskBar.normalizedValue==.5f&&tip.focusBar.normalizedValue==.5f,"Partial allowance bars incorrect");
            }
            if(step==3){Bounds(tip);Content(tip,RewardLimitKind.Fish);Capture("fish-1920");}
            if(step==4){ticket.OnPointerEnter(pointer);fish.OnPointerExit(pointer);Check(tip.gameObject.activeSelf,"Stale exit hid other target");}
            if(step==5){Bounds(tip);Content(tip,RewardLimitKind.Tickets);Capture("ticket-1920");Check(tip.ticketBar.value==2&&tip.ticketBar.maxValue==4,"Ticket allowance incorrect");}
            if(step==6)avatar.OnPointerEnter(pointer);
            if(step==7){Bounds(tip);Content(tip,RewardLimitKind.Experience);Capture("avatar-exp-1920");Check(tip.taskValue.text=="60 / 120 EXP","EXP allowance incorrect");}
            if(step==8){
                avatar.OnPointerExit(pointer);Check(!tip.gameObject.activeSelf,"Exit did not hide");
                for(int i=0;i<10;i++){fish.OnPointerEnter(pointer);ticket.OnPointerEnter(pointer);avatar.OnPointerEnter(pointer);avatar.OnPointerExit(pointer);}
                Check(!tip.gameObject.activeSelf,"Rapid hover stuck");
                fish.OnSelect(new BaseEventData(EventSystem.current));Check(tip.gameObject.activeSelf,"Selection missing");
                fish.OnDeselect(new BaseEventData(EventSystem.current));Check(!tip.gameObject.activeSelf,"Deselect stuck");
                fish.OnPointerEnter(pointer);tip.SendMessage("OnApplicationFocus",false);Check(!tip.gameObject.activeSelf,"Focus loss stuck");
                fish.OnPointerEnter(pointer);fish.enabled=false;Check(!tip.gameObject.activeSelf,"Disable stuck");fish.enabled=true;
                fish.OnPointerEnter(pointer);fish.OnPointerDown(pointer);Check(!tip.gameObject.activeSelf,"Press stuck");
                Check(File.ReadAllText(manager.SavePath)==File.ReadAllText(FolderNow+"/before-save.json"),"Hover mutated save");
                fish.OnPointerEnter(pointer);nav.ShowPage(nav.CurrentPage==MainMenuPage.Dashboard?MainMenuPage.Tasks:MainMenuPage.Dashboard);
            }
            if(step==9){Check(!tip.gameObject.activeSelf,"Page change stuck");Resolution(1280,720);}
            if(step==10)fish.OnPointerEnter(pointer);
            if(step==11){Bounds(tip);Content(tip,RewardLimitKind.Fish);Capture("fish-1280");}
            if(step==12)ticket.OnPointerEnter(pointer);
            if(step==13){Bounds(tip);Content(tip,RewardLimitKind.Tickets);Capture("ticket-1280");}
            if(step==14)avatar.OnPointerEnter(pointer);
            if(step==15){Bounds(tip);Content(tip,RewardLimitKind.Experience);Capture("avatar-exp-1280");}
            if(step==16){
                var day=manager.PlayerData.rewardDays.Single(d=>d.day==manager.Tasks.TodayRewards.day);
                day.taskExperience=120;day.taskFishCoins=60;day.focusMinutes=360;day.focusTickets=4;
                Check(manager.Tasks.BeginFocus(out var id),"Could not trigger committed refresh");
                Check(tip.taskBar.normalizedValue==1&&tip.focusBar.normalizedValue==1,"Live refresh did not fill capped bars");
                Check(manager.Tasks.CancelFocus(id),"Could not cancel verification session");
            }
            if(step==17){Bounds(tip);Capture("avatar-capped-1280");ticket.OnPointerEnter(pointer);Check(tip.ticketBar.normalizedValue==1,"Ticket cap not full");}
            if(step==18){Bounds(tip);Capture("ticket-capped-1280");}
            if(step==19){
                tip.Hide();
                var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Sliders/SliderGroup_UserInfo_Light.prefab"),nav.transform);
                reference.name="ReferenceOnly";reference.AddComponent<LayoutElement>().ignoreLayout=true;
                var rect=(RectTransform)reference.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one;
            }
            if(step==20)Capture("simplecasual-slider-reference-1280");
            if(step==21){Finish("PASS: Fish-only, Ticket-only, avatar EXP-only content; Task/Focus slider empty/partial/cap and committed refresh; noninteractive sliders; pointer exit/press, stale exit, rapid switching, selection/deselection, disable/re-enable, focus loss/page change; no hover save mutation; raycasts/bounds/text; all three at 1920x1080 and 1280x720.");return;}
        }
        step++;next=EditorApplication.timeSinceStartup+1;
    }
    static void Content(RewardLimitTooltip tip,RewardLimitKind kind)
    {
        string visible=string.Join(" ",tip.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.text));
        bool tickets=kind==RewardLimitKind.Tickets;
        Check(tip.ticketBar.gameObject.activeSelf==tickets&&tip.taskBar.gameObject.activeSelf!=tickets&&tip.focusBar.gameObject.activeSelf!=tickets,"Wrong bars visible");
        Check(kind==RewardLimitKind.Fish?(!visible.Contains("EXP")&&!visible.Contains("Ticket")):kind==RewardLimitKind.Tickets?(!visible.Contains("Fish")&&!visible.Contains("EXP")):(!visible.Contains("Fish")&&!visible.Contains("Ticket")),"Mixed currencies in tooltip");
        foreach(var bar in tip.GetComponentsInChildren<Slider>()){
            Check(!bar.interactable&&bar.navigation.mode==Navigation.Mode.None,"Allowance slider is interactive");
            float value=bar.value;
            bar.OnDrag(new PointerEventData(EventSystem.current){position=Vector2.zero,button=PointerEventData.InputButton.Left});
            Check(bar.value==value,"Dragging changes allowance");
        }
    }
    static void Finish(string result){File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" "+result);EditorApplication.isPlaying=false;}
    static void Bounds(RewardLimitTooltip tip){
        Canvas.ForceUpdateCanvases();
        var corners=new Vector3[4];tip.card.GetWorldCorners(corners);
        foreach(var corner in corners){var p=RectTransformUtility.WorldToScreenPoint(null,corner);Check(p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height,"Tooltip outside screen");}
        foreach(var text in tip.GetComponentsInChildren<TMPro.TMP_Text>()){text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Text clipped: "+text.text);}
        Check(!tip.GetComponent<CanvasGroup>().blocksRaycasts,"Tooltip blocks input");
        Check(tip.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"Tooltip graphics block input");
        foreach(var hover in UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>().GetComponentsInChildren<RewardLimitHoverTarget>()){
        var target=hover.transform;
        var point=RectTransformUtility.WorldToScreenPoint(null,target.position);
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<RewardLimitHoverTarget>(),"Header target not raycast reachable: "+target.name);
        }
    }
}
