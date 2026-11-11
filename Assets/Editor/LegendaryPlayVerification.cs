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
public static class LegendaryPlayVerification
{
    const string Flag = "CatQuest.LegendaryPlayVerification";
    const string Folder = "Library/LegendaryPlayVerification";
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
    static LegendaryPlayVerification()
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
        if (!capture) capture = new GameObject("Legendary Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Legendary Play Mode verification exceeded 240 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else EditorApplication.isPlaying=false; } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-legendary-play.request")) return;
        File.Delete("Temp/verify-legendary-play.request");
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
    static bool HasCriticalIcon(TMPro.TMP_Text text)=>text.transform.Find("CriticalIcon") is Transform icon&&icon.gameObject.activeInHierarchy;
    static EquipmentDefinition Item(string icon)=>LootRules.Catalog.Single(d=>d.IconName==icon);
    static System.Collections.IEnumerator Run(){
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("LegendaryPlayVerification"),"Isolated save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        manager.PlayerData.experience=9025;manager.PlayerData.tickets=30;manager.PlayerData.fishCoins=10000;manager.PlayerData.equipmentUnlocked=true;
        manager.PlayerData.equipment=EquipmentRules.Catalog.Concat(LootRules.Catalog).Select(d=>new EquipmentItem{id=d.Id}).ToList();
        manager.PlayerData.bossProgress[0].wins=1;manager.PlayerData.bossProgress[1].wins=1;manager.SaveData();
        var service=manager.Tasks;var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.CatHome);
        var page=nav.pages[4].GetComponent<EquipmentPageUI>();var shop=page.GetComponentInChildren<LootShopUI>(true);
        var detail=(EquipmentItemDetailUI)typeof(EquipmentPageUI).GetField("itemDetail",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(page);
        var inventory=page.GetComponentInChildren<EquipmentInventoryUI>(true);
        for(int i=0;i<60;i++)manager.PlayerData.tasks.Add(new QuestTask{id="wheel-"+i,title="Scroll task "+i});manager.SaveData();
        foreach(int width in new[]{1920,1280,1024,2560}){
            Resolution(width,width==1920||width==2560?1080:width==1024?768:720);yield return .4;
            var toggle=(Toggle)typeof(SidebarNavigationButton).GetField("expandedToggle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav.buttons[4]);
            foreach(bool expanded in new[]{false,true}){
                if(toggle)toggle.isOn=expanded;nav.ShowPage(MainMenuPage.CatHome);page.Refresh();yield return .5;
                var scroll=inventory.scroll;Check(scroll&&scroll.content==inventory.backpack,"Authored inventory scroll");
                scroll.StopMovement();scroll.verticalNormalizedPosition=1;EventSystem.current.SetSelectedGameObject(null);yield return .2;
                var size=inventory.grid.cellSize;Check(inventory.backpack.Cast<Transform>().Count(t=>t.gameObject.activeSelf)==30,"27 items fill six complete rows");
                float before=scroll.content.anchoredPosition.y;float expectedStep=Mathf.Min(48,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height-before));
                scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-6)});yield return .15;
                Check(Mathf.Abs(scroll.content.anchoredPosition.y-before-expectedStep)<1,"Inventory wheel bounded to 48 UI pixels or remaining content: "+(scroll.content.anchoredPosition.y-before)+" expected "+expectedStep);Check(inventory.grid.cellSize==size,"Scrolling preserves square size");
                Capture("inventory-wheel-"+width+"-"+expanded);yield return .15;
                scroll.verticalNormalizedPosition=0;yield return .2;Capture("inventory-bottom-"+width+"-"+expanded);yield return .15;
                for(int tick=0;tick<5;tick++)scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-6)});yield return .2;
                Check(scroll.verticalNormalizedPosition>=-.001f,"Inventory bottom clamps");
                scroll.verticalNormalizedPosition=1;EventSystem.current.SetSelectedGameObject(null);yield return .15;
                var last=inventory.backpack.GetComponentsInChildren<EquipmentInventoryItemUI>().Last();EventSystem.current.SetSelectedGameObject(last.button.gameObject);yield return .25;
                Capture("keyboard-reveal-"+width+"-"+expanded);yield return .15;
                var corners=new Vector3[4];((RectTransform)last.transform).GetWorldCorners(corners);var area=scroll.viewport.rect;area.xMin-=.5f;area.xMax+=.5f;area.yMin-=.5f;area.yMax+=.5f;
                Check(corners.All(c=>area.Contains(scroll.viewport.InverseTransformPoint(c))),"Keyboard selection reveals offscreen equipment: offset "+scroll.content.anchoredPosition.y+" max "+(scroll.content.rect.height-scroll.viewport.rect.height));EventSystem.current.SetSelectedGameObject(null);
                inventory.filters[1].onClick.Invoke();yield return .25;Check(inventory.grid.cellSize==size&&scroll.content.rect.height<=scroll.viewport.rect.height+.1f,"Filter removes overflow without shrinking cells");inventory.filters[0].onClick.Invoke();yield return .25;
                nav.ShowPage(MainMenuPage.Tasks);yield return .4;var tasks=nav.pages[(int)MainMenuPage.Tasks].GetComponent<TaskSystemUI>();tasks.SelectTab(TaskTab.Todo);yield return .3;var list=tasks.listContent.GetComponentInParent<ScrollRect>();
                list.StopMovement();list.verticalNormalizedPosition=1;yield return .2;before=list.content.anchoredPosition.y;list.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-6)});yield return .2;
                Check(Mathf.Abs(list.content.anchoredPosition.y-before-48)<1,"Task wheel moves 48 UI pixels per tick");Capture("tasks-wheel-"+width+"-"+expanded);yield return .15;
            }
            if(toggle)toggle.isOn=false;
        }
        nav.ShowPage(MainMenuPage.CatHome);yield return .2;
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .4;
            foreach(var def in LootRules.Catalog.Where(d=>d.Tier==3)){
                page.OpenItem(def.Id);yield return .2;
                Check(detail.abilityIcon.gameObject.activeInHierarchy&&detail.abilityIcon.sprite&&Inside(detail.abilityIcon.rectTransform,detail.card),"Visible ability icon "+def.Id);
                detail.abilityText.ForceMeshUpdate();Check(!detail.abilityText.isTextOverflowing&&Inside(detail.abilityText.rectTransform,detail.card),"Ability fits "+def.Id);
                Capture("ability-"+def.IconName+"-"+width);yield return .15;detail.close.onClick.Invoke();
            }
            shop.gameObject.SetActive(true);shop.OpenInformation();yield return .2;
            // Synthetic hover captures must not race the user's real desktop pointer.
            var module=EventSystem.current.currentInputModule;bool moduleEnabled=module&&module.enabled;if(module)module.enabled=false;
            try{
            foreach(var cell in shop.catalog.Take(6)){
                cell.OnPointerEnter(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,cell.transform.position)});yield return .2;
                Check(shop.hoverDetail.abilityIcon.gameObject.activeInHierarchy&&shop.hoverDetail.abilityIcon.sprite&&Inside(shop.hoverDetail.abilityIcon.rectTransform,shop.hoverDetail.card),"Hover ability icon");
                shop.hoverDetail.abilityText.ForceMeshUpdate();Check(!shop.hoverDetail.abilityText.isTextOverflowing&&Inside(shop.hoverDetail.abilityText.rectTransform,shop.hoverDetail.card),"Hover ability fits");
                Capture("hover-"+cell.icon.sprite.name+"-"+width);yield return .15;cell.OnPointerExit(new PointerEventData(EventSystem.current));
            }
            }finally{if(module)module.enabled=moduleEnabled;}
            shop.CloseInformation();shop.Close();
        }
        Check(service.AutoEquip(),"Ability-aware Auto Select");var selected=service.EquipmentSnapshot;var target=BattleRules.Boss(3);double best=EquipmentRules.AutoEquipScore(selected,target);
        foreach(var def in LootRules.Catalog){var old=EquipmentRules.Equipped(selected,def.Slot);EquipmentRules.SetEquipped(selected,def.Slot,def.Id);Check(EquipmentRules.AutoEquipScore(selected,target)<=best+1e-9,"Auto Select considers alternative "+def.Id);EquipmentRules.SetEquipped(selected,def.Slot,old);}
        var gear=new[]{Item("Armor_GauntletsGold"),Item("Armor_HeavyArmor"),Item("Armor_MagicCrown")};
        foreach(var def in gear)Check(service.Equip(def.Id,def.Slot),"Equip combat fixture");
        bool fixture=false;
        for(int level=20;level<=40&&!fixture;level++){
            manager.PlayerData.experience=25*(level-1)*(level-1);manager.SaveData();var preview=new BattleAttempt{equipmentAbilityVersion=2,cat=service.CatStats,boss=BattleRules.Boss(3),equippedEquipment=gear.Select(d=>new EquipmentItem{id=d.Id}).ToList()};
            bool crit=false,block=false;new BattleSimulation(preview).Advance(120,h=>{crit|=h.critical;block|=h.blocked;});fixture=crit&&block;
        }
        Check(fixture,"Valid progression fixture contains critical and block");
        var panel=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);nav.ShowPage(MainMenuPage.Boss);panel.SelectStage(3);
            for(int i=0;i<100&&(!panel.arena.IsReady||!panel.actors.Ready);i++)yield return .1;
            Check(panel.arena.IsReady&&panel.actors.Ready,"Boss loaded");yield return .5;panel.Action();panel.Confirm();Check(panel.IsBattling,"Battle entry");
            var saved=service.Snapshot.battles.Last();var expected=new BattleSimulation(saved);expected.Advance(120);
            int critFrames=0,blockFrames=0,frame=0;var positions=new System.Collections.Generic.Dictionary<int,Vector2>();bool moved=false,faded=false;
            var trace=new System.Text.StringBuilder("time,catHP,bossHP,text,x,y,alpha\n");
            while(panel.IsBattling&&frame<1000){
                var texts=panel.GetComponentsInChildren<TMPro.TMP_Text>().Where(t=>t.name.StartsWith("DamageNumber")&&t.gameObject.activeInHierarchy).ToArray();
                bool crit=texts.Any(t=>HasCriticalIcon(t)),block=texts.Any(t=>t.text=="BLOCK");
                foreach(var t in texts){
                    t.ForceMeshUpdate();Check(!t.isTextOverflowing,"Damage label clipped: "+t.text);
                    if(t.alpha>.3f&&(HasCriticalIcon(t)||t.text=="BLOCK")){
                        var bar=new Vector3[4];panel.catHealth.GetWorldCorners(bar);var bounds=t.textBounds;
                        var min=t.transform.TransformPoint(bounds.min);var max=t.transform.TransformPoint(bounds.max);
                        Check(!Rect.MinMaxRect(min.x,min.y,max.x,max.y).Overlaps(Rect.MinMaxRect(bar[0].x,bar[0].y,bar[2].x,bar[2].y)),"Ability label overlaps actor health bar");
                        if(t.text=="BLOCK")Check(t.color.g>t.color.r,"Block has distinct mint feedback");
                        else Check(t.color.b>t.color.r&&t.color.r>t.color.g&&t.transform.Find("CriticalIcon").GetComponent<Image>().sprite,"Critical uses icon and purple damage");
                    }
                    var p=t.rectTransform.anchoredPosition;int id=t.GetInstanceID();if(positions.TryGetValue(id,out var old)&&Vector2.Distance(p,old)>.1f)moved=true;positions[id]=p;faded|=t.alpha<.8f;
                    trace.AppendLine($"{panel.Simulation.Elapsed:F3},{panel.Simulation.CatHp},{panel.Simulation.BossHp},{t.text},{p.x:F2},{p.y:F2},{t.alpha:F3}");
                }
                if((crit&&critFrames<16)||(block&&blockFrames<16)){Capture("combat-"+width+"-"+frame.ToString("0000"));if(crit)critFrames++;if(block)blockFrames++;}
                frame++;yield return .07;
            }
            File.WriteAllText(FolderNow+"/motion-"+width+".csv",trace.ToString());
            Check(!panel.IsBattling&&panel.Simulation.Outcome==expected.Outcome,"Playback matches authoritative simulation");
            Check(critFrames>2&&blockFrames>2&&moved&&faded,"Visible crit/block with movement and fade");
            yield return 1.5;Check(panel.GetComponent<BattleDamageNumbers>().ActiveCount==0,"Feedback expires");
            nav.ShowPage(MainMenuPage.CatHome);yield return .2;Check(panel.GetComponent<BattleDamageNumbers>().ActiveCount==0,"Page closure clears feedback");
        }
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: fixed square inventory/full rows and bounded Task/Cat Home wheel in four sizes and both sidebar states; enlarged ability icons/detail/hover at 1920 and 1280; Auto Select; live Boss CRIT/BLOCK, movement, fade, expiry, page-close cleanup and authoritative outcome. Inspect current captures for visual review.");
    }
}
