using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class DashboardRewardPlayVerification
{
    private const string Flag = "CatQuest.RewardPlayVerification";
    private static int step, oldSize, hoverPhase;
    private static DashboardTaskRow hoverRow;
    private static double next;
    private static EditorWindow gameView;
    private static PropertyInfo sizeProperty;
    private static string[] taskIds;
    private static string folder;
    private static bool readyCaptured;
    private static RewardScreenshotCapture capture;
    static DashboardRewardPlayVerification()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += Poll;
    }
    private static void Poll()
    {
        if (File.Exists("Temp/verification-state.request"))
        {
            File.Delete("Temp/verification-state.request");
            File.WriteAllText("Temp/verification-state.result", $"Playing={EditorApplication.isPlaying}; Compiling={EditorApplication.isCompiling}; Scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists("Temp/verify-task-play.request"))
        {
            File.Delete("Temp/verify-task-play.request"); TaskSystemPlayVerification.Run(); return;
        }
        if (!File.Exists("Temp/verify-dashboard-play.request")) return;
        File.Delete("Temp/verify-dashboard-play.request"); Run();
    }
    [MenuItem("Tools/Cat Quest/Verify Dashboard Rewards Play Flow")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        folder = Path.GetFullPath("Library/DashboardRewardPlayVerification/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        SessionState.SetString(Flag + ".folder", folder);
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.Combine(folder, "save.json"));
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
        {
            folder = SessionState.GetString(Flag + ".folder", "");
            step = 0; hoverPhase=0; readyCaptured = false; next = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Flag, false))
        {
            Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", null);
            SessionState.SetBool(Flag, false); EditorApplication.update -= Tick;
            if (gameView && sizeProperty != null) sizeProperty.SetValue(gameView, oldSize);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Click(Button button)
    {
        Check(button && button.interactable && button.gameObject.activeInHierarchy, "Unavailable button: " + (button ? button.name : "null"));
        button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
    }
    private static void Resolution(int width, int height)
    {
        var assembly = typeof(Editor).Assembly;
        var viewType = assembly.GetType("UnityEditor.GameView");
        if (!gameView)
        {
            gameView = EditorWindow.GetWindow(viewType);
            sizeProperty = viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            oldSize = (int)sizeProperty.GetValue(gameView);
        }
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var getGroup = sizesType.GetMethod("GetGroup");
        var group = getGroup.Invoke(sizes, new[] { Enum.ToObject(getGroup.GetParameters()[0].ParameterType, 0) });
        var fixedType = assembly.GetType("UnityEditor.GameViewSizeType");
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var constructor = sizeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { fixedType, typeof(int), typeof(int), typeof(string) }, null);
        var size = constructor.Invoke(new[] { Enum.ToObject(fixedType, 1), (object)width, height, "Cat Quest Verification" });
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int builtIn = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
        int custom = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
        sizeProperty.SetValue(gameView, builtIn + custom - 1);
        gameView.Show(); gameView.Focus();
    }
    private static void ValidateLayout(MainMenuNavigation nav)
    {
        Canvas.ForceUpdateCanvases();
        var dashboard = nav.pages[0].GetComponent<RectTransform>();
        var bounds = new Vector3[4]; dashboard.GetWorldCorners(bounds);
        foreach (var text in nav.pages[0].GetComponentsInChildren<TMP_Text>())
        {
            if (!text.name.StartsWith("Text") && text.name != "TaskName") continue;
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            Check(corners[0].y >= bounds[0].y - 2 && corners[2].y <= bounds[2].y + 2, "Text outside dashboard: " + text.text);
            Check(!text.isTextOverflowing, "Text clipped: " + text.text);
        }
        var character = nav.pages[0].transform.Find("Top/Character");
        var preview = character.GetComponentInChildren<CharacterHomePreview>();
        if (preview)
        {
            var rect = preview.GetComponent<RawImage>().rectTransform;
            Check(rect.anchorMin.y >= .385f, "Character preview overlaps equipment or level status: " + rect.anchorMin);
        }
    }
    private static void Capture(string name)
    {
        if (!capture) capture = new GameObject("Reward Screenshot Verification").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.Combine(folder, name));
        File.WriteAllText("Temp/dashboard-reward-screenshots.path", folder);
    }
    private static void ValidateRowFeedback(bool hovering)
    {
        var feedback = hoverRow.GetComponent<JuicyButton>();
        Check(feedback && feedback.isActiveAndEnabled, "Dashboard row feedback missing or disabled.");
        if (hovering)
        {
            Check(feedback.DrawScale.y > 1.005f && feedback.DrawOffset > .5f, "Row did not lift and stretch on hover.");
            Check(feedback.DrawScale.x >= .99f && feedback.DrawScale.x <= 1.06f &&
                feedback.DrawScale.y <= 1.1f && feedback.DrawOffset <= 3.5f, "Row hover exceeded its visual bounds.");
            var mask = hoverRow.GetComponentInParent<RectMask2D>();
            if (mask)
            {
                var rect = (RectTransform)hoverRow.transform;
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var point = rect.InverseTransformPoint(corner);
                    point = (Vector3)rect.rect.center + Vector3.Scale(point - (Vector3)rect.rect.center,
                        new Vector3(feedback.DrawScale.x, feedback.DrawScale.y, 1)) + Vector3.up * feedback.DrawOffset;
                    float x = mask.rectTransform.InverseTransformPoint(rect.TransformPoint(point)).x;
                    Check(x >= mask.rectTransform.rect.xMin - 1 && x <= mask.rectTransform.rect.xMax + 1,
                        "Hovered row extends outside its scroll viewport horizontally.");
                }
            }
        }
        else Check(Vector2.Distance(feedback.DrawScale, Vector2.one) < .005f &&
            Mathf.Abs(feedback.DrawOffset) < .1f && feedback.DrawShine < .01f, "Row feedback did not reset after pointer exit.");
    }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (capture && capture.Pending) return;
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
            Check(nav, "No MainMenu navigation in the open scene.");
            var service = GameDataManager.EnsureInstance().Tasks;
            if (step == 0)
            {
                Check(service.Snapshot.tasks.Count == 0, "Test did not use an isolated store.");
                GameDataManager.Instance.SetUsername("Mochi");
                var header=UnityEngine.Object.FindFirstObjectByType<CatStatusUI>();header.enabled=false;header.enabled=true;
                nav.pages[0].SetActive(false); nav.pages[0].SetActive(true);
                Check(UnityEngine.Object.FindFirstObjectByType<CatStatusUI>().playerName.text == "Mochi", "Dashboard did not show the player's name.");
                Check(nav.CurrentPage == MainMenuPage.Dashboard, "Dashboard is not initial page.");
                taskIds = new string[3];
                var titles = new[] { "Read a chapter", "Finish project notes", "Review presentation" };
                for (int i = 0; i < 3; i++)
                {
                    Check(service.Upsert(new QuestTask { title = titles[i], difficulty = (TaskDifficulty)i,
                        daily = i == 0, scheduled = true, date = TaskService.DateKey(service.LocalNow) }, null, out taskIds[i]), service.LastError);
                }
                Resolution(1280, 720); next = EditorApplication.timeSinceStartup + 2; step++; return;
            }
            if(step==1 && hoverPhase==0){
                hoverRow=nav.pages[0].GetComponentInChildren<DashboardTaskRow>();
                Check(hoverRow,"Dashboard task row missing");
                hoverRow.GetComponent<JuicyButton>().OnPointerEnter(new PointerEventData(EventSystem.current));
                hoverPhase=1;next=EditorApplication.timeSinceStartup+.5;return;
            }
            if(step==1 && hoverPhase==1){
                Check(hoverRow.completeButton.GetComponent<JuicyButtonMesh>(),"Nested checkbox missing shared feedback");
                ValidateRowFeedback(true);
                Capture("dashboard-row-hover.png");hoverPhase=2;next=EditorApplication.timeSinceStartup+.5;return;
            }
            if(step==1 && hoverPhase==2){
                hoverRow.GetComponent<JuicyButton>().OnPointerExit(new PointerEventData(EventSystem.current));
                hoverRow.GetComponent<JuicyButton>().OnDeselect(new BaseEventData(EventSystem.current));
                hoverPhase=3;next=EditorApplication.timeSinceStartup+.5;return;
            }
            if (step == 1)
            { ValidateRowFeedback(false); ValidateLayout(nav); Capture("dashboard-1280x720.png"); next = EditorApplication.timeSinceStartup + 1; step++; return; }
            if (step == 2)
            { Resolution(1920, 1080); next = EditorApplication.timeSinceStartup + 2; step++; return; }
            if (step == 3)
            { ValidateLayout(nav); Capture("dashboard-1920x1080.png"); next = EditorApplication.timeSinceStartup + 1; step++; return; }
            if (step == 4)
            {
                foreach (var id in taskIds) Check(service.Complete(id, true), service.LastError);
                var goals = nav.pages[0].GetComponentsInChildren<DailyGoalUI>();
                var quests = goals.Single(g => g.goal == DailyRewardGoal.ThreeQuests);
                var focusGoal = goals.Single(g => g.goal == DailyRewardGoal.Focus25Minutes);
                Check(quests.claim.interactable && !focusGoal.claim.interactable && quests.progress.text == "3 / 3", "Independent goal UI failed.");
                if (!readyCaptured) { Capture("dashboard-ready-1920x1080.png"); readyCaptured = true; next = EditorApplication.timeSinceStartup + 1; return; }
                Click(quests.claim);
                Check(quests.claimLabel.text == "Claimed" && service.Snapshot.experience == 80 && service.Snapshot.fishCoins == 40, "Quest claim did not refresh UI/balance.");
                Click(nav.pages[0].GetComponentInChildren<FocusPanelUI>().start);
                Check(nav.CurrentPage == MainMenuPage.Focus && FocusRunner.EnsureInstance().Controller.Phase == FocusPhase.Focusing, "Quick Start did not start real Focus.");
                next = EditorApplication.timeSinceStartup + 2; step++; return;
            }
            if (step == 5)
            {
                Capture("focus-running-1920x1080.png");
                next = EditorApplication.timeSinceStartup + 1; step++; return;
            }
            if (step == 6)
            {
                Click(nav.buttons[0]);
                Check(FocusRunner.EnsureInstance().Controller.Phase == FocusPhase.Focusing, "Page switch stopped Focus.");
                var controller = FocusRunner.EnsureInstance().Controller;
                for (int i = 0; i < 1500 && controller.Phase == FocusPhase.Focusing; i++) controller.Advance(1);
                Check(controller.Phase == FocusPhase.Resting && service.Snapshot.tickets == 1, "Focus completion did not settle/start Rest.");
                var goal = nav.pages[0].GetComponentsInChildren<DailyGoalUI>().Single(g => g.goal == DailyRewardGoal.Focus25Minutes);
                Click(goal.claim);
                Check(service.Snapshot.experience == 125 && service.Snapshot.fishCoins == 60 && nav.tickets.text == "1", "Focus claim or HUD wrong.");
                var cat = UnityEngine.Object.FindFirstObjectByType<CatStatusUI>();
                Check(cat.level.text == "LV 2" && cat.experience.text == "25 / 125", "Cat status did not level up from rewards.");
                Click(nav.buttons[2]); next = EditorApplication.timeSinceStartup + 1; step++; return;
            }
            if (step == 7)
            {
                var panel = nav.pages[2].GetComponent<FocusPanelUI>();
                Check(panel.status.text == "Rest", "Rest UI not shown.");
                Capture("focus-rest-1920x1080.png");
                next = EditorApplication.timeSinceStartup + 1; step++; return;
            }
            if (step == 8)
            {
                var panel = nav.pages[2].GetComponent<FocusPanelUI>();
                Click(panel.endRest);
                Check(panel.start.gameObject.activeSelf && panel.start.GetComponentInChildren<TMP_Text>().text == "Start Another Focus", "Rest did not offer next actions.");
                Click(panel.start); Click(panel.cancel);
                Check(FocusRunner.EnsureInstance().Controller.Phase == FocusPhase.Interrupted && service.Snapshot.experience == 125, "Cancelled session rewarded or entered Rest.");
                Click(nav.buttons[(int)MainMenuPage.Dashboard]);
                Check(nav.CurrentPage == MainMenuPage.Dashboard, "Sidebar did not return to Dashboard.");
                next = EditorApplication.timeSinceStartup + 1; step++; return;
            }
            if (step == 9)
            {
                ValidateLayout(nav); Capture("dashboard-claimed-1920x1080.png");
                var disk = new PlayerDataStore(GameDataManager.Instance.SavePath);
                Check(disk.Data.rewardDays.Single().questsClaimed && disk.Data.rewardDays.Single().focusClaimed && disk.Data.tickets == 1, "Claims did not persist.");
                next = EditorApplication.timeSinceStartup + 1; step++; return;
            }
            File.WriteAllText("Temp/dashboard-reward-play.verification", "ALL PASS: real Dashboard UI, independent Claims, Focus quick start, navigation continuity, Rest/cancel, level/HUD refresh, persistence, 1280x720/1920x1080 layout. Screenshots: " + folder);
            EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
        }
        catch (Exception ex)
        {
            File.WriteAllText("Temp/dashboard-reward-play.verification", "FAIL step " + step + ": " + ex);
            Debug.LogException(ex); EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
        }
    }
}

public sealed class RewardScreenshotCapture : MonoBehaviour
{
    public bool Pending { get; private set; }
    public void Begin(string path) { Pending = true; StartCoroutine(Save(path)); }
    private IEnumerator Save(string path)
    {
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Destroy(texture); Pending = false; }
    }
}


