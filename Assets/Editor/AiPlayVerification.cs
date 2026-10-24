using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AiPlayVerification
{
    const string Flag = "CatQuest.AiPlay";
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static IEnumerator run;
    static double next;
    static RewardScreenshotCapture capture;
    static string folder, context;
    static string loggedError;
    static AiPlayVerification()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += (message, stack, type) => {
            if (SessionState.GetBool(Flag, false) && SessionState.GetBool(Flag + ".entered", false) &&
                (type == LogType.Error || type == LogType.Exception)) loggedError = message + "\n" + stack;
        };
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Flag, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { folder = SessionState.GetString(Flag + ".folder", ""); SessionState.SetBool(Flag + ".entered", true); run = Run(SessionState.GetBool(Flag + ".before", false)); }
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        };
        AssemblyReloadEvents.beforeAssemblyReload += () => {
            if (SessionState.GetBool(Flag, false) && SessionState.GetBool(Flag + ".entered", false) && !SessionState.GetBool(Flag + ".finished", false))
            { folder = SessionState.GetString(Flag + ".folder", ""); Record("FAIL: compilation interrupted the verification."); EditorApplication.isPlaying = false; }
        };
    }
    static EditorWindow View => (EditorWindow)typeof(SettingsPlayVerification).GetProperty("GameView", Hidden).GetValue(null);
    static PropertyInfo Size => (PropertyInfo)typeof(SettingsPlayVerification).GetProperty("SizeProperty", Hidden).GetValue(null);
    static object Group => typeof(SettingsPlayVerification).GetMethod("GameViewGroup", Hidden).Invoke(null, null);
    static int Count(string method) => (int)Group.GetType().GetMethod(method).Invoke(Group, null);
    static void Poll()
    {
        if (run != null && EditorApplication.isPlaying)
        {
            if (loggedError != null) { Record("FAIL: runtime error at " + context + ": " + loggedError); run = null; EditorApplication.isPlaying = false; return; }
            if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Flag + ".deadline", DateTime.UtcNow.ToString("O"))).ToUniversalTime())
            { Record("FAIL: AI Play Mode verification exceeded its deadline."); run = null; EditorApplication.isPlaying = false; return; }
            if (capture && capture.Pending || EditorApplication.timeSinceStartup < next) return;
            try {
                if (!run.MoveNext()) { Record("PASS: rendered captures and exercised AI flow; review images for visual acceptance."); run = null; EditorApplication.isPlaying = false; }
                else { next = EditorApplication.timeSinceStartup + (run.Current is double seconds ? seconds : .1); File.WriteAllText(folder + "/progress.txt", context ?? "starting"); }
            }
            catch (Exception e) { Record("FAIL at " + context + ": " + e); run = null; EditorApplication.isPlaying = false; }
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        bool before = File.Exists("Temp/capture-ai-before.request"), after = File.Exists("Temp/verify-ai-play.request");
        if (!before && !after) return;
        File.Delete(before ? "Temp/capture-ai-before.request" : "Temp/verify-ai-play.request");
        folder = "Library/AiVerification/" + (before ? "Before-" : "Play-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(folder);
        loggedError = null;
        SessionState.SetBool(Flag + ".entered", false); SessionState.SetBool(Flag + ".finished", false);
        SessionState.SetString(Flag + ".deadline", DateTime.UtcNow.AddMinutes(3).ToString("O"));
        File.WriteAllText("Temp/verify-ai-play.result", folder + "/result.txt\nRUNNING");
        SessionState.SetString(Flag + ".folder", folder); SessionState.SetBool(Flag + ".before", before);
        SessionState.SetString(Flag + ".store", Environment.GetEnvironmentVariable("CATQUEST_TASK_TEST_STORE") ?? "");
        SessionState.SetString(Flag + ".aiStore", Environment.GetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY") ?? "");
        SessionState.SetString(Flag + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetInt(Flag + ".size", (int)Size.GetValue(View)); SessionState.SetInt(Flag + ".custom", Count("GetCustomCount"));
        SessionState.SetBool(Flag + ".paused", EditorApplication.isPaused);
        Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", Path.GetFullPath(folder + "/save.json"));
        Environment.SetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY", Path.GetFullPath(folder + "/ai"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/CharacterHome.unity");
        SessionState.SetBool(Flag, true); EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }
    static void Record(string text) { SessionState.SetBool(Flag + ".finished", true); Directory.CreateDirectory(folder); File.WriteAllText(folder + "/result.txt", DateTime.UtcNow.ToString("O") + "\n" + text); File.WriteAllText("Temp/verify-ai-play.result", folder + "/result.txt\n" + text); }
    static void Restore()
    {
        try
        {
            folder = SessionState.GetString(Flag + ".folder", folder);
            if (!SessionState.GetBool(Flag + ".finished", false)) Record("FAIL: Play Mode ended before verification completed.");
            string old = SessionState.GetString(Flag + ".store", ""); Environment.SetEnvironmentVariable("CATQUEST_TASK_TEST_STORE", old.Length == 0 ? null : old);
            old = SessionState.GetString(Flag + ".aiStore", ""); Environment.SetEnvironmentVariable("CATQUEST_AI_TEST_DIRECTORY", old.Length == 0 ? null : old);
            old = SessionState.GetString(Flag + ".scene", ""); EditorSceneManager.playModeStartScene = old.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(old);
            Size.SetValue(View, SessionState.GetInt(Flag + ".size", 0));
            int built = Count("GetBuiltinCount");
            for (int i = Count("GetCustomCount") - 1; i >= SessionState.GetInt(Flag + ".custom", 0); i--) Group.GetType().GetMethod("RemoveCustomSize").Invoke(Group, new object[] { built + i });
            EditorApplication.isPaused = SessionState.GetBool(Flag + ".paused", false);
            File.AppendAllText(folder + "/result.txt", "\nRestored isolated save/config environments, start scene, Game View and pause state.");
        }
        finally { run = null; capture = null; SessionState.SetBool(Flag, false); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        if (!capture) capture = new GameObject("AI verification capture").AddComponent<RewardScreenshotCapture>();
        capture.Begin(Path.GetFullPath(folder + "/" + name + ".png"));
    }
    static void Click(Button button)
    {
        Check(button && button.IsInteractable() && button.gameObject.activeInHierarchy, "Unavailable button " + button?.name);
        var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = (corners[0] + corners[2]) / 2 };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject, "Button blocked " + button.name);
        ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator Run(bool before)
    {
        yield return 1.0;
        var nav = UnityEngine.Object.FindFirstObjectByType<MainMenuNavigation>();
        Check(nav && nav.ShowPage(MainMenuPage.Chat), "Chat navigation missing.");
        var page = UnityEngine.Object.FindFirstObjectByType<AiChatPage>();
        var client = ChatGptClient.EnsureInstance();
        while (client.Busy) yield return .1;
        var fake = new UiHttp(); client.ConfigureForVerification(new UiStore(), fake);
        while (client.Busy) yield return .1;
        client.LoadModels(); while (client.Busy) yield return .1;
        GameDataManager.EnsureInstance().Tasks.SaveTag(null,"Chat verification",out var chatTag);
        page.SendText("Help me plan a short study session."); yield return .2;
        float waveBefore=page.status.textInfo.meshInfo[0].vertices[0].y; Capture("chat-thinking-before"); yield return .2;
        Check(page.thinking.enabled && Mathf.Abs(page.status.textInfo.meshInfo[0].vertices[0].y-waveBefore)>.01f,"Thinking wave did not animate.");Capture("chat-thinking");yield return .2;
        fake.pending.SetResult("{\"message\":\"Here is a task proposal. Review it before saving.\",\"query\":null,\"drafts\":[{\"title\":\"Review lecture notes\",\"description\":\"整理今天的学习笔记，复习重点。\",\"tagIds\":[\""+chatTag+"\"],\"subtasks\":[{\"title\":\"Read the introduction\"},{\"title\":\"Write a short outline\"}]}]}");
        yield return .5;
        Check(!page.thinking.enabled,"Thinking animation did not stop.");
        Check(client.Chat.History.messages.Last().drafts.Count == 1, "No draft card: "+client.Chat.Status);
        foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1024,768), new Vector2Int(2560,1080) }) {
            context = "chat resolution " + size;
            typeof(SettingsPlayVerification).GetMethod("Resolution", Hidden).Invoke(null, new object[] { size });
            var resize = UnityEngine.Object.FindFirstObjectByType<ResisesSideMenuInLayoutGroup>();
            var expander = (Toggle)typeof(ResisesSideMenuInLayoutGroup).GetField("expander",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(resize);
            foreach(bool expanded in new[]{false,true}) {
                expander.isOn=expanded; yield return .5;
                Capture("chat-" + size.x + (expanded?"-expanded":"-collapsed")); yield return .2;
                if (page.content.rect.height > page.scroll.viewport.rect.height) {
                    ExecuteEvents.Execute(page.scroll.gameObject, new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -20) }, ExecuteEvents.scrollHandler);
                    yield return .3;
                    Check(page.scroll.verticalNormalizedPosition < .99f, "Conversation cannot scroll.");
                    page.scroll.verticalNormalizedPosition = 0; yield return .2;
                    Capture("chat-" + size.x + (expanded ? "-expanded-bottom" : "-collapsed-bottom")); yield return .2;
                    page.scroll.verticalNormalizedPosition = 1; yield return .2;
                }
            }
        }
        context = "Sensei avatar and icon controls";
        AiFeatureVerification.VerifyBindings();
        foreach (var row in page.content.GetComponentsInChildren<AiChatRow>())
            Check(row.avatar && row.avatar.activeSelf == (row.speaker.text == "Sensei") && row.userAvatar.activeSelf == (row.speaker.text == "You"), "Avatar shown on wrong speaker.");
        foreach (var button in new[] { page.send, page.stop, page.retry, page.clear, page.settings, page.today, page.plan })
            Check(button.transform.Find("ActionIcon") && button.GetComponent<JuicyButton>(), "Missing icon or shared feedback: " + button.name);
        var draft = client.Chat.History.messages.Last().drafts.Single();
        page.OpenDraft(draft);
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}){
            typeof(SettingsPlayVerification).GetMethod("Resolution",Hidden).Invoke(null,new object[]{size});yield return .4;
            var corners=new Vector3[4];((RectTransform)page.saveDraft.transform).GetWorldCorners(corners);
            Check(corners.All(c=>c.y>=0 && c.y<=Screen.height),"Review save button outside screen.");
            Check(page.editorOverlay.transform.parent==page.GetComponentInParent<Canvas>().rootCanvas.transform,"Review mask not full canvas.");
            Capture("chat-review-"+size.x);yield return .2;
        }
        Click(page.saveDraft); yield return .3;
        Check(draft.state == "saved" && GameDataManager.EnsureInstance().Tasks.Snapshot.tasks.Count(t => t.id == draft.task.id) == 1, "Confirmed task not saved exactly once.");
        page.SendText("Another message"); yield return .2;
        Click(page.stop); fake.pending.SetResult("{\"message\":\"Late reply\",\"drafts\":[]}"); yield return .2;
        Check(client.Chat.History.messages.Last().role == "user", "Late cancelled response leaked.");
        var feedback = page.today.GetComponent<JuicyButton>(); Check(feedback, "Chat feedback missing.");
        var pointer = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(page.today.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return .2;
        Check(feedback.DrawScale.x > 1, "Hover did not animate."); Capture("chat-hover"); yield return .1;
        ExecuteEvents.Execute(page.today.gameObject, pointer, ExecuteEvents.pointerExitHandler); yield return .5;
        Check(Mathf.Abs(feedback.DrawScale.x - 1) < .01f, "Hover did not settle.");
        Check(nav.ShowPage(MainMenuPage.Tasks), "Cannot leave Chat."); yield return .2;
        Check(nav.ShowPage(MainMenuPage.Chat), "Cannot reopen Chat."); yield return .2;
        Capture("chat-final"); yield return .2;
        context="settings and manual tasks after advice removal";
        var launcher=UnityEngine.Object.FindFirstObjectByType<SettingsLauncher>();
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)}) {
            typeof(SettingsPlayVerification).GetMethod("Resolution",Hidden).Invoke(null,new object[]{size}); yield return .3;
            launcher.Open(); yield return .2;
            var settings=UnityEngine.Object.FindFirstObjectByType<SettingsPanelUI>(); settings.OpenAI(); yield return .3;
            Capture("ai-settings-"+size.x); yield return .2;
            var language = settings.GetComponentInChildren<AiSettingsUI>(true).language;
            language.Show(); yield return .2;
            Check(language.transform.Find("Dropdown List"),"Language choices did not open.");
            Capture("ai-language-open-"+size.x); yield return .2;
            language.Hide(); language.value=1; while(client.Busy)yield return .1;
            Check(client.Connection.Language=="en","Language selection was not saved.");

            var model = settings.GetComponentInChildren<AiSettingsUI>(true).model;
            ExecuteEvents.Execute(model.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            yield return .3;
            var list = model.transform.Find("Dropdown List"); Check(list, "Model dropdown did not open.");
            var option = list.GetComponentInChildren<Toggle>(); Check(option && option.GetComponentInChildren<TMP_Text>().text == "mock", "Model option label missing.");
            Capture("ai-model-open-"+size.x); yield return .2;
            option.isOn = true; model.Hide(); yield return .3;
            Check(!model.transform.Find("Dropdown List"), "Model dropdown did not close.");
            settings.Close();
            nav.ShowPage(MainMenuPage.Tasks); yield return .2;
            nav.taskSystem.SelectTab(TaskTab.Todo); nav.taskSystem.SelectTask(draft.task.id); yield return .3;
            Capture("task-details-"+size.x); yield return .2;
            typeof(TaskSystemUI).GetMethod("BeginEdit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav.taskSystem,new object[]{nav.taskSystem.Service.Query(TaskTab.Todo).First(v=>v.task.id==draft.task.id)});
            yield return .3; Capture("task-edit-"+size.x); yield return .2; nav.taskSystem.CancelDraft();
            nav.ShowPage(MainMenuPage.Chat); yield return .2;
            Click(page.clear); yield return .2; Capture("chat-clear-"+size.x); yield return .2; Click(page.cancelClear);
        }
        page.SendText("Make a draft"); yield return .2;
        fake.pending.SetResult("{\"message\":\"Review\",\"drafts\":[{\"title\":\"\"}]}"); yield return .3;
        page.OpenDraft(client.Chat.History.messages.Last().drafts.Single());
        yield return .2; Click(page.saveDraft); yield return .2;
        Check(page.editorOverlay.activeSelf && page.reviewStatus.text.Contains("title"),"Save error was hidden by review modal.");
        Click(page.cancelDraft); yield return .2;
    }
    sealed class UiStore : IChatGptStorage
    {
        readonly Dictionary<string, string> data = new Dictionary<string, string> {
            ["connection.dpapi"] = "{\"client_id\":\"oaiapp_test\",\"refresh_token\":\"test\",\"access_token\":\"test\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":9999999999}",
            ["preferences.json"] = "{\"automatic\":true,\"model\":\"mock\"}" };
        public string Read(string key, bool encrypted) { lock (data) return data.TryGetValue(key, out var value) ? value : null; }
        public void Write(string key, string value, bool encrypted) { lock (data) data[key] = value; }
    }
    sealed class UiHttp : IChatGptHttp
    {
        public TaskCompletionSource<string> pending; public int requests;
        public Task<string> Send(string url, string method, string body, string type, string bearer, bool stream, CancellationToken token)
        {
            if (url.EndsWith("/models")) return Task.FromResult("{\"models\":[{\"visibility\":\"list\",\"slug\":\"mock\"}]}");
            if (url.EndsWith("/responses")) { requests++; pending = new TaskCompletionSource<string>(); return pending.Task; }
            return Task.FromResult("");
        }
    }
}
