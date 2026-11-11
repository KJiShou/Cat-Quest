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
public static class WeaponAttackVerification
{
    const string Flag = "CatQuest.WeaponAttackVerification";
    const string Folder = "Library/WeaponAttackVerification";
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
    static WeaponAttackVerification()
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
        if (!capture) capture = new GameObject("Weapon Attack Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(FolderNow + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if(DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Flag+".deadline",long.MaxValue.ToString()))) {
                File.WriteAllText(FolderNow+"/result.txt","FAIL: Weapon Attack Play Mode verification exceeded 240 seconds.");saveLock?.Dispose();saveLock=null;EditorApplication.isPlaying=false;return;
            }
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { if(run.MoveNext())next=EditorApplication.timeSinceStartup+Convert.ToDouble(run.Current);else EditorApplication.isPlaying=false; } catch (Exception e) { File.WriteAllText(FolderNow + "/result.txt", DateTime.UtcNow.ToString("O")+"\n"+e); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-weapon-attacks.request")) return;
        File.Delete("Temp/verify-weapon-attacks.request");
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
        var manager=GameDataManager.Instance;Check(manager.SavePath.Contains("WeaponAttackVerification"),"Isolated save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();
        manager.PlayerData.experience=9025;manager.PlayerData.tickets=30;manager.PlayerData.fishCoins=10000;manager.PlayerData.equipmentUnlocked=true;
        manager.PlayerData.equipment=EquipmentRules.Catalog.Concat(LootRules.Catalog).Select(d=>new EquipmentItem{id=d.Id}).ToList();manager.SaveData();
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var panel=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        foreach(int width in new[]{1920,1280}){
            Resolution(width,width==1920?1080:720);nav.ShowPage(MainMenuPage.Boss);panel.SelectStage(1);
            for(int i=0;i<100&&(!panel.arena.IsReady||!panel.actors.Ready);i++)yield return .1;
            Check(panel.actors.Ready,"Boss ready");
            foreach(string icon in new[]{"Weapon_MagicStaff_01","Armor_GauntletsGold","Armor_GauntletsSteel","Weapon_ShortSword"}){
                var item=LootRules.Catalog.Single(d=>d.IconName==icon);manager.Tasks.Equip(item.Id,EquipmentSlot.Weapon);
                var actors=panel.actors;actors.ResetPose();actors.StartRun();actors.Run(2);
                actors.StartCombat(new CombatStats{hp=100,atk=10,def=1,aspd=1},new CombatStats{hp=100,atk=10,def=1,aspd=1},item.Id);
                yield return .2;
                var clip=actors.Cat.GetComponentInChildren<Animator>().GetCurrentAnimatorClipInfo(0)[0].clip;
                Check(clip.name.Contains(WeaponAttackArt.Style(item.Id)==0?"slash":"pull lever"),"Weapon animation "+icon);
                actors.Hit(true);
                for(int frame=0;frame<8;frame++){yield return .08;Capture("battle-"+width+"-"+icon+"-"+frame);yield return .02;}
                actors.Finish(BattleOutcome.Victory);yield return .1;
                Check(!actors.Cat.parent.GetComponentsInChildren<ParticleSystem>().Any(p=>p.isPlaying&&(p.transform==actors.Cat.parent.Find("Weapon Attack Effect")||p.transform.IsChildOf(actors.Cat.parent.Find("Weapon Attack Effect")??actors.transform))),"Finish clears particles");
            }
            nav.ShowPage(MainMenuPage.Focus);yield return 1;
            var preview=nav.pages[(int)MainMenuPage.Focus].GetComponentInChildren<FocusScenePreview>(true);
            for(int i=0;i<100&&!preview.Stage;i++)yield return .1;
            var training=preview.Stage.GetComponentInChildren<FocusTrainingBattle>();Check(training.weaponAttackArt,"Training art binding");
            foreach(string icon in new[]{"Armor_GauntletsGold","Weapon_MagicStaff_01","Weapon_ShortSword"}){
                var item=LootRules.Catalog.Single(d=>d.IconName==icon);manager.Tasks.Equip(item.Id,EquipmentSlot.Weapon);
                training.Presentation=new Fixture();yield return .1;training.animator.Play("Training",0,0);training.animator.Update(0);
                Check(training.animator.GetCurrentAnimatorClipInfo(0)[0].clip.name==(WeaponAttackArt.Style(item.Id)==0?"FocusTrainingLoop":"FocusUnarmedTraining"),"Training weapon clip "+icon);
                for(int frame=0;frame<16;frame++){yield return .1;Capture("training-"+width+"-"+icon+"-"+frame);yield return .01;}
                training.SetTraining(false);training.Presentation=new Fixture{Phase=FocusPhase.Ready};yield return .1;
                Check(!training.GetComponentsInChildren<ParticleSystem>().Any(p=>p.isPlaying),"Training stop clears particles");
            }
            training.Presentation=new CompanionState{connected=true,phase=FocusPhase.Focusing,equippedWeaponId=LootRules.Catalog.Single(d=>d.IconName=="Armor_GauntletsSteel").Id};yield return .3;
            Check(training.animator.GetCurrentAnimatorClipInfo(0)[0].clip.name=="FocusUnarmedTraining","Remote weapon presentation");
            ((CompanionState)training.Presentation).connected=false;yield return .2;Check(!training.IsTraining,"Remote disconnect stops training");
            training.Presentation=null;nav.ShowPage(MainMenuPage.CatHome);yield return .1;
        }
        File.WriteAllText(FolderNow+"/result.txt",DateTime.UtcNow.ToString("O")+" PASS: gold/steel gauntlet, staff and sword animation selection; Boss effects and finish cleanup; training equipment switch, effects and pause cleanup at 1920/1280. Captured motion sequences for visual review.");
    }
    sealed class Fixture:IFocusPresentation {public FocusPhase Phase{get;set;}=FocusPhase.Focusing;public int SpectatorFishCount=>0;}
}
