using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BossStageVerification
{
    const string Flag="CatQuest.BossStages", Folder="Library/BossStageVerification";
    static int step; static double next; static RewardScreenshotCapture capture; static string error;
    static BossStageVerification()
    {
        EditorApplication.update+=Poll;
        Application.logMessageReceived+=(m,t,k)=>{if(SessionState.GetBool(Flag,false)&&(k==LogType.Exception||k==LogType.Error))error=m;};
        EditorApplication.playModeStateChanged+=s=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(s==PlayModeStateChange.EnteredPlayMode){step=0;error=null;next=EditorApplication.timeSinceStartup+3;}
            if(s==PlayModeStateChange.EnteredEditMode){
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+"scene",""));
                var prior=SessionState.GetString(Flag+"env","");Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",prior.Length==0?null:prior);
                var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,SessionState.GetInt(Flag+"size",0));
                SessionState.SetBool(Flag,false);
            }
        };
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Resolution(int w,int h)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w,h});
    static void Capture(string name){if(!capture)capture=new GameObject("Stage Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-boss-stages.request")){
            File.Delete("Temp/verify-boss-stages.request");Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/play-result.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
            SessionState.SetString(Flag+"env",Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"");
            SessionState.SetString(Flag+"scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));SessionState.SetInt(Flag+"size",(int)view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(view));
            SessionState.SetFloat(Flag+"start",(float)EditorApplication.timeSinceStartup);
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");SessionState.SetBool(Flag,true);Resolution(1920,1080);EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool(Flag,false))return;
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+"start",0)>120){File.WriteAllText(Folder+"/play-result.txt","FAIL timeout");EditorApplication.isPlaying=false;return;}
        if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();
        try {
            Check(error==null,error);
            var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();Check(nav,"Missing navigation");
            var panel=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
            if(step==0){Check(GameDataManager.Instance.SavePath.Contains("BossStageVerification"),"Non-isolated save");nav.ShowPage(MainMenuPage.Boss);}
            if(step>=1&&step<=12){
                int id=1+(step-1)%3;
                if(step==7)Resolution(1280,720);
                if(panel.SelectedBoss!=id){panel.SelectStage(id);next=EditorApplication.timeSinceStartup+.1;return;}
                if(panel.arena.IsLoading){next=EditorApplication.timeSinceStartup+.1;return;}
                Check(panel.arena.IsReady&&panel.actors.Ready,"Actors not ready for "+id);
                Check(panel.actors.Boss.name==(id==1?"Cyclope":id==2?"Orc":"Demon"),"Wrong actor");
                Check(panel.arena.Stage.name.StartsWith(id==1?panel.arena.stagePrefab.name:id==2?"OldForestBossStage":"LavaDungeonBossStage"),"Wrong environment");
                if(step<=3||step>=7&&step<=9)Capture("stage-"+id+"-"+(step<=3?1920:1280));
                panel.actors.StartRun();Check(panel.actors.Run(1.2f),"Run did not complete");
                panel.actors.StartCombat(BattleRules.Boss(1,0),BattleRules.Boss(id,0));panel.actors.Hit(true);panel.actors.Hit(false);
            }
            if(step==13){panel.actors.ResetPose();panel.SelectStage(2,2);Check(panel.arena.Stage.name.StartsWith("OldForest"),"Master environment");nav.ShowPage(MainMenuPage.Dashboard);}
            if(step==14){Check(!panel.arena.Stage.gameObject.activeSelf&&!panel.arena.StageCamera.enabled,"Off-page stage active");nav.ShowPage(MainMenuPage.Boss);}
            if(step==15){Check(panel.actors.Ready&&panel.arena.StageCamera.enabled,"Reopen failed");panel.SelectStage(3);Capture("stage-3-reopened");}
            if(step==16){Check(GameDataManager.Instance.PlayerData.battles.Count==0,"Preview settled a battle");File.WriteAllText(Folder+"/play-result.txt","PASS "+DateTime.UtcNow.ToString("O")+": all environments/actors, repeated 1/2/3 switching, run and impact animation smoke, Master mapping, page close/reopen, isolated save; 1920x1080 and 1280x720 captures.");EditorApplication.isPlaying=false;return;}
            step++;next=EditorApplication.timeSinceStartup+.8;
        }catch(Exception e){File.WriteAllText(Folder+"/play-result.txt","FAIL "+DateTime.UtcNow.ToString("O")+" "+e);EditorApplication.isPlaying=false;}
    }
}

