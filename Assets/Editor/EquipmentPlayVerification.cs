using System;
using static SqlitePlayVerificationSupport;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class EquipmentPlayVerification
{
    const string Flag="CatQuest.EquipmentPlay",Folder="Library/EquipmentVerification";
    static readonly List<string> textFailures=new List<string>();
    static string TextFailureReport=>textFailures.Count==0?"":"\nText overflow failures ("+textFailures.Count+"):\n"+string.Join("\n",textFailures);
    static IEnumerator run;static double next;static string error;static RewardScreenshotCapture capture;
    static EquipmentPlayVerification(){EditorApplication.update+=Poll;Application.logMessageReceived+=(m,s,t)=>{if(SessionState.GetBool(Flag,false)&&(t==LogType.Error||t==LogType.Exception))error=m;};EditorApplication.playModeStateChanged+=s=>{
        if(!SessionState.GetBool(Flag,false))return;
        if(s==PlayModeStateChange.EnteredPlayMode){error=null;textFailures.Clear();File.WriteAllText(Folder+"/text-overflow.txt","RUNNING "+DateTime.UtcNow.ToString("O"));run=Flatten(Run());next=EditorApplication.timeSinceStartup+2;}
        if(s==PlayModeStateChange.EnteredEditMode)Restore();
    };}
    static void Restore()
    {
        if(!SessionState.GetBool(Flag,false))return;
        try {
            (run as IDisposable)?.Dispose();run=null;
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",SessionState.GetString(Flag+".env","") is string prior&&prior.Length>0?prior:null);
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene",""));
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.GetType().GetProperty("selectedSizeIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).SetValue(view,SessionState.GetInt(Flag+".size",0));
            SqlitePlayVerificationSupport.RestoreCustomSizes(Flag);
        } finally { SessionState.SetBool(Flag,false);capture=null; }
    }
    static void Stop(string result)
    {
        File.WriteAllText(Folder+"/play-result.txt",result+TextFailureReport);
        (run as IDisposable)?.Dispose();run=null;
        if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;
        else Restore();
    }
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)&&File.Exists("Temp/verify-equipment-play.request")) {
            File.Delete("Temp/verify-equipment-play.request");
            File.AppendAllText(Folder+"/request-rejections.txt",DateTime.UtcNow.ToString("O")+" Rejected duplicate request while verification owns Editor state.\n");
        }
        if(!SessionState.GetBool(Flag,false)&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-equipment-play.request")){
            File.Delete("Temp/verify-equipment-play.request");Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/play-result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));SessionState.SetString(Flag+".env",Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"");var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));SessionState.SetInt(Flag+".size",(int)view.GetType().GetProperty("selectedSizeIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(view));SessionState.SetFloat(Flag+".started",(float)EditorApplication.timeSinceStartup);SqlitePlayVerificationSupport.RememberCustomSizes(Flag);SessionState.SetBool(Flag,true);SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/play-"+Guid.NewGuid().ToString("N")+".json"));EditorApplication.isPlaying=true;return;
        }
        if(SessionState.GetBool(Flag,false)) {
            double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+".started",0);
            if(elapsed>180){Stop("FAIL: timeout");return;}
            if(!EditorApplication.isPlayingOrWillChangePlaymode&&elapsed>5){Stop("FAIL: Play Mode entry was aborted; prior Editor state restored.");return;}
        }
        if(!EditorApplication.isPlaying||run==null||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();try{if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else{File.WriteAllText(Folder+"/play-result.txt","PASS "+DateTime.UtcNow.ToString("O")+": SimpleCasual inventory templates/authored-column layout/filter and auto-select feedback, outside-click dismissal and inside/drag/right-click guards, full-screen modal/header/sidebar raycasts, both character cards stats/power at four sizes and both sidebar states, detail controls/raycast/feedback reset, empty/locked state, Stage claim entry, direct equip/unequip including failure/retry/repeated input, direct chest claim and direct upgrade/Auto Select/failure/retry, persistence, full nine-item inventory, 1920/1280 layouts, hover/press, set stats and faster attack animation.");(run as IDisposable)?.Dispose();run=null;EditorApplication.isPlaying=false;}}catch(Exception e){File.WriteAllText(Folder+"/play-result.txt","FAIL "+DateTime.UtcNow.ToString("O")+"\n"+e+TextFailureReport);(run as IDisposable)?.Dispose();run=null;EditorApplication.isPlaying=false;}
    }
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Click(Transform root,string path){var target=root.Find(path);Check(target,"Missing control path "+path);Click(target.GetComponent<Button>());}
    static void Click(Button button){Check(button&&button.interactable&&button.gameObject.activeInHierarchy,"Unavailable "+(button?button.name:"unbound button"));button.onClick.Invoke();}
    static void Capture(string name){if(!capture)capture=new GameObject("Equipment Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{width,height});
    static void Layout(EquipmentPageUI page){Canvas.ForceUpdateCanvases();
        foreach(var rect in page.GetComponentsInChildren<RectTransform>().Where(r=>r.name=="GradeEffect"||r.name.StartsWith("Item_boss")||(r.GetComponent<Button>()&&r.name.StartsWith("Choice")&&r.name!="Choices")||(r.name.StartsWith("Slot")&&r.parent.name=="EquippedGear"))){Check(Mathf.Abs(rect.rect.width-rect.rect.height)<.1f,"Equipment is not square: "+rect.name);}
        foreach(var text in page.GetComponentsInChildren<TMP_Text>().Where(t=>!page.claimsOnly||t.transform.IsChildOf(page.transform.Find("ItemModal"))||t.transform.IsChildOf(page.transform.Find("ItemDetail"))||t.transform.IsChildOf(page.transform.Find("ConfirmEquipment")))){text.ForceMeshUpdate();Check(!text.isTextOverflowing,"Clipped: "+text.text);}}
    static void CheckInventory(EquipmentPageUI page){
        var view=page.GetComponentInChildren<EquipmentInventoryUI>();
        var authored=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab").GetComponentsInChildren<EquipmentInventoryUI>(true).Single();
        Check(view&&view.grid.constraint==GridLayoutGroup.Constraint.FixedColumnCount&&view.Columns==authored.Columns,"Inventory must preserve authored column binding");
        Check(view.backpack.Cast<Transform>().Count(t=>t.gameObject.activeSelf)==authored.Columns*4,"Inventory has four authored rows");
        Check(!view.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Equipment"||t.text=="Equipment saved."||t.text=="Inventory"||t.text=="Sale"),"Removed inventory labels must stay absent");
        foreach(var cell in view.backpack.Cast<RectTransform>().Where(t=>t.gameObject.activeSelf)){
            var corners=new Vector3[4];cell.GetWorldCorners(corners);
            foreach(var corner in corners){var local=view.backpack.InverseTransformPoint(corner);Check(local.x>=view.backpack.rect.xMin-.1f&&local.x<=view.backpack.rect.xMax+.1f&&local.y>=view.backpack.rect.yMin-.1f&&local.y<=view.backpack.rect.yMax+.1f,"Inventory cell clipped: "+cell.name);}
        }
    }
    static void CheckStats(EquipmentStatsUI view,SqlitePlayerService service){
        Check(view&&view.totalPower.text==$"{service.CatStats.Power:N0}","Total Power must match equipped stats");
        var c=service.CatStats;string[] expected={$"{c.hp:0.##}",$"{c.atk:0.##}",$"{c.def:0.##}",$"{c.aspd:0.00}/s"};
        for(int i=0;i<4;i++)Check(view.values[i].text==expected[i],"Stats value "+i);
        var baseStats=service.EquipmentStats.Base;
        double[] bonus={c.hp-baseStats.hp,c.atk-baseStats.atk,c.def-baseStats.def,c.aspd-baseStats.aspd};
        for(int i=0;i<4;i++){
            string expectedBonus=i==3?$"+{bonus[i]:0.00}/s gear":$"+{bonus[i]:0.##} gear";
            if(bonus[i]<=0)expectedBonus="";
            Check(view.bonuses[i]&&view.bonuses[i].text==expectedBonus,"Equipment/set bonus "+i);
            Check(!view.values[i].transform.parent.GetComponent<Image>().enabled,"Stat track still visible");
            Check(!view.values[i].transform.parent.Find("Fill"),"Obsolete stat fill remains");
        }
        foreach(var text in view.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();if(text.isTextOverflowing){textFailures.Add("Stats text overflow: "+text.text+" active="+text.gameObject.activeInHierarchy+" rect="+text.rectTransform.rect+" preferred="+text.GetPreferredValues(text.text)+" font="+text.fontSize+" mode="+text.overflowMode+" parent="+text.transform.parent.name+" canvas="+view.GetComponentInParent<Canvas>().rootCanvas.pixelRect);File.WriteAllText(Folder+"/text-overflow.txt",DateTime.UtcNow.ToString("O")+TextFailureReport);}}
    }
    static void CheckScreenModal(Transform modal){
        var root=modal.GetComponent<Canvas>().rootCanvas;var expected=new Vector3[4];var actual=new Vector3[4];
        ((RectTransform)root.transform).GetWorldCorners(expected);((RectTransform)modal).GetWorldCorners(actual);
        for(int i=0;i<4;i++)Check(Vector3.Distance(expected[i],actual[i])<1,"Modal must cover entire canvas");
        var events=UnityEngine.EventSystems.EventSystem.current;
        foreach(var point in new[]{new Vector2(8,8),new Vector2(Screen.width-8,Screen.height-8),new Vector2(35,Screen.height*.5f),new Vector2(Screen.width*.5f,Screen.height-30)}){
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=point},hits);
            Check(hits.Count>0&&hits[0].gameObject.transform.IsChildOf(modal),"Modal must block screen/header/sidebar "+point);
        }
    }
    static void PointerClick(Vector2 point,Vector2? press=null,UnityEngine.EventSystems.PointerEventData.InputButton button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,bool dragging=false){
        var events=UnityEngine.EventSystems.EventSystem.current;
        var data=new UnityEngine.EventSystems.PointerEventData(events){position=point,pressPosition=press??point,button=button,dragging=dragging};
        var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();events.RaycastAll(data,hits);
        Check(hits.Count>0,"Expected pointer target");data.pointerCurrentRaycast=data.pointerPressRaycast=hits[0];
        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator Run(){
        var statsPrefab=AssetDatabase.LoadAssetAtPath<EquipmentStatsUI>("Assets/Prefabs/EquipmentStats.prefab");
        Check(statsPrefab&&statsPrefab.totalPower&&statsPrefab.values.Length==4&&statsPrefab.bonuses.Length==4&&statsPrefab.bonuses.All(t=>t),"Stats serialized bindings");
        var prefab=AssetDatabase.LoadAssetAtPath<EquipmentItemDetailUI>("Assets/Prefabs/EquipmentItemDetail.prefab");
        Check(prefab&&prefab.card&&prefab.itemName&&prefab.level&&prefab.currentValue&&prefab.nextValue&&prefab.setInfo&&prefab.status&&prefab.equip&&prefab.upgrade&&prefab.close,"Serialized detail bindings");
        foreach(var t in prefab.GetComponentsInChildren<Transform>(true))Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing detail script: "+t.name);
        UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");yield return 2;
        Resolution(1920,1080);var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var manager=GameDataManager.EnsureInstance();var service=manager.Service;Check(manager.SavePath.Contains("EquipmentVerification"),"Must isolate saves");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        nav.ShowPage(MainMenuPage.CatHome);yield return 1;var page=nav.pages[4].GetComponent<EquipmentPageUI>();Check(page.Ready&&!service.EquipmentUnlocked,"Locked fresh state");Capture("equipment-empty-1920");Layout(page);yield return .4;
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);
            nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
            var emptyDashboard=nav.pages[0].GetComponentInChildren<DashboardEquipmentUI>();
            for(int slot=0;slot<3;slot++)Check(emptyDashboard.icons[slot].sprite==emptyDashboard.emptySlotIcons[slot],"Dashboard empty pictogram binding");
            Capture("dashboard-empty-pictograms-"+width);yield return .2;
            nav.ShowPage(MainMenuPage.CatHome);yield return .4;
            for(int slot=0;slot<3;slot++)Check(page.transform.Find("EquipmentSurface/CharacterCard/EquippedGear/Slot"+slot+"/Icon").GetComponent<Image>().sprite==page.emptySlotIcons[slot],"Cat Home empty pictogram binding");
            Capture("cat-home-empty-pictograms-"+width);yield return .2;
        }
        Resolution(1920,1080);yield return .4;
        var sourceInventory=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Equipment.prefab");
        var reference=UnityEngine.Object.Instantiate(sourceInventory.transform.Find("Right_Panel").gameObject,page.GetComponentInParent<Canvas>().rootCanvas.transform);
        var referenceRect=(RectTransform)reference.transform;referenceRect.anchorMin=referenceRect.anchorMax=referenceRect.pivot=new Vector2(.5f,.5f);referenceRect.sizeDelta=new Vector2(1280,1280);referenceRect.anchoredPosition=Vector2.zero;referenceRect.localScale=Vector3.one*.70f;
        yield return .4;Capture("inventory-source-light");yield return .2;UnityEngine.Object.Destroy(reference);yield return .2;
        var statsReference=UnityEngine.Object.Instantiate(sourceInventory.transform.Find("Panel_Left").gameObject,page.GetComponentInParent<Canvas>().rootCanvas.transform);
        var statsReferenceRect=(RectTransform)statsReference.transform;statsReferenceRect.anchorMin=statsReferenceRect.anchorMax=statsReferenceRect.pivot=new Vector2(.5f,.5f);statsReferenceRect.sizeDelta=new Vector2(1200,1280);statsReferenceRect.anchoredPosition=Vector2.zero;statsReferenceRect.localScale=Vector3.one*.7f;
        yield return .4;Capture("stats-source-light");yield return .2;UnityEngine.Object.Destroy(statsReference);yield return .2;
        yield return Command(FixtureAsync(d=>{d.experience=2025;d.fishCoins=1000;d.equipmentUnlocked=true;d.rewardDays.Add(new RewardDay{day="2026-10-20",questsClaimed=true});foreach(var p in d.bossProgress){p.wins=15;p.pendingClaims=3;}}));
        Check(service.EquipmentUnlocked,"Daily reward fixture must unlock equipment before claim UI opens");
        nav.ShowPage(MainMenuPage.Boss);yield return 1;var boss=nav.pages[3].GetComponent<BossPageUI>();boss.stageButton.onClick.Invoke();yield return .4;boss.masteryBars[0].transform.parent.GetComponent<Button>().onClick.Invoke();yield return .5;
        page=nav.pages[3].GetComponent<EquipmentPageUI>();Check(nav.CurrentPage==MainMenuPage.Boss,"Stage rewards must stay on Boss");Capture("equipment-claim-1920");Layout(page);yield return .4;
        Click(page.transform,"ItemModal/Card/Choices/ChoiceArea0/Choice0");yield return Ui(service);Check(service.EquipmentSnapshot.equipment.Count==1&&service.EquipmentSnapshot.bossProgress[0].pendingClaims==2,"Chest claim saved directly");Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Chest does not need a second confirmation");
        nav.ShowPage(MainMenuPage.CatHome);yield return .5;page=nav.pages[4].GetComponent<EquipmentPageUI>();page.OpenItem("boss1-weapon");
        Click(page.transform,"ItemDetail/Popup/Button_Select");yield return Ui(service);Check(service.EquipmentSnapshot.equippedWeaponId=="boss1-weapon","Equip saved directly");
        Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Equip must not ask for confirmation");
        var equipReload=ReloadAsync();foreach(var delay in Wait(equipReload))yield return delay;Check(equipReload.Result.equippedWeaponId=="boss1-weapon","Direct equip persisted");
        IDisposable blocked=TaskCalendarPlayVerification.FailSqliteWrites();
        try{
            Click(page.transform,"ItemDetail/Popup/Button_Select");yield return Ui(service);
            Check(service.EquipmentSnapshot.equippedWeaponId=="boss1-weapon","Rejected direct unequip must preserve equipment");
            Check(page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>().status.text==service.LastError&&!string.IsNullOrEmpty(service.LastError),"Direct failure must be visible");
            Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Failure must not open confirmation");
        }finally{blocked.Dispose();}
        for(int repeat=0;repeat<4;repeat++){Click(page.transform,"ItemDetail/Popup/Button_Select");yield return Ui(service);}
        Check(service.EquipmentSnapshot.equippedWeaponId=="boss1-weapon"&&service.EquipmentSnapshot.fishCoins==1000,"Repeated direct toggles preserve state and currency");
        var upgradeFeedback=page.transform.Find("ItemDetail/Popup/Button_Price_Coin").GetComponent<EquipmentUpgradeFeedback>();
        var hoverEvent=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        upgradeFeedback.OnPointerEnter(hoverEvent);yield return .15;
        Check(upgradeFeedback.caption.text=="+0.5 ATK"&&upgradeFeedback.effectView.gameObject.activeSelf,"Upgrade hover previews delta and effect");
        Check(upgradeFeedback.caption.transform.parent.name=="StatsList 1","Upgrade delta belongs beside current stat");
        Check(!page.transform.Find("ItemDetail/Popup/StatsList 2").gameObject.activeSelf,"Next-level card removed from view");
        Check(service.EquipmentSnapshot.fishCoins==1000&&service.EquipmentSnapshot.equipment[0].level==1,"Hover does not upgrade or charge");
        for(int frame=0;frame<5;frame++){Capture("upgrade-hover-motion-"+frame);yield return .16;}
        upgradeFeedback.OnPointerExit(hoverEvent);yield return .1;
        Check(!upgradeFeedback.effectView.gameObject.activeSelf&&!upgradeFeedback.caption.gameObject.activeSelf,"Pointer exit clears upgrade preview");
        upgradeFeedback.OnSelect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));yield return .1;
        Check(upgradeFeedback.effectView.gameObject.activeSelf,"Keyboard selection previews upgrade");
        upgradeFeedback.GetComponent<Button>().interactable=false;yield return .1;
        Check(!upgradeFeedback.effectView.gameObject.activeSelf,"Disabled upgrade clears preview");upgradeFeedback.GetComponent<Button>().interactable=true;
        Check(!UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(t=>t.name=="TaskStatus"),"Bottom notification strip removed");
        var batch=page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>().bulkUpgrade;
        Click(page.transform,"ItemDetail/Popup/Button_Price_Coin");yield return .3;
        Check(batch.gameObject.activeInHierarchy&&service.EquipmentSnapshot.fishCoins==1000,"Upgrade opens preview before spending");
        var levelSlider=batch.GetComponentInChildren<Slider>();
        var buyUpgrade=batch.transform.Find("Popup/Button_Confirm").GetComponent<Button>();
        var cancelUpgrade=batch.transform.Find("Popup/Button_Cancel").GetComponent<Button>();
        var upgradePrice=buyUpgrade.GetComponentInChildren<TMP_Text>();
        Check(levelSlider.wholeNumbers&&levelSlider.minValue==1&&upgradePrice.text=="15","Upgrade starts at one level and shows cost");
        levelSlider.value=3;
        Check(upgradePrice.text=="60"&&batch.transform.Find("Popup/Level Up/Text_Value (1)").GetComponent<TMP_Text>().text=="4","Slider previews target level and cumulative price");
        Capture("bulk-upgrade-1920");yield return .3;
        Resolution(1280,720);yield return .45;Capture("bulk-upgrade-1280");yield return .3;
        cancelUpgrade.onClick.Invoke();
        Check(!batch.gameObject.activeSelf&&service.EquipmentSnapshot.fishCoins==1000,"Cancel preserves level and balance");
        Resolution(1920,1080);yield return .3;
        Click(page.transform,"ItemDetail/Popup/Button_Price_Coin");levelSlider.value=3;
        blocked=TaskCalendarPlayVerification.FailSqliteWrites();
        try{
            buyUpgrade.onClick.Invoke();yield return Ui(service);
            Check(service.EquipmentSnapshot.fishCoins==1000 && service.EquipmentSnapshot.equipment[0].level==1,"Rejected upgrade charged coins");
            Check(batch.gameObject.activeSelf&&batch.transform.Find("Popup/UpgradeStatus").GetComponent<TMP_Text>().text==service.LastError&&!string.IsNullOrEmpty(service.LastError),"Upgrade failure appears in the batch popup");
        }finally{blocked.Dispose();}
        buyUpgrade.onClick.Invoke();buyUpgrade.onClick.Invoke();yield return Ui(service);
        Check(!batch.gameObject.activeSelf&&service.EquipmentSnapshot.fishCoins==940&&service.EquipmentSnapshot.equipment[0].level==4,"Retry commits the batch exactly once");yield return .5;Capture("equipment-item-1920");yield return .4;Layout(page);
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
        var blueDashboard=nav.pages[0].GetComponentInChildren<DashboardEquipmentUI>();Capture("dashboard-blue-stats-before-check-1920");yield return .2;CheckStats(blueDashboard.GetComponentInChildren<EquipmentStatsUI>(),service);
        Check(blueDashboard.slots[0].GetComponent<Image>().color==Color.Lerp(new Color32(67,184,255,255),Color.white,.75f),"Dashboard blue equipped background");
        Capture("dashboard-blue-1920");yield return .3;
        nav.ShowPage(MainMenuPage.CatHome);yield return .2;page.OpenItem("boss1-weapon");yield return .2;
        Click(page.transform,"ItemDetail/Popup/Button_Select");yield return Ui(service);Check(string.IsNullOrEmpty(service.EquipmentSnapshot.equippedWeaponId),"Unequip saved directly");
        Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Unequip must not ask for confirmation");
        yield return .3;Capture("direct-unequip-1920");yield return .2;
        foreach(var def in EquipmentRules.Catalog)if(!service.EquipmentSnapshot.equipment.Any(x=>x.id==def.Id))yield return Command(service.ClaimEquipmentAsync(def.Id));
        foreach(var def in EquipmentRules.Catalog.Where(x=>x.Tier==3))yield return Command(service.EquipAsync(def.Id,def.Slot));
        foreach(var setId in new[]{"boss1-weapon","boss2-body","boss3-head"}){
            page.OpenItem(setId);yield return .2;
            var setView=page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
            int tierIndex=EquipmentRules.Find(setId).Tier;
            Check(setView.grade.sprite==setView.tierLabels[tierIndex-1],"Set badge follows equipment tier");
            Check(setView.setBonusTitles.All(t=>!t.text.Contains("ACTIVE")),"Set cards omit ACTIVE text");
            foreach(var card in setView.setBonusCards){Color.RGBToHSV(card.color,out _,out float saturation,out _);Check((saturation>.15f)==(tierIndex==3),"Set card colour reflects equipped threshold");}
            Capture("set-cards-tier-"+tierIndex);yield return .3;
        }
        Click(page.transform,"ItemDetail/Popup/TopFrame/Button_Close");page.Refresh();yield return .8;Layout(page);Capture("equipment-full-1920");yield return .5;
        var inventoryView=page.GetComponentInChildren<EquipmentInventoryUI>();
        Check(inventoryView&&inventoryView.backpack&&inventoryView.filters.Length==4&&inventoryView.autoSelect,"Inventory serialized control bindings");
        for(int f=1;f<=3;f++){Click(inventoryView.filters[f]);yield return .1;Check(inventoryView.backpack.Cast<Transform>().Count(t=>t.name.StartsWith("Item_")&&t.gameObject.activeSelf)==3,"Category filter");}
        Click(inventoryView.filters[0]);
        yield return Command(service.UnequipAsync(EquipmentSlot.Head));
        blocked=TaskCalendarPlayVerification.FailSqliteWrites();
        try{
            Click(inventoryView.autoSelect);yield return Ui(service);
            Check(string.IsNullOrEmpty(service.EquipmentSnapshot.equippedHeadAccessoryId),"Blocked Auto Select changed equipment");
            Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Auto Select opened confirmation");
            Check(page.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==service.LastError),"Auto Select failure missing");
        }finally{blocked.Dispose();}
        Click(inventoryView.autoSelect);yield return Ui(service);Check(service.EquipmentSnapshot.equippedHeadAccessoryId=="boss3-head","Auto Select saved best set directly");
        Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Auto Select must not confirm");
        int autoCoins=service.EquipmentSnapshot.fishCoins;
        for(int repeat=0;repeat<3;repeat++){Click(inventoryView.autoSelect);yield return Ui(service);}
        Check(service.EquipmentSnapshot.equippedHeadAccessoryId=="boss3-head"&&service.EquipmentSnapshot.fishCoins==autoCoins,"Repeated Auto Select changed balance or best loadout");
        var autoReload=ReloadAsync();foreach(var delay in Wait(autoReload))yield return delay;Check(autoReload.Result.equippedHeadAccessoryId=="boss3-head","Auto Select did not persist");
        yield return .2;Check(page.GetComponentsInChildren<EquipmentGradeEffect>(true).Length==0,"Equipment uses rounded UI tiles without animated borders");
        Resolution(1280,720);yield return .8;Layout(page);Capture("equipment-full-1280");yield return .5;
        foreach(var control in inventoryView.filters.Concat(new[]{inventoryView.autoSelect})){
            var feedback=control.GetComponent<JuicyButton>();Check(feedback,"Inventory control feedback missing");
            var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            feedback.OnPointerEnter(data);yield return .2;feedback.OnPointerDown(data);yield return .15;Check(feedback.DrawScale.y<1,"Inventory control press feedback");
            feedback.OnPointerUp(data);feedback.OnPointerExit(data);yield return .35;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.02f,"Inventory control feedback settles");
            var eventSystem=UnityEngine.EventSystems.EventSystem.current;
            var point=RectTransformUtility.WorldToScreenPoint(null,control.transform.position);
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem){position=point},hits);
            var handler=hits.Count>0 ? UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(hits[0].gameObject) : null;
            Check(handler==control.gameObject,$"Inventory control {control.name} expected a click at {point}, hit {handler?.name ?? "none"}.");
            feedback.OnSelect(new UnityEngine.EventSystems.BaseEventData(eventSystem));yield return .15;
            feedback.OnSubmit(new UnityEngine.EventSystems.BaseEventData(eventSystem));bool submitted=false;float until=Time.realtimeSinceStartup+.3f;
            while(Time.realtimeSinceStartup<until){yield return .02;submitted|=feedback.DrawScale.y<1;}Check(submitted,"Inventory keyboard feedback");
            feedback.OnDeselect(new UnityEngine.EventSystems.BaseEventData(eventSystem));
            control.interactable=false;yield return .2;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.03f,"Inventory disabled feedback reset");control.interactable=true;
        }
        var itemTransform=inventoryView.backpack.Find("Item_boss3-head");Check(itemTransform,"Owned head item must be present in bound inventory backpack");var itemButton=itemTransform.GetComponent<JuicyButton>();Check(itemButton,"Inventory button feedback missing");itemButton.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));yield return .4;Capture("equipment-hover-1280");yield return .3;itemButton.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));yield return .4;Capture("equipment-pressed-1280");yield return .3;itemButton.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));itemButton.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
        PointerClick(RectTransformUtility.WorldToScreenPoint(null,itemButton.transform.position));Check(page.SelectedItem=="boss3-head"&&page.transform.Find("ItemDetail").gameObject.activeSelf,"Inventory item raycast opens its detail");
        page.OpenItem("boss3-head");yield return .5;Layout(page);Capture("equipment-item-1280");yield return .4;
        page.OpenItem("boss1-weapon");yield return .5;Layout(page);Capture("equipment-weapon-1280");yield return .3;
        var detail=page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
        var events=UnityEngine.EventSystems.EventSystem.current;
        foreach(var button in new[]{detail.equip,detail.upgrade,detail.close}){
            var feedback=button.GetComponent<JuicyButton>();Check(feedback,"Detail feedback missing");
            var rect=(RectTransform)button.transform;var before=rect.rect;
            var pointer=new UnityEngine.EventSystems.PointerEventData(events){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center))};
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();events.RaycastAll(pointer,hits);
            Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"Detail button raycast blocked: "+button.name+" at "+pointer.position+" hit "+(hits.Count>0?hits[0].gameObject.name:"none"));
            feedback.OnPointerEnter(pointer);yield return .12;Check(feedback.DrawScale.x>1,"Hover feedback");
            if(button==detail.upgrade){Capture("detail-hover");yield return .1;}
            feedback.OnPointerDown(pointer);yield return .12;Check(feedback.DrawScale.y<1,"Press feedback");
            if(button==detail.upgrade){Capture("detail-press");yield return .1;}
            for(int repeat=0;repeat<5;repeat++){feedback.OnPointerUp(pointer);feedback.OnPointerDown(pointer);}
            feedback.OnPointerExit(pointer);feedback.OnDeselect(new UnityEngine.EventSystems.BaseEventData(events));yield return .6;
            Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f&&rect.rect==before,"Pointer exit must settle without moving hit area");
            feedback.OnSelect(new UnityEngine.EventSystems.BaseEventData(events));yield return .15;Check(feedback.DrawScale.x>1,"Keyboard selection");
            feedback.OnSubmit(new UnityEngine.EventSystems.BaseEventData(events));
            bool sawSubmit=false;float submitDeadline=Time.realtimeSinceStartup+.35f;
            while(Time.realtimeSinceStartup<submitDeadline){yield return .02;sawSubmit|=feedback.DrawScale.y<1;}
            Check(sawSubmit,"Keyboard submit must animate a press: "+button.name);
            feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus loss reset");
            feedback.OnPointerDown(pointer);button.interactable=false;yield return .6;Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f,"Disabled reset");button.interactable=true;
            feedback.OnPointerEnter(pointer);detail.gameObject.SetActive(false);Check(feedback.DrawScale==Vector2.one,"Close reset");detail.gameObject.SetActive(true);yield return .2;
        }
        // Capture consecutive Play Mode frames of the actual mesh animation.
        var motion=detail.upgrade.GetComponent<JuicyButton>();
        var motionPointer=new UnityEngine.EventSystems.PointerEventData(events);
        for(int frame=0;frame<12;frame++){
            if(frame==0)motion.OnPointerEnter(motionPointer);
            if(frame==4)motion.OnPointerDown(motionPointer);
            if(frame==8){motion.OnPointerUp(motionPointer);motion.OnPointerExit(motionPointer);}
            yield return .04;Capture("detail-motion-"+frame.ToString("00"));yield return .04;
        }
        detail.close.onClick.Invoke();Check(!detail.gameObject.activeSelf,"Detail dismissal");page.OpenItem("boss1-weapon");
        int savedFish=service.FishCoins;yield return Command(FixtureAsync(d=>d.fishCoins=0));page.OpenItem("boss1-weapon");yield return .3;
        Check(!detail.upgrade.interactable&&!string.IsNullOrEmpty(detail.status.text),"Insufficient Fish explanation");Layout(page);Capture("detail-no-fish-1280");yield return .3;
        yield return Command(FixtureAsync(d=>d.fishCoins=savedFish));
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;var mini=nav.pages[0].GetComponentInChildren<DashboardEquipmentUI>();Check(mini.slots.All(b=>b.interactable)&&mini.icons.All(i=>i.color==Color.white),"Dashboard gear not refreshed");Check(mini.GetComponentsInChildren<EquipmentGradeEffect>(true).Length==0,"Dashboard uses rounded UI tiles without animated borders");Capture("dashboard-equipment-grades");yield return .4;mini.slots[2].onClick.Invoke();yield return .5;Check(page.SelectedItem=="boss3-head"&&nav.CurrentPage==MainMenuPage.CatHome,"Dashboard slot link");
        var disk=ReloadAsync();foreach(var delay in Wait(disk))yield return delay;var loaded=disk.Result;Check(loaded.equipment.Count==9&&EquipmentRules.Calculate(loaded).Final.aspd==service.CatStats.aspd,"Disk reload");
        nav.ShowPage(MainMenuPage.Boss);yield return 1;boss.actors.ResetPose();boss.actors.StartCombat(BattleRules.Cat(service.Experience),BattleRules.Boss(1));float normal=boss.actors.CatAnimationSpeed;boss.actors.StartCombat(service.CatStats,BattleRules.Boss(1));Check(Math.Abs(boss.actors.CatAnimationSpeed/normal-service.CatStats.aspd)<.001,"Equipped ASPD animation");
        boss.stagePicker.SetActive(false);boss.actors.Run(2);
        for(int tier=1;tier<=3;tier++){
            boss.actors.StartCombat(service.CatStats,BattleRules.Boss(1),"boss"+tier+"-weapon");boss.actors.Hit(true);
            Check(boss.actors.WeaponEffectTier==tier,"Weapon grade slash mapping");
            var slash=boss.arena.Stage.Find("Weapon Slash Grade "+tier);Check(slash,"Slash prefab missing");
            foreach(var ps in slash.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(.14f,false,true);ps.Pause(false);}
            Capture("weapon-slash-"+tier);yield return .4;
        }
        boss.actors.ResetPose();Check(!boss.arena.Stage.Find("Weapon Slash Grade 3").GetComponentsInChildren<ParticleSystem>().Any(p=>p.IsAlive()),"Slash cleanup");
        var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
        var toggle=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(sidebar);
        bool oldExpanded=toggle.isOn;
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}){
            Resolution(size.x,size.y);
            foreach(bool expanded in new[]{false,true}){
                toggle.isOn=expanded;yield return .7;
                string context=size.x+"-"+(expanded?"expanded":"collapsed");
                nav.ShowPage(MainMenuPage.Dashboard);yield return .5;
                var dashboard=nav.pages[0].GetComponentInChildren<DashboardEquipmentUI>();
                CheckStats(dashboard.GetComponentInChildren<EquipmentStatsUI>(),service);
                var powerCorners=new Vector3[4];var slotCorners=new Vector3[4];
                ((RectTransform)dashboard.GetComponentInChildren<EquipmentStatsUI>().totalPower.transform.parent).GetWorldCorners(powerCorners);
                foreach(var slotButton in dashboard.slots){
                    var slotRect=(RectTransform)slotButton.transform;slotRect.GetWorldCorners(slotCorners);
                    Check(Mathf.Abs((slotCorners[2].x-slotCorners[0].x)-(slotCorners[2].y-slotCorners[0].y))<.5f,"Dashboard equipment slot must remain square: "+slotButton.name+" "+context);
                    float bottom=slotCorners[0].y;
                    foreach(var graphic in slotButton.GetComponentsInChildren<Graphic>()) {
                        var mesh=graphic.canvasRenderer.GetMesh();
                        if(!graphic.enabled || !mesh)continue;
                        foreach(var vertex in mesh.vertices) bottom=Mathf.Min(bottom,graphic.transform.TransformPoint(vertex).y);
                    }
                    Check(bottom>powerCorners[2].y+4,$"Power must not overlap the rendered equipment frame: {context}, gap={bottom-powerCorners[2].y:F2}.");
                }
                for(int i=0;i<3;i++)Check(dashboard.slots[i].GetComponent<Image>().color==Color.Lerp(new Color32(255,201,65,255),Color.white,.75f),"Dashboard equipped grade background");
                Capture("dashboard-stats-"+context);yield return .2;
                nav.ShowPage(MainMenuPage.CatHome);yield return .3;page=nav.pages[4].GetComponent<EquipmentPageUI>();
                CheckStats(page.GetComponentInChildren<EquipmentStatsUI>(),service);Layout(page);CheckInventory(page);
                Capture("cat-home-stats-"+context);yield return .2;
                page.OpenItem("boss1-weapon");yield return .3;
                CheckScreenModal(page.transform.Find("ItemDetail"));
                Capture("fullscreen-detail-"+context);yield return .2;
                var dismissDetail=page.transform.Find("ItemDetail").GetComponent<EquipmentItemDetailUI>();
                var inside=RectTransformUtility.WorldToScreenPoint(null,dismissDetail.card.TransformPoint(new Vector3(0,-dismissDetail.card.rect.height*.2f,0)));
                var outside=new Vector2(35,Screen.height*.5f);
                PointerClick(inside);Check(dismissDetail.gameObject.activeSelf,"Card blank area must not dismiss");
                PointerClick(outside,inside);Check(dismissDetail.gameObject.activeSelf,"Press inside/release outside must not dismiss");
                PointerClick(outside,button:UnityEngine.EventSystems.PointerEventData.InputButton.Right);Check(dismissDetail.gameObject.activeSelf,"Right-click must not dismiss");
                PointerClick(outside,dragging:true);Check(dismissDetail.gameObject.activeSelf,"Drag must not dismiss");
                PointerClick(outside);Check(!dismissDetail.gameObject.activeSelf&&nav.CurrentPage==MainMenuPage.CatHome,"Outside click closes without reaching navigation");
                Capture("outside-dismiss-"+context);yield return .2;
                page.OpenItem("boss1-weapon");yield return .2;
                PointerClick(new Vector2(Screen.width*.5f,Screen.height-30));Check(!dismissDetail.gameObject.activeSelf,"Reopened dismissDetail closes from header backdrop");
                page.OpenItem("boss1-weapon");yield return .2;
                Click(page.transform,"ItemDetail/Popup/TopFrame/Button_Close");
                Click(inventoryView.autoSelect);yield return Ui(service);yield return .2;
                Check(!page.transform.Find("ConfirmEquipment").gameObject.activeSelf,"Auto Select must remain direct at every size");
            }
        }
        nav.ShowPage(MainMenuPage.Tasks);yield return .4;
        var taskUI=nav.pages[1].GetComponentInChildren<TaskSystemUI>();
        taskUI.Notify("Task restored. Previously earned rewards are kept.");
        var notice=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(t=>t.name=="TaskConfirmDialog");
        Check(!notice.gameObject.activeSelf,"Success notice does not interrupt UI");
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .4;
            taskUI.Notify("Could not save the change. Please try again.",true);yield return .2;
            Check(notice.gameObject.activeSelf&&!notice.Find("Popup/Cancel").gameObject.activeSelf,"Errors remain visible without a confirmation choice");
            Capture("task-error-notice-"+width);yield return .2;
            Click(notice,"Popup/Confirm");Check(!notice.gameObject.activeSelf,"Error notice dismisses");
        }
        toggle.isOn=oldExpanded;
        Check(error==null,error);Resolution(1920,1080);
        Check(textFailures.Count==0,"Business/UI flow completed but text overflow checks failed; see accumulated details and captures.");
    }
}

