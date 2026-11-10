using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit, isolated verification only. Asset authoring is deliberately separate.
[InitializeOnLoad]
public static class RemainingFixesVerification
{
    const string Flag="CatQuest.RemainingFixes", Request="Temp/verify-remaining-fixes.request";
    static IEnumerator flow;
    static double next;
    static RewardScreenshotCapture capture;
    static string Folder=>SessionState.GetString(Flag+".folder","");
    static readonly BindingFlags Private=BindingFlags.Static|BindingFlags.NonPublic;
    static EditorWindow GameView=>EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty=>GameView.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    static object SizeGroup()=>typeof(TaskTabsVerification).GetMethod("GameViewGroup",Private).Invoke(null,null);
    static RemainingFixesVerification()
    {
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Flag,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){flow=Run();next=EditorApplication.timeSinceStartup+2;}
            if(state==PlayModeStateChange.EnteredEditMode)Restore();
        };
    }
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(SessionState.GetBool(Flag,false)) {
            if(DateTime.UtcNow.Ticks>long.Parse(SessionState.GetString(Flag+".deadline","0"))){Finish("FAIL: bounded verification timed out");return;}
            if(!EditorApplication.isPlaying||flow==null||EditorApplication.timeSinceStartup<next)return;
            try {
                if(!flow.MoveNext()){Finish("PASS");return;}
                next=EditorApplication.timeSinceStartup+Convert.ToDouble(flow.Current);
            }catch(Exception e){Finish("FAIL: "+e);}
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        if(Directory.GetFiles("Temp","verify-*.request").Any(p=>Path.GetFileName(p)!=Path.GetFileName(Request)))return;
        string mode=File.ReadAllText(Request).Trim();File.Delete(Request);
        if(!new[]{"before","after","performance-before","performance-after","presentation-after","runtime-after","equipment-after","guidance-after","bossresult-after","navigation-before","navigation-after"}.Contains(mode))throw new ArgumentException("Unknown verification group");
        string folder="Library/RemainingFixesVerification/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+mode;
        Directory.CreateDirectory(folder);SessionState.SetString(Flag+".folder",folder);
        File.WriteAllText("Library/RemainingFixesVerification/latest.txt",folder);
        SessionState.SetBool(Flag+".before",mode.EndsWith("before"));SessionState.SetString(Flag+".group",mode.Contains("-")?mode.Split('-')[0]:"all");
        foreach(string variable in new[]{"CATQUEST_TASK_TEST_STORE","CATQUEST_AI_TEST_DIRECTORY"}) {
            string prior=Environment.GetEnvironmentVariable(variable);
            SessionState.SetBool(Flag+variable+".exists",prior!=null);SessionState.SetString(Flag+variable,prior??"");
        }
        SessionState.SetString(Flag+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Flag+".paused",EditorApplication.isPaused);
        SessionState.SetInt(Flag+".size",(int)SizeProperty.GetValue(GameView));
        var group=SizeGroup();SessionState.SetInt(Flag+".custom",(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null));
        SessionState.SetString(Flag+".deadline",DateTime.UtcNow.AddMinutes(15).Ticks.ToString());SessionState.SetBool(Flag,true);
        File.WriteAllText(folder+"/result.txt",DateTime.UtcNow.ToString("O")+" RUNNING");
        File.WriteAllText(folder+"/checks.txt",DateTime.UtcNow.ToString("O")+" Unity "+Application.unityVersion+" "+mode+"\n");
        try {
            string save=Path.GetFullPath(folder+"/save.json");
            File.WriteAllText(save,"{\"version\":5,\"experience\":10000,\"tickets\":20,\"fishCoins\":500,\"tagsInitialized\":true,\"tutorial\":{\"version\":1,\"dismissed\":true,\"taskStep\":4}}");
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE",save);
            Environment.SetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY",Path.GetFullPath(folder+"/ai"));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }catch(Exception e){Finish("FAIL starting: "+e);}
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder+"/result.txt",DateTime.UtcNow.ToString("O")+" "+result);
        (flow as IDisposable)?.Dispose();flow=null;
        if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else Restore();
    }
    static void Restore()
    {
        (flow as IDisposable)?.Dispose();flow=null;capture=null;
        foreach(string variable in new[]{"CATQUEST_TASK_TEST_STORE","CATQUEST_AI_TEST_DIRECTORY"})
            Environment.SetEnvironmentVariable(variable,SessionState.GetBool(Flag+variable+".exists",false)?SessionState.GetString(Flag+variable,""):null);
        string scene=SessionState.GetString(Flag+".scene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        try {
            SizeProperty.SetValue(GameView,SessionState.GetInt(Flag+".size",0));
            var group=SizeGroup();int count=(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null);
            int builtIn=(int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group,null);
            for(int i=count-1;i>=SessionState.GetInt(Flag+".custom",count);i--)group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{builtIn+i});
        }finally {
            EditorApplication.isPaused=SessionState.GetBool(Flag+".paused",false);SessionState.SetBool(Flag,false);
            File.WriteAllText(Folder+"/restored.txt",DateTime.UtcNow.ToString("O")+" Restored save/AI environment, start scene, Game View and pause state; no authored scene saved.");
        }
    }
    public static void Check(bool condition,string message){if(!condition)throw new Exception(message);File.AppendAllText(Folder+"/checks.txt","PASS "+message+"\n");}
    public static void Resolution(int width,int height)=>typeof(DashboardRewardPlayVerification).GetMethod("Resolution",Private).Invoke(null,new object[]{width,height});
    public static string ResolutionStatus=>"Editor-context Screen="+Screen.width+"x"+Screen.height+", Game View size index="+SizeProperty.GetValue(GameView);
    public static IEnumerator Capture(string name)
    {
        if(!capture)capture=new GameObject("Remaining fixes capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(Folder+"/"+name+".png"));while(capture.Pending)yield return .05;
    }
    static IEnumerator Helper(string name,params object[] args)
        => HelperMethod(name,"Run",args);
    static IEnumerator HelperMethod(string name,string method,params object[] args)
    {
        var type=typeof(RemainingFixesVerification).Assembly.GetType(name);
        if(type==null)throw new InvalidOperationException("Missing required verifier "+name);
        var entry=type.GetMethod(method,BindingFlags.Static|BindingFlags.Public);
        if(entry==null)throw new InvalidOperationException("Missing required verifier entry "+name+"."+method);
        object result=entry.Invoke(null,args);
        // C# IEnumerable iterators also implement IEnumerator, but require GetEnumerator
        // to initialize their state. Casting first can silently skip every check.
        var iterator=(result as IEnumerable)?.GetEnumerator()??result as IEnumerator;
        if(iterator==null)throw new InvalidOperationException("Invalid verifier "+name);
        try {while(iterator.MoveNext())yield return iterator.Current;}
        finally {(iterator as IDisposable)?.Dispose();}
    }
    static IEnumerator Run()
    {
        bool before=SessionState.GetBool(Flag+".before",false);
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();var manager=GameDataManager.EnsureInstance();
        Check(nav&&manager.SavePath.Contains("RemainingFixesVerification"),"Main scene uses isolated verification save");
        UnityEngine.Object.FindFirstObjectByType<TutorialController>()?.Skip();yield return 3;
        string group=SessionState.GetString(Flag+".group","all");
        if(group=="navigation") {
            var navigation=HelperMethod("RemainingEquipmentVerificationChecks","RunNavigation",nav,manager,before,Folder,(Action<bool,string>)Check);
            try {while(navigation.MoveNext())yield return navigation.Current;}finally{(navigation as IDisposable)?.Dispose();}
        }
        if(group=="bossresult") {
            var boss=HelperMethod("RemainingGuidanceVerificationChecks","RunBoss",nav,manager,before,Folder,(Action<bool,string>)Check);
            try {while(boss.MoveNext())yield return boss.Current;}finally{(boss as IDisposable)?.Dispose();}
        }
        string[] names=group=="performance"||group=="navigation"||group=="bossresult"?Array.Empty<string>():group=="presentation"?new[]{"RemainingPresentationVerificationChecks"}:group=="runtime"?new[]{"RemainingPresentationRuntimeChecks"}:group=="equipment"?new[]{"RemainingEquipmentVerificationChecks"}:group=="guidance"?new[]{"RemainingGuidanceVerificationChecks"}:new[]{"RemainingPresentationVerificationChecks","RemainingPresentationRuntimeChecks","RemainingEquipmentVerificationChecks","RemainingGuidanceVerificationChecks"};
        foreach(string name in names) {
            var helper=Helper(name,nav,manager,before,Folder,(Action<bool,string>)Check);
            try {while(helper.MoveNext())yield return helper.Current;}finally{(helper as IDisposable)?.Dispose();}
        }
        if(group=="all"||group=="performance") {
            var performance=Helper("RemainingPerformanceVerificationChecks",before,Folder,(Action<bool,string>)Check);
            try {while(performance.MoveNext())yield return performance.Current;}finally{(performance as IDisposable)?.Dispose();}
        }
        Check(!nav.GetComponentInParent<Canvas>().rootCanvas.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0),"No missing scripts in application UI");
    }
}
