using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CharacterAuraVerification
{
    const string Flag="CatQuest.AuraCheck", Variable="CATQUEST_TASK_TEST_STORE";
    static IEnumerator flow; static double next; static RewardScreenshotCapture capture;
    static string Folder=>SessionState.GetString(Flag+"folder","");
    static EditorWindow View=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo Size=>View.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object Group()=>typeof(FocusLayoutVerification).GetMethod("SizeGroup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    static int Count(object group,string method)=>(int)group.GetType().GetMethod(method).Invoke(group,null);
    static CharacterAuraVerification(){
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Flag+"entered",true);flow=Run();next=EditorApplication.timeSinceStartup+3;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Flag+"entered",false)){
                Environment.SetEnvironmentVariable(Variable,SessionState.GetBool(Flag+"hadEnv",false)?SessionState.GetString(Flag+"env",""):null);
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+"scene",""));
                Size.SetValue(View,SessionState.GetInt(Flag+"size",0));
                var group=Group();int builtin=Count(group,"GetBuiltinCount");
                for(int i=Count(group,"GetCustomCount")-1;i>=SessionState.GetInt(Flag+"count",0);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtin+i});
                EditorApplication.isPaused=SessionState.GetBool(Flag+"paused",false);
                File.WriteAllText(Folder+"/done.txt","Restored isolated-save environment, start scene, Game View and pause.");
                SessionState.SetBool(Flag,false);SessionState.SetBool(Flag+"entered",false);flow=null;
            }
        };
    }
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)&&EditorApplication.isPlaying&&flow==null){SessionState.SetBool(Flag+"entered",true);flow=Run();next=EditorApplication.timeSinceStartup+3;}
        if(SessionState.GetBool(Flag,false)&&EditorApplication.isPlaying&&flow!=null){
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+"deadline","0"))){Finish("FAIL timeout");return;}
            if(EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
            try{if(flow.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(flow.Current);else Finish("PASS: selection, owned equipment changes, nine effects, mixed tiers/tie, empty gear, duplicate refresh, page reopen, unscaled particles, remote state/hide/reconnect, reload; 1920x1080 and 1280x720 captures. Native process not covered.");}
            catch(Exception e){Finish("FAIL "+e);Debug.LogException(e);}return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-character-aura.request"))return;
        File.Delete("Temp/verify-character-aura.request");
        string folder="Library/CharacterAuraVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(folder);
        SessionState.SetString(Flag+"folder",folder);string previous=Environment.GetEnvironmentVariable(Variable);
        SessionState.SetBool(Flag+"hadEnv",previous!=null);SessionState.SetString(Flag+"env",previous??"");
        SessionState.SetString(Flag+"scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag+"size",(int)Size.GetValue(View));SessionState.SetInt(Flag+"count",Count(Group(),"GetCustomCount"));
        SessionState.SetBool(Flag+"paused",EditorApplication.isPaused);SessionState.SetString(Flag+"deadline",DateTime.UtcNow.AddSeconds(180).Ticks.ToString());SessionState.SetBool(Flag,true);
        Environment.SetEnvironmentVariable(Variable,Path.GetFullPath(folder+"/save.json"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");EditorApplication.isPaused=false;
        Resolution(1920,1080);EditorApplication.isPlaying=true;
    }
    static void Finish(string result){File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("o")+" | Unity "+Application.unityVersion+"\n"+result);flow=null;EditorApplication.isPlaying=false;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Capture(string name){if(!capture)capture=new GameObject("Aura Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Equip(TaskService service,int tier,int pieces){
        foreach(EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))Check(service.Unequip(slot),service.LastError);
        for(int i=0;i<pieces;i++)Check(service.Equip(EquipmentRules.Catalog.First(d=>d.Tier==tier&&(int)d.Slot==i).Id,(EquipmentSlot)i),service.LastError);
    }
    static IEnumerator Run(){
        var manager=GameDataManager.EnsureInstance();Check(manager.SavePath.Contains("CharacterAuraVerification"),"Not isolated");
        var data=manager.PlayerData;data.experience=10000;
        data.tutorial=new TutorialProgress{version=1,dismissed=true,taskStep=4,announcedLevel=99};
        data.equipmentUnlocked=true;data.equipment=EquipmentRules.Catalog.Concat(LootRules.Catalog).Select(d=>new EquipmentItem{id=d.Id}).ToList();manager.SaveData();
        var service=manager.Tasks;var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();nav.ShowPage(MainMenuPage.Dashboard);yield return 1;
        var preview=nav.pages[0].GetComponentInChildren<CharacterHomePreview>();
        var aura=preview.character.GetComponentInChildren<CharacterEquipmentAura>();Check(aura,"Dashboard shared character missing");
        Check(aura.circlePrefabs.Length==9&&aura.circlePrefabs.All(p=>p),"Incomplete shared effect bindings");
        foreach(string path in new[]{"Assets/Prefabs/CatCharacter.prefab","Assets/Prefabs/CharacterEquipmentAura.prefab","Assets/Prefabs/Focus/FocusTrainingBattle.prefab","Assets/Prefabs/Boss/ForestBossStage.prefab","Assets/Prefabs/Boss/OldForestBossStage.prefab","Assets/Prefabs/Boss/LavaDungeonBossStage.prefab"}){
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(asset&&asset.GetComponentInChildren<CharacterEquipmentAura>(true),"Missing shared binding "+path);
            Check(asset.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Missing script "+path);
        }
        Capture("dashboard-before-1920");yield return .2;
        for(int tier=1;tier<=3;tier++)for(int pieces=1;pieces<=3;pieces++){
            Equip(service,tier,pieces);yield return .8;
            Check(aura.Tier==tier&&aura.Pieces==pieces&&aura.Circle,"Wrong aura");
            Check(aura.circlePrefabs[(tier-1)*3+pieces-1].name.StartsWith("Circle_Effect_"),"Old foot circle still bound");
            Check(!aura.Circle.transform.Find("Demo_Item_Icon_3").gameObject.activeSelf,"Vendor demo icon visible");
            var sorting=preview.character.GetComponent<UnityEngine.Rendering.SortingGroup>();
            Check(sorting&&sorting.enabled&&sorting.sortingOrder>preview.character.sortingOrder,"Aura/character not grouped above ground");
            Check(aura.Circle.GetComponentsInChildren<ParticleSystemRenderer>().All(r=>r.sortingOrder<preview.character.sortingOrder),"Aura draws in front of Ninja");
            Check(aura.Circle.GetComponentsInChildren<ParticleSystem>().All(p=>p.isPlaying&&p.main.useUnscaledTime),"Particles not running");
            Capture("dashboard-"+tier+"-"+pieces);yield return .2;
            var circle=aura.Circle;Check(service.Equip(EquipmentRules.Catalog.First(d=>d.Tier==tier&&d.Slot==EquipmentSlot.Weapon).Id,EquipmentSlot.Weapon),service.LastError);
            Check(aura.Circle==circle,"Duplicate equip restarted effect");
        }
        Equip(service,1,2);Check(service.Equip("boss3-head",EquipmentSlot.Head),service.LastError);Check(aura.Tier==1&&aura.Pieces==2,"Majority selection");
        Check(service.Equip("boss2-body",EquipmentSlot.Body),service.LastError);Check(aura.Tier==3&&aura.Pieces==1,"Tie selection");
        Equip(service,1,0);Check(!aura.Circle,"Empty loadout left effect");
        Check(!preview.character.GetComponent<UnityEngine.Rendering.SortingGroup>().enabled,"Empty loadout changed original sorting");
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);nav.ShowPage(MainMenuPage.CatHome);yield return .5;
            Equip(service,1,0);
            string[] legendary={"chest-armor_gauntletsgold","chest-armor_heavyarmor","chest-armor_helmgolden"};
            for(int i=0;i<3;i++){
                Check(service.Equip(legendary[i],(EquipmentSlot)i),service.LastError);yield return .8;
                Check(aura.Tier==CharacterEquipmentAura.LegendaryTier&&aura.Pieces==i+1&&aura.Circle.transform.childCount==i+1,"Legendary ring count");
                Check(service.EquipmentStats.SetPieces.All(n=>n==0),"Legendary presentation granted mastery set bonus");
                float previous=0;
                foreach(Transform ring in aura.Circle.transform){Check(ring.localScale.x>previous,"Rings must grow from small to large");previous=ring.localScale.x;}
                var same=aura.Circle;Check(service.Equip(legendary[i],(EquipmentSlot)i),service.LastError);Check(aura.Circle==same,"Duplicate Legendary equip restarted rings");
                Capture("legendary-"+(i+1)+"-"+width);yield return .2;
            }
            var loadedLegendary=new PlayerDataStore(manager.SavePath);
            CharacterEquipmentAura.SelectEquipment(loadedLegendary.Data.equippedWeaponId,loadedLegendary.Data.equippedBodyOutfitId,loadedLegendary.Data.equippedHeadAccessoryId,out int lt,out int lp);
            Check(lt==CharacterEquipmentAura.LegendaryTier&&lp==3,"Legendary reload selection");
            nav.ShowPage(MainMenuPage.Dashboard);yield return .3;nav.ShowPage(MainMenuPage.CatHome);yield return .3;
            Check(aura.Pieces==3&&aura.Circle.transform.childCount==3,"Legendary reopen lost rings");
            var preferences=service.Settings;preferences.reduceMotion=true;Check(service.SaveSettings(preferences),service.LastError);yield return .2;
            Check(!aura.Circle.activeSelf,"Reduced motion left Legendary rings active");
            preferences.reduceMotion=false;Check(service.SaveSettings(preferences),service.LastError);yield return .2;
            Check(aura.Circle.activeSelf,"Legendary rings did not resume");
            if(width==1920)for(int frame=0;frame<12;frame++){Capture("legendary-motion-"+frame.ToString("00"));yield return .12;}
            for(int i=2;i>=0;i--){Check(service.Unequip((EquipmentSlot)i),service.LastError);yield return .1;Check(i==0?!aura.Circle:aura.Pieces==i,"Unequip left stale Legendary circles");}
        }
        Equip(service,1,3);yield return .5;
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);yield return .5;
            foreach(var page in new[]{MainMenuPage.Dashboard,MainMenuPage.CatHome,MainMenuPage.Focus,MainMenuPage.Boss}){
                nav.ShowPage(page);yield return 1;
                CharacterEquipmentAura shown;
                if(page==MainMenuPage.Focus)shown=nav.pages[(int)page].GetComponentInChildren<FocusScenePreview>().Stage.GetComponentInChildren<CharacterEquipmentAura>();
                else if(page==MainMenuPage.Boss)shown=nav.pages[(int)page].GetComponentInChildren<BossArenaPreview>().Stage.GetComponentInChildren<CharacterEquipmentAura>();
                else shown=nav.pages[(int)page].GetComponentInChildren<CharacterHomePreview>().character.GetComponentInChildren<CharacterEquipmentAura>();
                Check(shown&&shown.Tier==1&&shown.Pieces==3&&shown.Circle,"Missing visible aura "+page);
                Capture(page+"-"+width);yield return .2;
                if(page==MainMenuPage.Boss&&width==1920){
                    for(int frame=0;frame<8;frame++){
                        int live=0;
                        foreach(var system in shown.Circle.GetComponentsInChildren<ParticleSystem>()){
                            var particles=new ParticleSystem.Particle[system.main.maxParticles];
                            int count=system.GetParticles(particles);live+=count;
                            for(int i=0;i<count;i++)Check(particles[i].velocity.sqrMagnitude<.000001f,"Idle Boss aura particle is drifting");
                        }
                        Check(live>0,"Stationary check had no live particles");
                        Capture("boss-stationary-"+frame.ToString("00"));yield return .2;
                    }
                }
            }
        }
        nav.ShowPage(MainMenuPage.Focus);yield return .5;
        var focus=nav.pages[(int)MainMenuPage.Focus].GetComponentInChildren<FocusScenePreview>();
        var remote=focus.Stage.GetComponentInChildren<CharacterEquipmentAura>();
        focus.Bind(new CompanionState{connected=true,equipmentAuraTier=CharacterEquipmentAura.LegendaryTier,equipmentAuraPieces=3});yield return .3;
        Check(remote.Tier==CharacterEquipmentAura.LegendaryTier&&remote.Circle.transform.childCount==3,"Remote Legendary rings missing");
        focus.Bind(new CompanionState{connected=false,equipmentAuraTier=CharacterEquipmentAura.LegendaryTier,equipmentAuraPieces=3});Check(!remote.Circle,"Disconnected Legendary rings stale");
        focus.Bind(new CompanionState{connected=true,equipmentAuraTier=2,equipmentAuraPieces=2});Check(remote.Tier==2&&remote.Pieces==2,"Remote not applied");
        focus.Bind(new CompanionState{connected=false,equipmentAuraTier=2,equipmentAuraPieces=2});Check(!remote.Circle,"Disconnected effect stale");
        focus.Bind(new CompanionState{connected=true,equipmentAuraTier=3,equipmentAuraPieces=3});Check(remote.Tier==3&&remote.Circle,"Reconnect");
        var loaded=new PlayerDataStore(manager.SavePath);CharacterEquipmentAura.Select(EquipmentRules.Calculate(loaded.Data).SetPieces,out int t,out int p);Check(t==1&&p==3,"Reload selection");
        nav.ShowPage(MainMenuPage.Dashboard);yield return .5;Check(aura.Tier==1&&aura.Circle,"Reopen lost aura");
        for(int i=0;i<10;i++){Capture("motion-"+i.ToString("00"));yield return .12;}
        // Render the actual vendor source next to the character for reference review.
        var reference=UnityEngine.Object.Instantiate(aura.circlePrefabs[2],preview.character.transform);
        reference.transform.localPosition=aura.characterOffset+Vector3.right*6+Vector3.up*aura.threePieceLift;
        var demoIcon=reference.transform.Find("Demo_Item_Icon_3");if(demoIcon)demoIcon.gameObject.SetActive(false);
        reference.transform.localScale=aura.circleScale;
        foreach(var renderer in reference.GetComponentsInChildren<ParticleSystemRenderer>()){renderer.sortingLayerID=preview.character.sortingLayerID;renderer.sortingOrder=preview.character.sortingOrder+1;}
        yield return .5;Capture("vendor-circle-reference");yield return .2;UnityEngine.Object.Destroy(reference);
    }
}
