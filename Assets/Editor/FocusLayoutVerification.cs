using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Opt-in check, using a disposable save rather than the player's quests or rewards.
[InitializeOnLoad]
public static class FocusLayoutVerification
{
    const string Flag = "CatQuest.FocusLayoutCheck";
    const string Folder = "Library/FocusLayoutVerification";
    static int step;
    static double next;
    static RewardScreenshotCapture capture;
    static string questId;
    static FocusLayoutVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = 0; next = EditorApplication.timeSinceStartup + 3; }
            if (state == PlayModeStateChange.EnteredEditMode) {
                SessionState.SetBool(Flag, false);
                Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", null);
                File.WriteAllText(Folder + "/done.txt", "Returned to Edit Mode");
            }
        };
    }
    static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
    static void Resolution(int width, int height) => typeof(DashboardRewardPlayVerification).GetMethod("Resolution", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { width, height });
    static void Capture(string name)
    {
        if (!capture) capture = new GameObject("Focus Layout Capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(Folder + "/" + name + ".png"));
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Flag, false)) {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next || (capture && capture.Pending)) return;
            try { Tick(); } catch (Exception e) { File.WriteAllText(Folder + "/result.txt", e.ToString()); EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/verify-focus-layout.request")) return;
        File.Delete("Temp/verify-focus-layout.request"); Directory.CreateDirectory(Folder);
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(Folder + "/session-" + Guid.NewGuid().ToString("N") + ".json"));
        EditorSceneManager.OpenScene("Assets/Scenes/CharacterHome.unity");
        SessionState.SetBool(Flag, true); Resolution(1920, 1080); EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        var panel = nav.pages[2].GetComponent<FocusPanelUI>();
        var service = GameDataManager.Instance.Tasks;
        var focus = FocusRunner.EnsureInstance().Controller;
        Check(GameDataManager.Instance.SavePath.Contains("FocusLayoutVerification"), "Verification must use isolated save");
        if (step == 0) { Check(nav.ShowPage(MainMenuPage.Focus), "Cannot open Focus"); }
        if (step == 1) {
            Check((!panel.status || !panel.status.gameObject.activeSelf) && (!panel.message || !panel.message.gameObject.activeSelf), "Extra labels on sky");
            var heading=panel.transform.Find("Text");
            Check((!heading || !heading.gameObject.activeSelf) && (!panel.dashboard || !panel.dashboard.gameObject.activeInHierarchy), "Page heading or Return to Dashboard still visible");
            var preview = panel.GetComponentInChildren<FocusScenePreview>();
            Check(preview.transform.IsChildOf(panel.transform.Find("background")), "Scene must be inside Focus background");
            Check(preview.GetComponent<RectTransform>().anchorMin == Vector2.zero && preview.GetComponent<RectTransform>().anchorMax == Vector2.one, "Scene does not fill frame");
            Check(service.Upsert(new QuestTask { title = "Read chapter", daily = false }, null, out questId), service.LastError);
            Check(service.Upsert(new QuestTask { title = "Practice piano", daily = false }, null, out _), service.LastError);
            panel.focusDuration.value = 37; panel.restDuration.value = 7;
            Check(focus.Options.focusMinutes == 37 && focus.Options.restMinutes == 7, "Sliders not bound");
            Check(panel.rewards.text == "37" && panel.fishReward.text == "14" && panel.ticketReward.text == "1", "Reward numbers incorrect");
            Capture("focus-ready-1920");
        }
        if (step == 2) {
            panel.questDropdown.onClick.Invoke(); panel.questSearch.text = "READ";
            var buttons = panel.questList.GetComponentsInChildren<Button>();
            Check(buttons.Length == 2 && buttons.Any(b => b.name == questId), "Case-insensitive quest search failed");
            Capture("focus-search-1920");
        }
        if (step == 3) {
            panel.questSearch.text = "zzz-no-match";
            Check(panel.questList.GetComponentsInChildren<Button>().Length == 1, "Empty search should keep free focus");
            panel.questSearch.text = "chapter";
        }
        if (step == 4) {
            panel.questList.GetComponentsInChildren<Button>().Single(b => b.name == questId).onClick.Invoke();
            Check(focus.Options.questId == questId && !panel.questPopup.gameObject.activeSelf && panel.questSearch.text == "", "Selection did not close or bind");
            panel.start.onClick.Invoke();
            Check(focus.Phase == FocusPhase.Focusing && !panel.questSearch.interactable && !panel.restDuration.interactable, "Session settings not locked");
            Capture("focus-running-1920");
        }
        if (step == 5) {
            for (int i=0; i<focus.FocusSeconds && focus.Phase==FocusPhase.Focusing; i++) focus.Advance(1);
            Check(focus.Phase == FocusPhase.Resting && focus.RestSeconds == 420 && panel.rewards.text == "37", "Rest/rewards changed");
            Capture("focus-rest-1920");
        }
        if (step == 6) {
            panel.endRest.onClick.Invoke(); Resolution(1280, 720);
            Check(panel.questSearch.interactable && panel.focusDuration.interactable, "Settings not unlocked");
        }
        if (step == 7) Capture("focus-ready-1280");
        if (step == 8) {
            File.WriteAllText(Folder + "/result.txt", "PASS: full-frame scene, timer only, custom Focus/Rest lengths, reward preview and settlement, case-insensitive search, no-match state, quest selection, locked running settings and restored finished settings. Screenshots at 1920x1080 and 1280x720.");
            EditorApplication.isPlaying = false; return;
        }
        step++; next = EditorApplication.timeSinceStartup + 1;
    }
}
