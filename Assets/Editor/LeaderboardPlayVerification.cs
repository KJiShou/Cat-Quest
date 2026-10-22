using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Observes saved authored assets. This verifier never generates or repairs UI.
[InitializeOnLoad]
public static class LeaderboardPlayVerification
{
    const string Flag = "CatQuest.LeaderboardPlayVerification";
    const string Request = "Temp/verify-leaderboard-play.request";
    const string StoreVariable = "CATQUEST_TASK_TEST_STORE";
    const string Output = "Library/LeaderboardVerification";
    const string PrefabPath = "Assets/Prefabs/Leaderboard/LeaderboardContent.prefab";
    const string Vendor = "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/";
    const string ReferencePath = Vendor + "Prefabs/Prefabs_DemoScene_Panels/Light/Ranking.prefab";
    static IEnumerator run;
    static double next;
    static string context, loggedError;
    static RewardScreenshotCapture capture;
    static readonly List<string> evidence = new List<string>();
    static readonly List<string> checks = new List<string>();
    static string Folder => SessionState.GetString(Flag + ".folder", Output);

    static LeaderboardPlayVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += StateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        EditorApplication.delayCall += RecoverAfterReload;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Flag, false) && (type == LogType.Error || type == LogType.Exception))
                loggedError = message + "\n" + stack;
        };
    }

    static void BeforeReload()
    {
        if (SessionState.GetBool(Flag, false) && SessionState.GetBool(Flag + ".enteredPlay", false)
            && !SessionState.GetBool(Flag + ".finished", false))
            Finish("FAIL: script compilation/domain reload interrupted the running verification at " + context + ". Run again after code changes finish.");
    }

    static void RecoverAfterReload()
    {
        if (!SessionState.GetBool(Flag, false) || !SessionState.GetBool(Flag + ".enteredPlay", false) || run != null) return;
        if (!SessionState.GetBool(Flag + ".finished", false))
            Finish("FAIL: a domain reload discarded the running verification. Run again after code changes finish.");
        else if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Restore();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Flag, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Flag + ".enteredPlay", true);
            evidence.Clear(); checks.Clear(); loggedError = null;
            run = Run(); next = EditorApplication.timeSinceStartup + 2;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (!SessionState.GetBool(Flag + ".finished", false)) WriteResult("FAIL: Play Mode ended before verification completed.");
            Restore();
        }
    }

    static void Poll()
    {
        if (SessionState.GetBool(Flag, false))
        {
            // Entry can be requested during the Editor's transition update; recover
            // an already-entered Play Mode if its state notification was missed.
            if (EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating
                && run == null && !SessionState.GetBool(Flag + ".enteredPlay", false)
                && !SessionState.GetBool(Flag + ".finished", false))
                StateChanged(PlayModeStateChange.EnteredPlayMode);
            if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Flag + ".deadline", ""), null, DateTimeStyles.RoundtripKind))
            { Finish("FAIL: bounded run timed out at " + context); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !EditorApplication.isPlaying || run == null) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try
            {
                Check(loggedError == null, "Unexpected Unity error: " + loggedError);
                if (run.MoveNext())
                {
                    next = EditorApplication.timeSinceStartup + Convert.ToDouble(run.Current, CultureInfo.InvariantCulture);
                    if (SessionState.GetString(Flag + ".context", "") != context)
                    {
                        SessionState.SetString(Flag + ".context", context ?? "");
                        File.WriteAllText(Folder + "/progress.txt", DateTime.UtcNow.ToString("O") + "\n" + context);
                    }
                }
                else
                {
                    Check(evidence.All(path => File.Exists(path) && new FileInfo(path).Length > 100), "A requested screenshot is missing or empty.");
                    Finish("PASS: four categories, medal-number centering, panel/button/own-rank containment, removed copy, empty/error/auth states, navigation, cancellation and feedback at four resolutions; expanded/collapsed sidebar layout captures. Review current screenshots separately.");
                }
            }
            catch (Exception e) { Finish("FAIL at " + context + ": " + e); }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        evidence.Clear(); checks.Clear(); context = "static authored bindings"; loggedError = null;
        string folder = Output + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        SessionState.SetString(Flag + ".folder", folder);
        try
        {
            ValidateBindings();
            string previous = Environment.GetEnvironmentVariable(StoreVariable);
            SessionState.SetBool(Flag + ".hadStore", previous != null);
            SessionState.SetString(Flag + ".store", previous ?? "");
            SessionState.SetString(Flag + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Flag + ".sceneSetup", SceneSignature());
            SessionState.SetInt(Flag + ".size", (int)SizeProperty.GetValue(GameView));
            var group = GameViewGroup();
            SessionState.SetInt(Flag + ".customCount", Count(group, "GetCustomCount"));
            SessionState.SetFloat(Flag + ".timeScale", Time.timeScale);
            SessionState.SetBool(Flag + ".paused", EditorApplication.isPaused);
            SessionState.SetString(Flag + ".deadline", DateTime.UtcNow.AddSeconds(240).ToString("O"));
            SessionState.SetBool(Flag + ".finished", false);
            SessionState.SetBool(Flag + ".enteredPlay", false);
            SessionState.SetBool(Flag, true);
            WriteResult("RUNNING");
            Environment.SetEnvironmentVariable(StoreVariable, Path.GetFullPath(folder + "/save.json"));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
            Check(EditorSceneManager.playModeStartScene, "CharacterHome scene missing.");
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }
        catch (Exception e)
        {
            if (SessionState.GetBool(Flag, false)) Finish("FAIL starting verification: " + e);
            else WriteResult("FAIL static bindings: " + e);
        }
    }

    static void ValidateBindings()
    {
        var canvas=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Canvas.prefab");
        Check(canvas.GetComponentInChildren<MainMenuNavigation>(true).pages[5], "Leaderboard navigation missing");
        var panel=canvas.GetComponentInChildren<LeaderboardPanelUI>(true);
        if (!panel) return; // Baseline capture before targeted authoring.
        Check(panel.categories.Length==4 && panel.template && panel.ownRow && panel.scroll && panel.refresh && panel.accountButton,"Leaderboard bindings missing");
        foreach (var row in new[] { panel.template, panel.ownRow })
            Check(row.metricIcon && row.metricIcons.Length == 4 && row.metricIcons.All(icon => icon), "Ranking metric icon bindings missing");
        foreach(var t in panel.GetComponentsInChildren<Transform>(true))Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing script: "+t.name);
    }
    static EditorWindow GameView => EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
    static PropertyInfo SizeProperty => GameView.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    static int Count(object group, string method) => (int)group.GetType().GetMethod(method).Invoke(group, null);
    static object GameViewGroup()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(type);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var method = type.GetMethod("GetGroup");
        return method.Invoke(sizes, new[] { Enum.ToObject(method.GetParameters()[0].ParameterType, 0) });
    }
    static void Resolution(Vector2Int size)
    {
        var assembly = typeof(Editor).Assembly;
        var fixedType = assembly.GetType("UnityEditor.GameViewSizeType");
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var constructor = sizeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { fixedType, typeof(int), typeof(int), typeof(string) }, null);
        var custom = constructor.Invoke(new[] { Enum.ToObject(fixedType, 1), (object)size.x, size.y, "Cat Quest Leaderboard Verification" });
        var group = GameViewGroup(); group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { custom });
        SizeProperty.SetValue(GameView, Count(group, "GetBuiltinCount") + Count(group, "GetCustomCount") - 1);
        GameView.Show(); GameView.Focus();
    }
    static string SceneSignature() => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var scene = SceneManager.GetSceneAt(i);
        return scene.path + "|" + scene.isLoaded + "|" + (scene == SceneManager.GetActiveScene()) + "|" + scene.isDirty;
    }));
    static void Restore()
    {
        try { (run as IDisposable)?.Dispose(); } catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nCleanup error: " + e); }
        run = null; capture = null;
        try
        {
            Environment.SetEnvironmentVariable(StoreVariable, SessionState.GetBool(Flag + ".hadStore", false) ? SessionState.GetString(Flag + ".store", "") : null);
            string previousScene = SessionState.GetString(Flag + ".scene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScene);
            SizeProperty.SetValue(GameView, SessionState.GetInt(Flag + ".size", 0));
            var group = GameViewGroup(); int builtIn = Count(group, "GetBuiltinCount");
            for (int i = Count(group, "GetCustomCount") - 1; i >= SessionState.GetInt(Flag + ".customCount", 0); i--)
                group.GetType().GetMethod("RemoveCustomSize").Invoke(group, new object[] { builtIn + i });
            Time.timeScale = SessionState.GetFloat(Flag + ".timeScale", 1);
            EditorApplication.isPaused = SessionState.GetBool(Flag + ".paused", false);
            Check(SceneSignature() == SessionState.GetString(Flag + ".sceneSetup", ""), "Prior Editor scene setup/dirty state changed.");
            File.AppendAllText(Folder + "/result.txt", "\nRESTORED: prior save environment, Play Mode start scene, Game View selection/custom sizes, time scale, pause and Editor scene setup.\n");
        }
        catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nFAIL restoring environment: " + e); }
        finally { SessionState.SetBool(Flag + ".enteredPlay", false); SessionState.SetBool(Flag, false); }
    }
    static void Finish(string result)
    {
        SessionState.SetBool(Flag + ".finished", true);
        WriteResult(result);
        try { (run as IDisposable)?.Dispose(); } catch (Exception e) { File.AppendAllText(Folder + "/result.txt", "\nFAIL disposing run: " + e); }
        run = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Restore();
    }
    static void WriteResult(string result)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/result.txt", DateTime.UtcNow.ToString("O") + "\n" + result + "\nContext: " + context
            + "\nReference prefab: " + ReferencePath + "\nReference preview: " + Vendor + "Preview/055_Ranking.png\n"
            + string.Join("\n", checks) + "\nScreenshots:\n" + string.Join("\n", evidence.Count > 0 ? evidence : Directory.GetFiles(Folder, "*.png").OrderBy(path => path).ToList()));
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/latest-run.txt", Path.GetFullPath(Folder));
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Rect Bounds(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
    }
    static GameObject Hit(Vector2 position)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
        Check(hits.Count > 0, "No UI raycast at " + position);
        return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
    }
    static PointerEventData Pointer(Component component) => new PointerEventData(EventSystem.current)
    { button = PointerEventData.InputButton.Left, position = Bounds((RectTransform)component.transform).center };
    static void Click(Selectable control)
    {
        Check(control && control.IsInteractable() && control.gameObject.activeInHierarchy, "Control is unavailable: " + (control ? control.name : "null"));
        var pointer = Pointer(control);
        if (control.targetGraphic) pointer.position = Bounds(control.targetGraphic.rectTransform).center;
        var target = Hit(pointer.position);
        Check(target && (target == control.gameObject || target.transform.IsChildOf(control.transform)), "Control center is blocked: " + control.name);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
    }
    static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        if (!capture) capture = new GameObject("Settings Verification Capture").AddComponent<RewardScreenshotCapture>();
        string path = Path.GetFullPath(Folder + "/" + name + ".png"); evidence.Add(path); capture.Begin(path);
    }
    static void Stable(Rect before, Component control)
    {
        var after = Bounds((RectTransform)control.transform);
        Check(Vector2.Distance(before.position, after.position) < 1 && Vector2.Distance(before.size, after.size) < 1, "Feedback moved the control's hit area: " + control.name);
    }
    static void Reset(JuicyButton feedback, string reason) => Check(Vector2.Distance(feedback.DrawScale, Vector2.one) < .005f && Mathf.Abs(feedback.DrawOffset) < .1f, reason);

    static void Contained(RectTransform parent,RectTransform child,string message)
    {
        var outer=Bounds(parent);var inner=Bounds(child);
        Check(inner.xMin>=outer.xMin-1&&inner.xMax<=outer.xMax+1&&inner.yMin>=outer.yMin-1&&inner.yMax<=outer.yMax+1,message+": "+inner+" within "+outer);
    }
    static void Layout(LeaderboardPanelUI panel)
    {
        Canvas.ForceUpdateCanvases();
        var root=(RectTransform)panel.transform;
        Contained((RectTransform)root.parent,root,"Ranking content leaves page");
        foreach(var button in panel.categories.Concat(new[]{panel.refresh,panel.accountButton}))
            Contained(root,(RectTransform)button.transform,"Ranking button leaves panel: "+button.name);
        var own=(RectTransform)panel.ownRow.transform.parent;
        Contained((RectTransform)own.parent,own,"Your Rank frame leaves table");
        Contained(own,(RectTransform)own.Find("Title"),"Your Rank title leaves frame");
        if(panel.ownRow.gameObject.activeSelf)Contained(own,(RectTransform)panel.ownRow.transform,"Player rank row leaves Your Rank frame");
        else Contained(own,panel.ownMessage.rectTransform,"Own-rank message leaves frame");
        Check(!panel.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.StartsWith("Top ",StringComparison.Ordinal)||t.text.StartsWith("Four rankings.",StringComparison.Ordinal)),"Removed ranking copy is visible");
        foreach(var row in panel.content.GetComponentsInChildren<LeaderboardRowUI>().Take(3).Concat(new[]{panel.ownRow}))
        {
            if(!row.gameObject.activeInHierarchy||!row.medal.enabled)continue;
            row.rank.ForceMeshUpdate();
            var glyph=row.rank.transform.TransformPoint(row.rank.textBounds.center);
            var local=row.medal.transform.InverseTransformPoint(glyph);
            Check(Mathf.Abs(local.x)<1.5f,"Medal number is off center: "+row.rank.text);
            Check(Mathf.Abs(local.y+5.2f)<2,"Medal number leaves medal face: "+row.rank.text);
        }
    }

    sealed class Source : ILeaderboardSource
    {
        public string mode="normal";public int calls,cancels;
        public void Cancel(){cancels++;}
        public IEnumerator Load(LeaderboardKind kind,Action<LeaderboardSnapshot> complete)
        {
            calls++;yield return new WaitForSecondsRealtime(.2f);
            var result=new LeaderboardSnapshot{kind=kind,success=mode!="error",readTime="2026-10-21T14:00:00Z",message="Top 50 players - Published statistics",ownMessage="Not participating. Enable sharing in Account to receive a rank."};
            if(mode=="error"){result.message="Could not load rankings. Check your connection and retry.";complete(result);yield break;}
            if(mode=="empty"){result.message="No players have shared statistics yet.";complete(result);yield break;}
            for(int i=0;i<140;i++) result.leaders.Add(new AccountAggregate{playerId="player-"+i.ToString("D3"),playerName=i==0?"VeryLongPlayerName123":"Cat Adventurer "+i,bossStage=1+i%12,totalTasksCompleted=i*5,totalFocusMinutes=3000-i*9,power=(i%12)*123,catLevel=1+i%30});
            result.leaders.Sort((a,b)=>LeaderboardQuery.Compare(a,b,kind));
            if(mode!="optout") {result.own=result.leaders[mode=="ownTop"?0:137];result.ownRank=mode=="ownTop"?1:138;result.ownMessage=null;}
            result.leaders.RemoveRange(50,90); complete(result);
        }
    }
    static IEnumerator Run()
    {
        var nav=UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();Check(nav && EventSystem.current,"Navigation missing");
        Check(Path.GetFullPath(GameDataManager.EnsureInstance().SavePath)==Path.GetFullPath(Folder+"/save.json"),"Live save used");
        GameObject reference=null;var source=new Source();
        try
        {
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
            {
                string suffix=size.x+"x"+size.y;Resolution(size);nav.ShowPage(MainMenuPage.Leaderboard);yield return 1;
                context="baseline-"+suffix;Capture(context);yield return .2;
                var canvas=new GameObject("Ranking reference",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));reference=canvas;
                canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=200;
                var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(2560,1440);scaler.matchWidthOrHeight=.5f;
                var vendor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePath),canvas.transform);
                var rect=(RectTransform)vendor.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                yield return .4;Capture("reference-"+suffix);yield return .2;UnityEngine.Object.Destroy(reference);reference=null;yield return .2;
                var panel=nav.pages[5].GetComponentInChildren<LeaderboardPanelUI>();if(!panel)continue;
                panel.SetSource(null);
                typeof(LeaderboardPanelUI).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, null);
                panel.Reload(); // Editor script reload may discard this non-serialized interface.
                yield return .4;Check(!panel.Loading && panel.Snapshot != null,"Transient source was not restored");
                context="signed-out-"+suffix;Capture(context);yield return .2;
                source.mode="normal";panel.SetSource(source);yield return .4;
                for(int i=0;i<4;i++)
                {
                    context="category-"+i+"-"+suffix;Click(panel.categories[i]);yield return .4;
                    Check(panel.Snapshot!=null && panel.Snapshot.kind==(LeaderboardKind)i && panel.ownRow.gameObject.activeSelf && panel.ownRow.rank.text=="138","Independent category / out-of-top own rank missing");
                    Check(panel.content.GetComponentsInChildren<LeaderboardRowUI>().Length==50,"Expected top 50 visible models");
                    foreach(var button in panel.categories)Check(!button.GetComponentInChildren<TMP_Text>().isTextOverflowing,"Category text clipped");
                    Layout(panel);
                    Capture(context);yield return .2;
                }
                var buttonToTest=panel.categories[0];var before=Bounds((RectTransform)buttonToTest.transform);var pointer=Pointer(buttonToTest);
                ExecuteEvents.Execute(buttonToTest.gameObject,pointer,ExecuteEvents.pointerEnterHandler);yield return .2;Capture("hover-"+suffix);yield return .1;Stable(before,buttonToTest);
                ExecuteEvents.Execute(buttonToTest.gameObject,pointer,ExecuteEvents.pointerDownHandler);yield return .1;Capture("press-"+suffix);yield return .1;
                ExecuteEvents.Execute(buttonToTest.gameObject,pointer,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(buttonToTest.gameObject,pointer,ExecuteEvents.pointerExitHandler);EventSystem.current.SetSelectedGameObject(null);yield return .7;Reset(buttonToTest.GetComponent<JuicyButton>(),"Button feedback did not reset");
                Click(panel.categories[0]);Click(panel.categories[3]);yield return .4;Check(panel.Snapshot.kind==LeaderboardKind.Power,"Stale category response displayed");
                foreach(string mode in new[]{"ownTop","optout","empty","error"})
                {
                    context=mode+"-"+suffix;source.mode=mode;Click(panel.refresh);yield return .4;
                    Check(!panel.Loading && panel.refresh.IsInteractable(),"Loading stuck");Layout(panel);Capture(context);yield return .2;
                }
                var sidebar=UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
                var expander=(Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sidebar);
                source.mode="ownTop";Click(panel.refresh);yield return .4;
                foreach(bool expanded in new[]{true,false}){
                    expander.isOn=expanded;yield return .6;Layout(panel);Capture("layout-"+suffix+(expanded?"-expanded":"-collapsed"));yield return .2;
                }
                source.mode="normal";Click(panel.refresh);nav.ShowPage(MainMenuPage.Tasks);yield return .3;Check(!panel.Loading,"Closed page still loading");nav.ShowPage(MainMenuPage.Leaderboard);yield return .4;
                Click(panel.accountButton);yield return .3;var settings=UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>();Check(settings && settings.CurrentView=="Account","Account action missing");settings.Close();yield return .2;
                checks.Add("PASS "+suffix+": four categories, rank 138 outside top 50, own top row, states, rapid switch, close/reopen, Account modal, stable hover/press/reset");
            }
        }
        finally{if(reference)UnityEngine.Object.Destroy(reference);}
    }
}
