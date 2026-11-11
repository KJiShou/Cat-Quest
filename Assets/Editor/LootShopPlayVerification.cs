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
public static class LootShopPlayVerification
{
    const string Flag = "CatQuest.LootShopPlayVerification";
    const string Folder = "Library/LootShopPlayVerification";
    static System.Collections.IEnumerator run;
    static double next;
    static RewardScreenshotCapture capture;
    static FileStream saveLock;
    static string FolderNow => SessionState.GetString(Flag + ".folder", Folder);
    static EditorWindow View => EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty => View.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static object SizeGroup()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var method=type.GetMethod("GetGroup");return method.Invoke(sizes,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,0)});
    }
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static LootShopPlayVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { run = Run(); next = EditorApplication.timeSinceStartup + 3; }
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
    static bool Inside(RectTransform child,RectTransform parent){var corners=new Vector3[4];child.GetWorldCorners(corners);return corners.All(c=>parent.rect.Contains(parent.InverseTransformPoint(c)));}
    static void Resolution(int width, int height) => typeof(DashboardRewardPlayVerification).GetMethod("Resolution", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { width, height });
    static void Capture(string name)
    {
        if (!capture) capture = new GameObject("Loot Shop Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Loot Shop Play Mode verification exceeded 240 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else EditorApplication.isPlaying=false; } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-loot-play.request")) return;
        File.Delete("Temp/verify-loot-play.request");
        string folder=Folder+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-after";Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+".folder",folder);
        string previous=Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE");
        SessionState.SetBool(Flag+".hadStore",previous!=null);SessionState.SetString(Flag+".store",previous??"");
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(View));SessionState.SetInt(Flag+".count",Count(SizeGroup(),"GetCustomCount"));
        SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddSeconds(240).Ticks.ToString());
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(folder+"/save.json"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        SessionState.SetBool(Flag, true); Resolution(1920, 1080); EditorApplication.isPaused=false; EditorApplication.isPlaying = true;
    }
    static System.Collections.IEnumerator Run(){
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("LootShopPlayVerification"),"Isolated save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        manager.PlayerData.experience=2025;manager.PlayerData.tickets=50;manager.PlayerData.fishCoins=1000;manager.PlayerData.equipmentUnlocked=true;manager.SaveData();
        var service=manager.Tasks;var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.CatHome);
        var page=nav.pages[4].GetComponent<EquipmentPageUI>();var shop=page.GetComponentInChildren<LootShopUI>(true);
        Check(shop&&shop.art.icons.Length==18&&shop.art.blueOpening.Length>40&&shop.art.goldOpening.Length>40,"Authored shop, icons and animation frames");
        foreach(var frames in new[]{shop.art.blueOpening,shop.art.purpleOpening,shop.art.goldOpening})
            Check(frames.All(f=>f&&f.rect.width>250&&f.rect.height>180),"Every imported frame binds the chest body, not detached particles");
        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(shop.gameObject)==0,"No missing shop scripts");
        var launch=page.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ShopLauncher");
        Check(((Image)launch.targetGraphic).pixelsPerUnitMultiplier==2,"Cat Home Shop pixel multiplier");
        yield return 2.0;
        Capture("cat-home-entry-1920");yield return .2;
        launch.onClick.Invoke();yield return .5;Check(shop.gameObject.activeInHierarchy,"Shop launcher");
        foreach(int width in new[]{1920,1280,1024,2560}){
            Resolution(width,width==1920||width==2560?1080:width==1024?768:720);yield return .5;
            foreach(var cell in shop.catalog){Check(cell.icon.sprite&&cell.slotIcon.sprite&&cell.title.text.Length>0&&!cell.gameObject.activeInHierarchy,"Catalog hidden on shop home");}
            Check(!shop.informationPanel.activeSelf,"Rates hidden by default");
            Capture("shop-"+width);yield return .2;
        }
        Resolution(1920,1080);yield return .3;
        Check(shop.panel.GetComponentsInChildren<Image>(true).Count(i=>i.name.StartsWith("RarityLabel"))==3,"Three equipment rarity badges");
        foreach(var b in shop.GetComponentsInChildren<Button>(true))if(b.targetGraphic is Image bi&&bi.type==Image.Type.Sliced)Check(bi.pixelsPerUnitMultiplier==2,"Button pixel multiplier");
        Check(shop.catalog.All(c=>c.ownedIcon&&!c.ownedIcon.gameObject.activeSelf&&!c.ownedIcon.raycastTarget),"Unowned catalog has serialized non-blocking markers hidden");
        Check(!shop.informationText.gameObject.activeSelf&&string.IsNullOrEmpty(shop.informationText.text),"Removed independent draws footer");
        foreach(int tier in new[]{1,2,3}){
            object[] args={1,"owned-marker-"+tier,null,(Func<int,int>)(max=>max==100?(tier==3?0:1):max==99?(tier==2?0:10):0)};
            Check((bool)typeof(TaskService).GetMethod("DrawLoot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(service,args),"Acquire ownership fixture");
        }
        for(int i=0;i<shop.catalog.Length;i++)Check(shop.catalog[i].ownedIcon.gameObject.activeSelf==service.OwnsEquipment(LootRules.Catalog[i].Id),"Ownership refreshes on committed service change");
        yield return 4.0; // Let the fixture's currency feedback settle before visual comparison.
        shop.information.onClick.Invoke();yield return .3;Check(shop.informationPanel.activeSelf&&shop.rarityRates[2].text=="89%"&&shop.duplicateValues[0].text=="+300"&&shop.catalog.All(c=>c.gameObject.activeInHierarchy),"Information rates and duplicate amounts");Capture("information-1920");yield return .2;
        var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_UI_Etc/Toggle_Check_Light.prefab"),shop.informationPanel.transform,false);
        var referenceRect=(RectTransform)reference.transform;referenceRect.anchorMin=referenceRect.anchorMax=referenceRect.pivot=new Vector2(.5f,.5f);referenceRect.anchoredPosition=Vector2.zero;
        yield return .2;Capture("simplecasual-check-reference");yield return .2;UnityEngine.Object.Destroy(reference);yield return .2;
        Resolution(1280,720);yield return .3;Capture("information-1280");yield return .2;shop.Close();
        shop.OpenInformation();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .3;
            var cell=shop.catalog[0];var point=RectTransformUtility.WorldToScreenPoint(null,cell.transform.position);
            cell.OnPointerEnter(new PointerEventData(EventSystem.current){position=point});yield return .3;
            Check(shop.hoverDetail.gameObject.activeSelf&&shop.hoverDetail.IsHoverPreview&&!shop.hoverDetail.equip.gameObject.activeSelf,"Read-only equipment hover");Capture("hover-"+width);yield return .2;
            Check(Inside(shop.hoverDetail.itemIcon.rectTransform,shop.hoverDetail.card)&&Inside(shop.hoverDetail.itemName.rectTransform,shop.hoverDetail.card)&&Inside(shop.hoverDetail.currentValue.rectTransform,shop.hoverDetail.card),"Hover content remains inside compact card");
            cell.OnPointerMove(new PointerEventData(EventSystem.current){position=new Vector2(Screen.width-2,2)});yield return .2;
            var corners=new Vector3[4];shop.hoverDetail.card.GetWorldCorners(corners);Check(corners.All(c=>c.x>=0&&c.x<=Screen.width&&c.y>=0&&c.y<=Screen.height),"Hover stays within screen at corner");
            cell.OnPointerExit(new PointerEventData(EventSystem.current));Check(!shop.hoverDetail.gameObject.activeSelf,"Hover exit cleanup");
        }
        shop.CloseInformation();
        Resolution(1920,1080);yield return .3;
        var rays=new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=new Vector2(80,Screen.height-50)},rays);
        Check(rays.Count>0&&rays[0].gameObject.transform.IsChildOf(shop.transform),"Shop blocks underlying header/sidebar clicks");
        ExecuteEvents.Execute(shop.single.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
        Check(shop.confirmation.activeSelf&&EventSystem.current.currentSelectedGameObject==shop.cancel.gameObject,"Keyboard submit opens confirmation with cancel focused");
        shop.cancel.onClick.Invoke();
        int tickets=service.Tickets;shop.Prepare(10);yield return .3;Capture("confirm-1920");yield return .2;shop.cancel.onClick.Invoke();Check(service.Tickets==tickets,"Cancel spends nothing");
        var feedback=shop.ten.GetComponent<JuicyButton>();var pointer=new PointerEventData(EventSystem.current);var rect=(RectTransform)shop.ten.transform;var size=rect.rect.size;
        feedback.OnPointerEnter(pointer);yield return .3;Check(feedback.DrawScale.x>1,"Hover feedback");
        feedback.OnPointerDown(pointer);yield return .2;Check(feedback.DrawScale.y<1,"Press feedback");
        for(int i=0;i<5;i++){feedback.OnPointerUp(pointer);feedback.OnPointerDown(pointer);}feedback.OnPointerExit(pointer);feedback.OnDeselect(pointer);yield return .7;
        Check(Vector2.Distance(feedback.DrawScale,Vector2.one)<.01f&&rect.rect.size==size,"Stable hitbox and reset after rapid input");
        feedback.SendMessage("OnApplicationFocus",false);Check(feedback.DrawScale==Vector2.one,"Focus reset");
        shop.Prepare(10);saveLock=new FileStream(manager.SavePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);shop.Purchase();
        Check(shop.confirmation.activeSelf&&service.Tickets==tickets&&!shop.reveal.activeSelf,"Save failure keeps confirmation and does not reveal");saveLock.Dispose();saveLock=null;
        shop.Purchase();shop.Purchase();Check(service.Tickets==tickets-10&&service.LastLootReceipt.count==10,"Ten pull exactly once");
        float revealStart=Time.unscaledTime;bool flewDuringOpening=false;
        int previous=shop.RevealedCount;bool sawFlight=false;
        for(int frame=0;frame<90&&shop.Revealing;frame++){
            Check(shop.RevealedCount>=previous&&shop.RevealedCount<=previous+1,"Ten items land individually in order");previous=shop.RevealedCount;
            Check(shop.results.GetComponentsInChildren<LootShopItemUI>().Count(c=>c.visibility.alpha==1)==shop.RevealedCount,"Only landed equipment visible");
            Check(service.Tickets==tickets-10,"Animation never spends tickets");
            if(shop.flyingIcon.gameObject.activeSelf){sawFlight=true;if(shop.ChestFrameIndex<shop.art.Opening(service.LastLootReceipt.drops.Max(d=>EquipmentRules.Find(d.itemId).Tier)).Length-1)flewDuringOpening=true;Capture("flight-"+frame.ToString("00"));}
            Check(shop.chest.sprite.rect.width>250&&shop.chest.sprite.rect.height>180,"Chest body remains visible throughout animation");
            yield return .1;
        }
        Check(flewDuringOpening&&Time.unscaledTime-revealStart<5,"Ten pull starts during opening and finishes in under five seconds");
        Check(sawFlight&&shop.RevealedCount==10&&!shop.Revealing&&shop.results.gameObject.activeSelf,"Sequential reveal naturally finishes");
        Check(shop.results.childCount==10,"Ten visible results");Capture("results-1920");yield return .2;
        Resolution(1280,720);yield return .4;Capture("results-1280");yield return .2;
        var resultItem=shop.results.GetChild(0).GetComponent<LootShopItemUI>();resultItem.OnPointerEnter(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,resultItem.transform.position)});yield return .2;
        Check(shop.hoverDetail.gameObject.activeSelf,"Result equipment hover");Capture("result-hover-1280");yield return .2;resultItem.OnPointerExit(new PointerEventData(EventSystem.current));
        shop.done.onClick.Invoke();Check(!shop.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(b=>b.name=="LastResult"||b.GetComponentInChildren<TMPro.TMP_Text>(true)?.text=="Last result"),"Last Result removed");
        shop.Prepare(1);shop.Purchase();Check(service.Tickets==tickets-11,"Single cost");for(int wait=0;wait<250&&!shop.flyingIcon.gameObject.activeSelf&&shop.Revealing;wait++)yield return .02;Check(shop.flyingIcon.gameObject.activeSelf,"Skip tested during flight");shop.skip.onClick.Invoke();Check(!shop.Revealing&&!shop.flyingIcon.gameObject.activeSelf&&shop.RevealedCount==1&&shop.results.gameObject.activeSelf,"Skip keeps committed reward and clears flight");shop.done.onClick.Invoke();
        shop.Prepare(1);shop.Purchase();shop.SendMessage("OnApplicationFocus",false);Check(!shop.Revealing&&shop.results.gameObject.activeSelf,"Focus loss settles presentation");shop.done.onClick.Invoke();
        shop.Prepare(1);shop.Purchase();shop.gameObject.SetActive(false);yield return .2;shop.gameObject.SetActive(true);yield return .2;
        Check(!shop.Revealing&&!shop.reveal.activeSelf&&service.Tickets==tickets-13,"Disable/reopen no replay or repayment");
        Resolution(1920,1080);yield return .3;
        foreach(int tier in new[]{3,2,1}){
            object[] args={1,"visual-tier-"+tier,null,(Func<int,int>)(max=>max==100?(tier==3?0:1):max==99?(tier==2?0:10):0)};
            Check((bool)typeof(TaskService).GetMethod("DrawLoot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(service,args),"Committed rarity fixture");
            typeof(LootShopUI).GetMethod("Show",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(shop,new[]{args[2],(object)true});
            for(int frame=0;frame<12;frame++){
                if(shop.Revealing){var sprite=shop.chest.sprite;Check(Vector2.Distance(shop.chest.rectTransform.sizeDelta,sprite.rect.size*shop.chestFrameScale)<.01f,"Stable original-canvas frame scale");}
                Capture("tier-"+tier+"-"+frame.ToString("00"));yield return .12;
            }
            yield return .5;shop.FinishReveal();Capture("tier-"+tier+"-result");yield return .2;shop.done.onClick.Invoke();
        }
        var settings=PlayerDataStore.Copy(service.Snapshot.settings);settings.reduceMotion=true;Check(service.SaveSettings(settings),"Reduce Motion setting");
        shop.Prepare(1);shop.Purchase();Check(!shop.Revealing&&shop.results.gameObject.activeSelf,"Reduced motion keeps reward without animation");shop.done.onClick.Invoke();
        settings.reduceMotion=false;Check(service.SaveSettings(settings),"Restore motion");
        Resolution(1280,720);yield return .3;
        manager.PlayerData.tickets=0;manager.SaveData();shop.gameObject.SetActive(false);shop.gameObject.SetActive(true);Check(!shop.single.interactable&&!shop.ten.interactable&&shop.status.text.Length>0,"Empty balance disabled state");Capture("no-tickets-1280");yield return .2;
        shop.Close();
        manager.PlayerData.equipment=EquipmentRules.Catalog.Concat(LootRules.Catalog).Select(d=>new EquipmentItem{id=d.Id}).ToList();manager.SaveData();page.Refresh();yield return .5;
        shop.gameObject.SetActive(true);shop.OpenInformation();yield return .3;
        Check(shop.catalog.All(c=>c.ownedIcon.gameObject.activeSelf),"Reopen reflects all owned equipment");Capture("all-owned-1280");yield return .2;shop.CloseInformation();shop.Close();
        foreach(int width in new[]{1920,1280,1024,2560}){
            Resolution(width,width==1920||width==2560?1080:width==1024?768:720);yield return .4;
            var toggle=(Toggle)typeof(SidebarNavigationButton).GetField("expandedToggle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav.buttons[4]);
            foreach(bool expanded in new[]{false,true}){
                if(toggle)toggle.isOn=expanded;yield return .5;
                Capture("inventory-27-"+width+(expanded?"-expanded":"-collapsed"));yield return .2;
            }
            if(toggle)toggle.isOn=false;
        }
        Resolution(1280,720);yield return .3;
        var target=LootRules.Catalog.First(d=>d.IconName=="Armor_GauntletsGold");Check(service.Equip(target.Id,EquipmentSlot.Weapon),"Gauntlets weapon equip");page.OpenItem(target.Id);yield return .4;Capture("gauntlets-detail-1280");yield return .2;
        var detail=(EquipmentItemDetailUI)typeof(EquipmentPageUI).GetField("itemDetail",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(page);
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .3;
            foreach(var icon in new[]{"Armor_MagicCrown","Armor_HeavyArmor","Armor_GauntletsGold","Armor_GauntletsSteel","Armor_BoneArmor"}){
                var definition=LootRules.Catalog.Single(d=>d.IconName==icon);page.OpenItem(definition.Id);yield return .2;
                Check(detail.itemBackground&&detail.itemBackground.color==LootShopUI.Tint(definition.Tier),"Detail icon background follows rarity");
                Check(detail.abilityText.gameObject.activeSelf==(EquipmentRules.Ability(definition)!=EquipmentAbility.None),"Only implemented legendary abilities shown");
                if(detail.abilityText.gameObject.activeSelf){detail.abilityText.ForceMeshUpdate();Check(!detail.abilityText.isTextOverflowing&&Inside(detail.abilityText.rectTransform,detail.card),"Ability text fits detail");}
                Capture("ability-"+icon+"-"+width);yield return .2;
            }
        }
        Check(service.EquipmentStats.SetPieces.All(x=>x==0),"Loot does not activate Boss set");
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: serialized bindings; four shop sizes; cancel, save failure/retry, single/ten/duplicate submission; opening-time flight, faster ten pull and skip; catalog information; focus/disable cleanup; no-ticket state; 27-item inventory and gauntlet equip. Visual captures require inspection.");
    }
}
