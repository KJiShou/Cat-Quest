using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class EquipmentPlayVerification
{
    const string Flag="CatQuest.EquipmentPlay",Folder="Library/EquipmentVerification";
    static IEnumerator run;static double next;static string error;static RewardScreenshotCapture capture;
    static EquipmentPlayVerification(){EditorApplication.update+=Poll;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Flag,false)&&(t==LogType.Error||t==LogType.Exception))error=m;};EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){error=null;run=Run();next=EditorApplication.timeSinceStartup+2;}
        if(s==PlayModeStateChange.EnteredEditMode){run=null;SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));}
    };}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-equipment-play.request")){
            File.Delete("Temp/verify-equipment-play.request");Directory.CreateDirectory(Folder);SessionState.SetBool(Flag,true);SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/play-"+Guid.NewGuid().ToString("N")+".json"));EditorApplication.isPlaying=true;return;
        }
        if(!EditorApplication.isPlaying||run==null||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();try{if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else{File.WriteAllText(Folder+"/play-result.txt","PASS: empty/locked state, Stage claim entry, real claim/equip/unequip/upgrade confirmations, persistence, full nine-item inventory, 1920/1280 layouts, hover/press, set stats and faster attack animation.");run=null;EditorApplication.isPlaying=false;}}catch(Exception e){File.WriteAllText(Folder+"/play-result.txt",e.ToString());run=null;EditorApplication.isPlaying=false;}
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Click(Transform root,string path){var b=root.Find(path).GetComponent<Button>();Check(b&&b.interactable&&b.gameObject.activeInHierarchy,"Unavailable "+path);b.onClick.Invoke();}
    static void Capture(string name){if(!capture)capture=new GameObject("Equipment Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{width,height});
    static void Layout(EquipmentPageUI page){Canvas.ForceUpdateCanvases();
        foreach(var rect in page.GetComponentsInChildren<RectTransform>().Where(r=>r.name=="GradeEffect"||r.name.StartsWith("Item_boss")||(r.name.StartsWith("Choice")&&r.name!="Choices")||(r.name.StartsWith("Slot")&&r.parent.name=="EquippedGear"))){Check(Mathf.Abs(rect.rect.width-rect.rect.height)<.1f,"Equipment is not square: "+rect.name);}
        foreach(var text in page.GetComponentsInChildren<TMP_Text>().Where(t=>!page.claimsOnly||t.transform.IsChildOf(page.transform.Find("ItemModal"))||t.transform.IsChildOf(page.transform.Find("ConfirmEquipment")))){text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Clipped: "+text.text);}}
    static IEnumerator Run(){
        UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");yield return 2;
        Resolution(1920,1080);var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var manager=GameDataManager.EnsureInstance();var service=manager.Tasks;Check(manager.SavePath.Contains("EquipmentVerification"),"Must isolate saves");
        nav.ShowPage(MainMenuPage.CatHome);yield return 1;var page=nav.pages[4].GetComponent<EquipmentPageUI>();Check(page.Ready&&!service.EquipmentUnlocked,"Locked fresh state");Capture("equipment-empty-1920");Layout(page);yield return .4;
        manager.PlayerData.experience=2025;manager.PlayerData.fishCoins=1000;manager.PlayerData.rewardDays.Add(new RewardDay{day="2026-10-20",questsClaimed=true});foreach(var p in manager.PlayerData.bossProgress){p.wins=15;p.pendingClaims=3;}manager.SaveData();
        nav.ShowPage(MainMenuPage.Boss);yield return 1;var boss=nav.pages[3].GetComponent<BossPageUI>();boss.stageButton.onClick.Invoke();yield return .4;boss.masteryBars[0].transform.parent.GetComponent<Button>().onClick.Invoke();yield return .5;
        page=nav.pages[3].GetComponent<EquipmentPageUI>();Check(nav.CurrentPage==MainMenuPage.Boss,"Stage rewards must stay on Boss");Capture("equipment-claim-1920");Layout(page);yield return .4;
        Click(page.transform,"ItemModal/Card/Choices/Choice0");Click(page.transform,"ItemModal/Card/Claim");Check(service.Snapshot.equipment.Count==0,"Claim needs confirmation");Click(page.transform,"ConfirmEquipment/Card/Confirm");Check(service.Snapshot.equipment.Count==1&&service.Snapshot.bossProgress[0].pendingClaims==2,"Claim saved");
        nav.ShowPage(MainMenuPage.CatHome);yield return .5;page=nav.pages[4].GetComponent<EquipmentPageUI>();page.OpenItem("boss1-weapon");
        Click(page.transform,"ItemModal/Card/Equip");Click(page.transform,"ConfirmEquipment/Card/Confirm");Check(service.Snapshot.equippedWeaponId=="boss1-weapon","Equip saved");
        Click(page.transform,"ItemModal/Card/Upgrade");Capture("equipment-upgrade-confirm");yield return .5;Click(page.transform,"ConfirmEquipment/Card/Cancel");Check(service.Snapshot.fishCoins==1000,"Cancel charged coins");
        Click(page.transform,"ItemModal/Card/Upgrade");Click(page.transform,"ConfirmEquipment/Card/Confirm");Check(service.Snapshot.fishCoins==985&&service.Snapshot.equipment[0].level==2,"Upgrade saved");yield return .5;Layout(page);Capture("equipment-item-1920");yield return .4;
        Click(page.transform,"ItemModal/Card/Equip");Click(page.transform,"ConfirmEquipment/Card/Confirm");Check(string.IsNullOrEmpty(service.Snapshot.equippedWeaponId),"Unequip saved");
        foreach(var def in EquipmentRules.Catalog)if(!service.Snapshot.equipment.Any(x=>x.id==def.Id))Check(service.ClaimEquipment(def.Id),"Claim "+def.Id);
        foreach(var def in EquipmentRules.Catalog.Where(x=>x.Tier==3))Check(service.Equip(def.Id,def.Slot),"Equip full set");
        Click(page.transform,"ItemModal/Card/Close");page.Refresh();yield return .8;Layout(page);Capture("equipment-full-1920");yield return .5;
        for(int f=1;f<=3;f++){Click(page.transform,"EquipmentSurface/InventoryCard/Filter"+f);yield return .1;Check(page.transform.Find("EquipmentSurface/Backpack").Cast<Transform>().Count(t=>t.name.StartsWith("Item_")&&t.gameObject.activeSelf)==3,"Category filter");}
        Click(page.transform,"EquipmentSurface/InventoryCard/Filter0");
        service.Unequip(EquipmentSlot.Head);Click(page.transform,"EquipmentSurface/InventoryCard/AutoSelect");Click(page.transform,"ConfirmEquipment/Card/Cancel");Check(string.IsNullOrEmpty(service.Snapshot.equippedHeadAccessoryId),"Auto Select cancel");
        Click(page.transform,"EquipmentSurface/InventoryCard/AutoSelect");Click(page.transform,"ConfirmEquipment/Card/Confirm");Check(service.Snapshot.equippedHeadAccessoryId=="boss3-head","Auto Select saved best set");
        yield return .2;Check(page.GetComponentsInChildren<EquipmentGradeEffect>().Length==12,"Grade effects on equipped and owned items");
        Resolution(1280,720);yield return .8;Layout(page);Capture("equipment-full-1280");yield return .5;
        var itemButton=page.transform.Find("EquipmentSurface/Backpack/Item_boss3-head").GetComponent<JuicyButton>();Check(itemButton,"Inventory button feedback missing");itemButton.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));yield return .4;Capture("equipment-hover-1280");yield return .3;itemButton.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));yield return .4;Capture("equipment-pressed-1280");yield return .3;itemButton.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));itemButton.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
        page.OpenItem("boss3-head");yield return .5;Layout(page);Capture("equipment-item-1280");yield return .4;
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;var mini=nav.pages[0].GetComponentInChildren<DashboardEquipmentUI>();Check(mini.slots.All(b=>b.interactable)&&mini.icons.All(i=>i.color==Color.white),"Dashboard gear not refreshed");Check(mini.GetComponentsInChildren<EquipmentGradeEffect>().Length==3,"Dashboard grade borders missing");Capture("dashboard-equipment-grades");yield return .4;mini.slots[2].onClick.Invoke();yield return .5;Check(page.SelectedItem=="boss3-head"&&nav.CurrentPage==MainMenuPage.CatHome,"Dashboard slot link");
        var loaded=new TaskService(new PlayerDataStore(manager.SavePath),new SystemTaskClock());Check(loaded.Snapshot.equipment.Count==9&&loaded.CatStats.aspd==service.CatStats.aspd,"Disk reload");
        nav.ShowPage(MainMenuPage.Boss);yield return 1;boss.actors.ResetPose();boss.actors.StartCombat(BattleRules.Cat(manager.PlayerData.experience),BattleRules.Boss(1));float normal=boss.actors.CatAnimationSpeed;boss.actors.StartCombat(service.CatStats,BattleRules.Boss(1));Check(Math.Abs(boss.actors.CatAnimationSpeed/normal-service.CatStats.aspd)<.001,"Equipped ASPD animation");
        boss.stagePicker.SetActive(false);boss.actors.Run(2);
        for(int tier=1;tier<=3;tier++){
            boss.actors.StartCombat(service.CatStats,BattleRules.Boss(1),"boss"+tier+"-weapon");boss.actors.Hit(true);
            Check(boss.actors.WeaponEffectTier==tier,"Weapon grade slash mapping");
            var slash=boss.arena.Stage.Find("Weapon Slash Grade "+tier);Check(slash,"Slash prefab missing");
            foreach(var ps in slash.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(.14f,false,true);ps.Pause(false);}
            Capture("weapon-slash-"+tier);yield return .4;
        }
        boss.actors.ResetPose();Check(!boss.arena.Stage.Find("Weapon Slash Grade 3").GetComponentsInChildren<ParticleSystem>().Any(p=>p.IsAlive()),"Slash cleanup");
        Check(error==null,error);Resolution(1920,1080);
    }
}

