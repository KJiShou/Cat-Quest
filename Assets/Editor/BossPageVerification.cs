using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BossPageVerification
{
    const string Flag="CatQuest.BossPreviewCheck";
    const string Folder="Library/BossPreviewVerification";
    static double next;static int step;static RewardScreenshotCapture capture;static float animationTime;static int tickets;
    static BossPageVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=s=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(s==PlayModeStateChange.EnteredPlayMode){step=0;next=EditorApplication.timeSinceStartup+3;}
            if(s==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Flag,false);Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",null);}
        };
    }
    static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{width,height});
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/verify-boss-page.request")){
            File.Delete("Temp/verify-boss-page.request");Directory.CreateDirectory(Folder);
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));
            SessionState.SetBool(Flag,true);Resolution(1920,1080);EditorApplication.isPlaying=true;return;
        }
        if(!EditorApplication.isPlaying||!SessionState.GetBool(Flag,false)||EditorApplication.timeSinceStartup<next||(capture&&capture.Pending))return;
        EditorApplication.QueuePlayerLoopUpdate();
        try{
            var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
            if(!nav && step==0 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="CharacterHome"){
                UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterHome");next=EditorApplication.timeSinceStartup+3;return;
            }
            if(!nav)throw new Exception("CharacterHome must contain the shared Canvas.");
            if(step==0){tickets=GameDataManager.Instance.PlayerData.tickets;nav.ShowPage(MainMenuPage.Boss);}
            if(step==1){
                var stage=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c=>c.name=="Arena Camera").transform.parent;
                var anim=stage.Find("Cyclope").GetComponentInChildren<Animator>();animationTime=anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
                var camera=stage.GetComponentInChildren<Camera>();
                var target=camera.targetTexture;var previous=RenderTexture.active;
                var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try { RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes("Assets/Generated/Previews/ForestBossArena.png",texture.EncodeToPNG()); }
                finally { RenderTexture.active=previous;UnityEngine.Object.Destroy(texture); }
                Capture("boss-1920.png");
            }
            if(step==2){
                var stage=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c=>c.name=="Arena Camera").transform.parent;
                if(stage.Find("Cyclope").GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime<=animationTime)throw new Exception("Cyclope idle animation did not advance.");
                if(stage.Find("Ninja Cat").GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime<=0)throw new Exception("Cat idle animation did not advance.");
                Resolution(1280,720);
            }
            if(step==3)Capture("boss-1280.png");
            if(step==4){nav.ShowPage(MainMenuPage.Dashboard);if(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c=>c.name=="Arena Camera"&&c.enabled))throw new Exception("Arena camera remains active off-page.");nav.ShowPage(MainMenuPage.Boss);}
            if(step==5){
                if(GameDataManager.Instance.PlayerData.tickets!=tickets)throw new Exception("Preview changed ticket balance.");
                File.WriteAllText(Folder+"/result.txt","PASS: Boss page at 1920x1080 and 1280x720; Cyclope and cat idle animations advance; preview stops off-page and reopens; no ticket change.");
                Resolution(1920,1080);EditorApplication.isPlaying=false;return;
            }
            step++;next=EditorApplication.timeSinceStartup+1.5;
        }catch(Exception e){File.WriteAllText(Folder+"/result.txt",e.ToString());EditorApplication.isPlaying=false;}
    }
    static void Capture(string name){if(!capture)capture=new GameObject("Boss Preview Capture").AddComponent<RewardScreenshotCapture>();capture.Begin(Path.GetFullPath(Folder+"/"+name));}
}
