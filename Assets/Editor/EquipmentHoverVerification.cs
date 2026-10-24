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
public static class EquipmentHoverVerification
{
    const string Flag = "CatQuest.EquipmentHoverCheck";
    const string Folder = "Library/EquipmentHoverVerification";
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
    static EquipmentHoverVerification()
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
        if (!capture) capture = new GameObject("Equipment Hover Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Equipment Hover Play Mode verification exceeded 180 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { Tick(); } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-equipment-hover.request")) return;
        SessionState.SetBool(Flag+".before",File.ReadAllText("Temp/verify-equipment-hover.request").Trim()=="before");
        File.Delete("Temp/verify-equipment-hover.request");
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
        var manager=GameDataManager.Instance;var service=manager.Tasks;
        var page=nav.pages[(int)MainMenuPage.CatHome].GetComponent<EquipmentPageUI>();
        Check(manager.SavePath.Contains("EquipmentHoverVerification"),"Must use isolated save");
        if(step==0){
            manager.PlayerData.experience=2025;manager.PlayerData.fishCoins=1000;
            manager.PlayerData.rewardDays.Add(new RewardDay{day="2026-10-24",questsClaimed=true});
            foreach(var boss in manager.PlayerData.bossProgress){boss.wins=15;boss.pendingClaims=3;}
            manager.SaveData();
            foreach(var def in EquipmentRules.Catalog)Check(service.ClaimEquipment(def.Id),service.LastError);
            Check(service.Equip("boss1-weapon",EquipmentSlot.Weapon),service.LastError);
            nav.ShowPage(MainMenuPage.CatHome);
        }
        var detail=page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
        var events=EventSystem.current;
        var item=page.GetComponentsInChildren<EquipmentInventoryItemUI>().FirstOrDefault(x=>x.name=="Item_boss1-weapon");
        var target=item?item.GetComponent<EquipmentHoverTarget>():null;
        var pointer=new PointerEventData(events){position=target?RectTransformUtility.WorldToScreenPoint(null,target.transform.position):Vector2.zero,button=PointerEventData.InputButton.Left};
        if(Before){
            if(step==1)Capture("cat-home-1920");
            if(step==2)page.OpenItem("boss1-weapon");
            if(step==3)Capture("click-detail-1920");
            if(step==4)Resolution(1280,720);
            if(step==5)Capture("click-detail-1280");
            if(step==6){File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" Captured existing Cat Home and click detail.");EditorApplication.isPlaying=false;return;}
        }else{
            if(step==1){
                Check(target,"Inventory hover binding missing");
                File.WriteAllText(FolderNow+"/before-hover-save.json",File.ReadAllText(manager.SavePath));
                SessionState.SetString(Flag+"selected",page.SelectedItem??"");
                target.OnPointerEnter(pointer);
            }
            if(step==2){
                Check(detail.IsHoverPreview&&detail.gameObject.activeInHierarchy,"Hover did not open preview");
                Check((page.SelectedItem??"")==SessionState.GetString(Flag+"selected",""),"Hover changed actionable selection");
                Check(detail.itemName.text==EquipmentRules.Find("boss1-weapon").Name&&detail.currentValue.text==EquipmentRules.Effect(EquipmentRules.Find("boss1-weapon"),1),"Hover content differs from item data");
                Check(!detail.GetComponent<GraphicRaycaster>().enabled&&!detail.GetComponent<CanvasGroup>().blocksRaycasts,"Tooltip blocks pointer");
                Check(!detail.equip.gameObject.activeSelf&&!detail.upgrade.gameObject.activeSelf&&!detail.close.gameObject.activeSelf,"Hover shows action buttons");
                Check(!detail.transform.Find("ScreenDim").GetComponent<Image>().enabled,"Hover dimmed the page");
                Bounds(detail);TextFits(detail);Capture("hover-inventory-1920");
            }
            if(step==3){SessionState.SetFloat(Flag+"x",detail.card.position.x);pointer.position+=new Vector2(30,16);target.OnPointerMove(pointer);}
            if(step==4){Check(Mathf.Abs(detail.card.position.x-SessionState.GetFloat(Flag+"x",0))>10,"Tooltip does not follow pointer movement");Capture("hover-moved-1920");}
            if(step==5){
                var hits=new System.Collections.Generic.List<RaycastResult>();events.RaycastAll(pointer,hits);
                Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<EquipmentInventoryItemUI>()==item,"Tooltip intercepted source item click");
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);
            }
            if(step==6){
                Check(!detail.IsHoverPreview&&detail.gameObject.activeSelf&&detail.GetComponent<GraphicRaycaster>().enabled&&detail.GetComponent<CanvasGroup>().blocksRaycasts,"Click did not restore modal input");
                Check(detail.transform.Find("ScreenDim").GetComponent<Image>().enabled,"Click did not restore backdrop");
                Check(detail.card.anchoredPosition==Vector2.zero&&detail.equip.gameObject.activeSelf&&detail.close.gameObject.activeSelf,"Click did not restore card/actions");
                Check(events.currentSelectedGameObject.transform.IsChildOf(detail.transform),"Click modal lost keyboard focus");
                Capture("click-after-hover-1920");
            }
            if(step==7){detail.close.onClick.Invoke();target.OnPointerEnter(pointer);target.OnPointerExit(pointer);Check(!detail.gameObject.activeSelf,"Pointer exit did not hide preview");}
            if(step==8){
                var gear=page.transform.Find("EquipmentSurface/CharacterCard/EquippedGear");
                var empty=gear.Find("Slot1").GetComponent<EquipmentHoverTarget>();empty.OnPointerEnter(pointer);Check(!detail.gameObject.activeSelf,"Empty slot showed stale item");
                var worn=gear.Find("Slot0").GetComponent<EquipmentHoverTarget>();pointer.position=RectTransformUtility.WorldToScreenPoint(null,worn.transform.position);worn.OnPointerEnter(pointer);
            }
            if(step==9){Check(detail.IsHoverPreview&&detail.equipLabel.text=="Unequip","Equipped slot details wrong");Bounds(detail);Capture("hover-equipped-1920");}
            if(step==10){page.HideHover();Resolution(1280,720);}
            if(step==11){target.OnPointerEnter(pointer);}
            if(step==12){Bounds(detail);TextFits(detail);Capture("hover-inventory-1280");}
            if(step>=13&&step<=20){
                var corners=new[]{new Vector2(5,5),new Vector2(Screen.width-5,5),new Vector2(5,Screen.height-5),new Vector2(Screen.width-5,Screen.height-5)};
                int index=(step-13)/2;
                if(step%2==1)detail.MoveHoverPreview(corners[index]);
                else{Bounds(detail);Capture("hover-corner-"+index);}
            }
            if(step==21){
                page.HideHover();target.OnPointerEnter(pointer);page.SendMessage("OnApplicationFocus",false);Check(!detail.gameObject.activeSelf,"Focus loss did not dismiss");
                page.SendMessage("OnApplicationFocus",true);target.OnPointerEnter(pointer);nav.ShowPage(MainMenuPage.Dashboard);Check(!detail.gameObject.activeSelf,"Page closure did not dismiss");nav.ShowPage(MainMenuPage.CatHome);
            }
            if(step==22){
                for(int i=0;i<10;i++){target.OnPointerEnter(pointer);target.OnPointerExit(pointer);}
                Check(!detail.gameObject.activeSelf,"Rapid hover left tooltip open");
                target.OnPointerEnter(pointer);page.Refresh();Check(!detail.gameObject.activeSelf,"Inventory rebuild left stale tooltip");
            }
            if(step==23){target.OnPointerEnter(pointer);target.OnPointerDown(pointer);Check(!detail.gameObject.activeSelf,"Pointer press/drag did not dismiss");target.OnPointerEnter(pointer);target.gameObject.SetActive(false);Check(!detail.gameObject.activeSelf,"Source disable did not dismiss");target.gameObject.SetActive(true);}
            if(step==24){Check(File.ReadAllText(manager.SavePath)==File.ReadAllText(FolderNow+"/before-hover-save.json"),"Hover/click inspection mutated save");page.OpenItem("boss1-weapon");}
            if(step==25){Capture("click-after-hover-1280");TextFits(detail);}
            if(step==26){detail.equip.onClick.Invoke();Check(string.IsNullOrEmpty(service.Snapshot.equippedWeaponId),"Unequip after hover failed");}
            if(step==27){detail.equip.onClick.Invoke();Check(service.Snapshot.equippedWeaponId=="boss1-weapon","Equip after hover failed");detail.upgrade.onClick.Invoke();Check(service.Snapshot.equipment.First(x=>x.id=="boss1-weapon").level==2,"Upgrade after hover failed");}
            if(step==28){detail.close.onClick.Invoke();target.OnPointerEnter(pointer);}
            if(step==29){Check(detail.level.text=="2","Hover has stale upgraded level");Bounds(detail);Capture("hover-upgraded-1280");}
            if(step==30){
                page.HideHover();
                var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Equipment_Popup_Detail.prefab"),page.transform);
                reference.name="ReferenceOnly";var rect=(RectTransform)reference.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.localScale=Vector3.one*.5f;
            }
            if(step==31)Capture("simplecasual-reference-1280");
            if(step==32){
                File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: hover content, inventory/equipped/empty slots, cursor following, four-edge containment, non-blocking raycasts, click/modal/keyboard restoration, rapid hover, exit/disable/page/focus-loss cleanup, isolated-save immutability, direct equip/unequip/upgrade and fresh level; screenshots at 1920x1080 and 1280x720.");
                EditorApplication.isPlaying=false;return;
            }
        }
        step++;next=EditorApplication.timeSinceStartup+1;
    }
    static void Bounds(EquipmentItemDetailUI detail){
        var corners=new Vector3[4];detail.card.GetWorldCorners(corners);
        foreach(var corner in corners){var point=RectTransformUtility.WorldToScreenPoint(null,corner);Check(point.x>=0&&point.y>=0&&point.x<=Screen.width&&point.y<=Screen.height,"Tooltip outside screen: "+point);}
    }
    static void TextFits(EquipmentItemDetailUI detail){
        Canvas.ForceUpdateCanvases();foreach(var text in detail.GetComponentsInChildren<TMPro.TMP_Text>()){text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Clipped detail: "+text.text);}
    }
}
