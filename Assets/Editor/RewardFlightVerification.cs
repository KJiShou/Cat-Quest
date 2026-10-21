using System;
using System.IO;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RewardFlightVerification
{
    const string Flag = "CatQuest.RewardFlightCheck", Folder = "Library/RewardFlightVerification";
    static IEnumerator flow;
    static double next, deadline;
    static string error;
    static RewardFlightVerification()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += (m, s, t) => { if (SessionState.GetBool(Flag, false) && (t == LogType.Exception || t == LogType.Error)) error = m; };
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { flow = Run(); next = EditorApplication.timeSinceStartup + 3; deadline = next + 100; error = null; }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Flag, false); flow = null;
                var oldEnv = SessionState.GetString(Flag+".env", ""); Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", oldEnv.Length == 0 ? null : oldEnv);
                UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag+".scene", ""));
                var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view, SessionState.GetInt(Flag+".size",0));
                Time.timeScale = SessionState.GetFloat(Flag+".scale", 1);
            }
        };
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists("Temp/verify-reward-flight.request"))
        {
            File.Delete("Temp/verify-reward-flight.request"); Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/result.txt", "RUNNING "+DateTime.UtcNow.ToString("O"));
            SessionState.SetString(Flag+".env", Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE")??"");
            SessionState.SetString(Flag+".scene", AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
            SessionState.SetFloat(Flag+".scale", Time.timeScale);
            var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            SessionState.SetInt(Flag+".size", (int)view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(view));
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(Folder+"/save-"+Guid.NewGuid().ToString("N")+".json"));
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            SessionState.SetBool(Flag,true); EditorApplication.isPlaying = true; return;
        }
        if (!EditorApplication.isPlaying || flow == null || EditorApplication.timeSinceStartup < next) return;
        try
        {
            Check(error == null, error); Check(EditorApplication.timeSinceStartup < deadline, "Timeout");
            EditorApplication.QueuePlayerLoopUpdate();
            if (flow.MoveNext()) next = EditorApplication.timeSinceStartup + (flow.Current is float seconds ? seconds : .05f);
            else { File.WriteAllText(Folder+"/result.txt", "PASS "+DateTime.UtcNow.ToString("O")+": committed quest/focus/boss rewards, duplicate and cancellation, delayed HUD arrival, unscaled motion, pooled burst, focus/disable reset, 1920/1280/1024/2560 captures, expanded/collapsed sidebar, capped rewards, target icon pulse/reset and accumulated gain numbers. Rewards remain authoritative and persisted."); flow=null; EditorApplication.isPlaying=false; }
        }
        catch(Exception e) { File.WriteAllText(Folder+"/result.txt", "FAIL "+e); flow=null; EditorApplication.isPlaying=false; }
    }
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Resolution(int w, int h) => typeof(DashboardRewardPlayVerification).GetMethod("Resolution", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null, new object[]{w,h});
    static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/"+name+".png"));
    static IEnumerator Run()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<RewardFlightUI>(); Check(ui, "Reward flight binding missing");
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var manager = GameDataManager.EnsureInstance(); var service = manager.Tasks;
        Check(service.Snapshot.tasks.Count==0, "Must use isolated save");
        Check(ui.fishTarget.sprite && ui.ticketTarget.sprite && ui.experienceIcon, "Missing source icons");
        foreach(int width in new[]{1920,1280,1024,2560})
        {
            Resolution(width,width==1024?768:width==1280?720:1080);
            var sidebar = UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
            var toggle = (UnityEngine.UI.Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
            toggle.isOn = width==1024 || width==2560; yield return 1f;
            Check(service.Upsert(new QuestTask{title="Reward flight check", difficulty=TaskDifficulty.Normal},null,out string id),service.LastError);
            int before=service.Snapshot.fishCoins;
            Check(service.Complete(id,true),service.LastError);
            Check(ui.ActiveCount>0,"Quest did not spawn reward icons");
            yield return .12f; Check(nav.fishCoins.text==before.ToString(),"HUD increased before arrival"); Capture("quest-scatter-"+width);
            yield return .4f; Capture("quest-flight-"+width);
            var fishMesh = ui.fishTarget.GetComponent<JuicyButtonMesh>();
            Vector3 originalScale = ui.fishTarget.rectTransform.localScale;
            bool pulsed = false;
            double arrivalDeadline = EditorApplication.timeSinceStartup + 3;
            while (ui.ActiveCount > 0 && EditorApplication.timeSinceStartup < arrivalDeadline)
            {
                pulsed |= fishMesh.RewardScale > 1.01f;
                yield return .02f;
            }
            Check(pulsed, "Fish target did not pulse on receipt");
            var gains = (TMPro.TMP_Text[])typeof(RewardFlightUI).GetField("gains",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            Check(gains[0].text == "+10", "Fish stack did not accumulate the full reward");
            Check(ui.fishTarget.rectTransform.localScale == originalScale, "Receipt feedback changed layout transform");
            Capture("quest-stacked-arrival-"+width);
            yield return .9f; Check(ui.ActiveCount==0 && nav.fishCoins.text==service.Snapshot.fishCoins.ToString(),"Reward flight failed to settle");
            Check(fishMesh.RewardScale == 1, "Fish target failed to return to original size");
            Check(service.Complete(id,true),service.LastError); Check(ui.ActiveCount==0,"Duplicate completion replayed reward");
        }
        Resolution(1280,720); yield return 1f;
        Check(service.BeginFocus(out string session),service.LastError);
        Check(service.FinishFocus(session,1500,false),service.LastError); Check(ui.ActiveCount>0,"Focus flight missing");
        Time.timeScale=0; yield return .2f; Capture("focus-ticket-scatter-1280"); yield return .4f; Capture("focus-ticket-flight-1280");
        bool ticketPulsed = false;
        var ticketMesh = ui.ticketTarget.GetComponent<JuicyButtonMesh>();
        double ticketDeadline = EditorApplication.timeSinceStartup + 3;
        while(ui.ActiveCount > 0 && EditorApplication.timeSinceStartup < ticketDeadline)
        { ticketPulsed |= ticketMesh.RewardScale > 1.01f; yield return .02f; }
        Check(ticketPulsed, "Ticket target did not pulse with paused gameplay");
        Capture("ticket-stacked-arrival-1280");
        yield return 1.2f; Check(ui.ActiveCount==0 && nav.tickets.text=="1" && ticketMesh.RewardScale==1,"Ticket arrival/unscaled animation failed"); Time.timeScale=1;
        Check(service.FinishFocus(session,1500,false),service.LastError); Check(ui.ActiveCount==0,"Duplicate Focus reward");
        Check(service.BeginFocus(out string cancel),service.LastError); Check(service.CancelFocus(cancel),service.LastError); Check(ui.ActiveCount==0,"Cancelled Focus animated a reward");
        manager.PlayerData.experience=2025; manager.PlayerData.tickets=5; manager.SaveData(); ui.enabled=false; ui.enabled=true;
        nav.ShowPage(MainMenuPage.Boss); yield return 1f;
        Check(service.BeginBattle(1,0,out var attempt),service.LastError);
        Check(service.ResolveBattle(attempt.id,BattleOutcome.Victory),service.LastError); Check(ui.ActiveCount>0,"Boss EXP flight missing");
        yield return .2f; Capture("boss-exp-scatter-1280"); yield return .4f; Capture("boss-exp-flight-1280"); yield return 1.2f;
        Check(service.ResolveBattle(attempt.id,BattleOutcome.Victory),service.LastError); Check(ui.ActiveCount==0,"Duplicate Boss reward");
        Check(service.BeginFocus(out string interrupted),service.LastError); Check(service.FinishFocus(interrupted,600,true),service.LastError);
        ui.SendMessage("OnApplicationFocus",false); Check(ui.ActiveCount==0 && nav.fishCoins.text==service.Snapshot.fishCoins.ToString(),"Focus loss did not settle presentation");
        Check(service.BeginFocus(out string disabled),service.LastError); Check(service.FinishFocus(disabled,600,true),service.LastError);
        ui.enabled=false; Check(ui.ActiveCount==0,"Disable leaked flights"); ui.enabled=true; Check(ui.ActiveCount==0,"Enable replayed rewards");
        for (int i=0;i<6;i++)
        {
            Check(service.Upsert(new QuestTask{title="Burst "+i,difficulty=TaskDifficulty.Hard},null,out string burst),service.LastError);
            Check(service.Complete(burst,true),service.LastError);
        }
        Check(ui.ActiveCount<=48,"Unbounded reward pool"); yield return 2f;
        Check(ui.ActiveCount==0 && nav.fishCoins.text==service.Snapshot.fishCoins.ToString(),"Burst did not settle");
        Check(service.Upsert(new QuestTask{title="Capped",difficulty=TaskDifficulty.Hard},null,out string capped),service.LastError);
        Check(service.Complete(capped,true),service.LastError); Check(ui.ActiveCount==0,"Capped rewards animated");
        // Reproduce Unity hot reload: OnDisable runs, scalar state survives,
        // but the non-serializable pool references are reconstructed as empty.
        ui.enabled=false;
        var pool=(Array)typeof(RewardFlightUI).GetField("flights",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
        Array.Clear(pool,0,pool.Length);
        ui.enabled=true; yield return .1f;
        Check(ui.transform.childCount==53, "Hot reload duplicated transient icons/labels");
        Check(service.ClaimDailyGoal(DailyRewardGoal.ThreeQuests),service.LastError);
        Check(ui.ActiveCount>0,"Hot reload did not restore subscription");
        for(int i=0;i<7;i++)
        {
            Check(service.BeginFocus(out string rapid),service.LastError);
            Check(service.FinishFocus(rapid,600,true),service.LastError);
        }
        Check(ui.ActiveCount==48,"Burst fixture did not exercise the bounded pool");
        yield return 1.6f;
        var stackedGains=(TMPro.TMP_Text[])typeof(RewardFlightUI).GetField("gains",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
        Check(stackedGains[0].text=="+38","Hot reload duplicated rewards or saturated pool lost stacked amounts");
        ui.SendMessage("OnApplicationFocus",false);
        Check(ui.fishTarget.GetComponent<JuicyButtonMesh>().RewardScale==1 && !stackedGains[0].gameObject.activeSelf,"Focus loss left receipt feedback behind");
        var disk=new PlayerDataStore(manager.SavePath); Check(disk.Data.fishCoins==service.Snapshot.fishCoins && disk.Data.tickets==service.Snapshot.tickets,"Presentation altered saved rewards");
        yield return .5f;
    }
}
